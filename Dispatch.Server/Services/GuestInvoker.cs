// The only file that touches WACS. Everything else in HandlerRunner is engine-agnostic.
//
// The harness DLL is loaded by reflection instead of being referenced at compile time:
// `wacs harness` emits it against System.Private.CoreLib, which a net10 project cannot
// reference (CS0012). At run time the CLR resolves that identity to the running core library,
// so loading and calling it dynamically works fine.
//
// UNVERIFIED: member names are discovered by convention (see Bind/Build/Read). If the
// harness's shape differs, the InvalidOperationException lists what is actually there.

using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using Wacs.Core.Runtime;
using Wacs.Core.Runtime.Types;
using Wacs.WASI.Preview1;
using Wacs.WASI.Preview2;
using HandleCore = System.Func<int, int, int, int, int, int, int, int, int>;

namespace Dispatch.Server.Services;

internal static class GuestInvoker
{
    private const string HarnessAssembly = "Dispatch.Guest.dll";
    private const string HarnessType = "Dispatch.Guest.DispatchHarness";
    private const string HarnessModule = "harness"; // HarnessLoader.Load registers the core module under this name
    private const string Preview1 = "wasi_snapshot_preview1";
    private const string HandleExport = "dispatch:guest/handler@2.0.0#handle";

    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

    private static readonly Lazy<(MethodInfo LoadFrom, MethodInfo Handle, FieldInfo Core)> Harness = new(Bind);

    // WACS only counts executed instructions (fuel) when a limit is set, so set one that can't be reached.
    private static InvokerOptions Metered => new() { GasLimit = long.MaxValue };

    public static (HandlerRunner.Response Response, HandlerRunner.Fuel Fuel) Invoke(byte[] component, HandlerRunner.Request request)
    {
        var (loadFrom, handle, core) = Harness.Value;
        using var preview1 = new Wasi(Sandbox());
        WasmRuntime? runtime = null;

        try
        {
            // Fresh instance per call for now: wasm linear memory persists between calls, and instances are not thread-safe.
            // TODO: [Cache] Maybe cache instances later
            var instance = loadFrom.Invoke(null, [component, (Action<WasmRuntime>)(bound => BindImports(runtime = bound, preview1))]);

            var loaded = runtime!;
            long initializeFuel = 0, handlerFuel = 0;

            // WASI reactors (e.g. Go's wasip1 c-shared) must run _initialize before any other export.
            // A component instantiation would do this; the harness instantiates the core module directly.
            if (loaded.TryGetExportedFunction((HarnessModule, "_initialize"), out var initialize))
            {
                loaded.CreateInvokerAction(initialize, Metered)();
                initializeFuel = loaded.ExecContext.steps;
            }

            // The harness calls handle through an unmetered invoker, then calls post-return, which resets the
            // count. Swap in a metered invoker that reads the count as soon as handle itself returns.
            if (!loaded.TryGetExportedFunction((HarnessModule, HandleExport), out var export))
                throw new InvalidOperationException($"The component does not export '{HandleExport}'.");

            var metered = loaded.CreateInvokerFunc<int, int, int, int, int, int, int, int, int>(export, Metered);
            core.SetValue(instance, (HandleCore)((a0, a1, a2, a3, a4, a5, a6, a7) =>
            {
                var result = metered(a0, a1, a2, a3, a4, a5, a6, a7);
                handlerFuel = loaded.ExecContext.steps;
                return result;
            }));

            var input = Build(
                handle.GetParameters()[0].ParameterType,
                new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["method"] = request.Method,
                    ["path"] = request.Path ?? "/",
                    ["headers"] = request.Headers,
                    ["body"] = request.Body,
                }
            );

            var output = handle.Invoke(instance, [input])
                         ?? throw new InvalidOperationException("The handler returned null.");

            return (Read(output), new HandlerRunner.Fuel(initializeFuel, handlerFuel));
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            // Surface the guest's own failure (trap, validation error, ...) instead of the reflection wrapper.
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
    }

    // Host imports. The harness instantiates the component's main core module directly, without the
    // shim/fixup wiring that routes its wasi_snapshot_preview1 imports to the embedded adapter, so
    // the host supplies Preview 1 itself alongside Preview 2.
    private static void BindImports(WasmRuntime runtime, Wasi preview1)
    {
        preview1.BindToRuntime(runtime);
        runtime.UseWasiPreview2(_ => { });

        // wit-component renames a duplicated import to "<name> [v2]"; point it at the same host function.
        foreach (var (_, name) in runtime.EnumerateBoundEntities().Where(id => id.Module == Preview1).ToList())
        {
            if (runtime.TryGetExportedFunction((Preview1, name), out var function))
                runtime.BindExternal((Preview1, $"{name} [v2]"), new ExternalValue.Function(function));
        }

        // Adapter-only extension, not part of Preview 1. Without the adapter there is no clock to pause.
        runtime.BindHostFunction<Action<int>>((Preview1, "adapter_monotonic_clock_set_paused"), _ => { });
    }

    // Preview 1 insists on a root directory even when it is not preopened; give it an empty one.
    private static readonly Lazy<string> SandboxRoot = new(() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "dispatch-guest-root")).FullName);

    // Deliberately not Wasi.DefaultConfiguration(): that copies the host's environment variables
    // (connection strings, credentials) into the guest and preopens the working directory.
    private static WasiConfiguration Sandbox() => new()
    {
        HostRootDirectory = SandboxRoot.Value,
        PreopenHostRootDirectory = false,
        StandardInput = Stream.Null,
        StandardOutput = Stream.Null,
        StandardError = Stream.Null,
        AllowFileCreation = false,
        AllowFileDeletion = false,
        AllowNetworkSockets = false,
    };

    private static (MethodInfo LoadFrom, MethodInfo Handle, FieldInfo Core) Bind()
    {
        var path = Path.Combine(AppContext.BaseDirectory, HarnessAssembly);
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);

        var harness = assembly.GetType(HarnessType)
                      ?? assembly.GetExportedTypes().FirstOrDefault(type => type.Name.EndsWith("Harness"))
                      ?? throw Mismatch("a '*Harness' type", assembly.GetExportedTypes());

        var loadFrom = harness.GetMethod(
                           "LoadFrom",
                           BindingFlags.Public | BindingFlags.Static,
                           binder: null,
                           [typeof(byte[]), typeof(Action<WasmRuntime>)],
                           modifiers: null)
                       ?? throw Mismatch("a static LoadFrom(byte[], Action<WasmRuntime>)", harness);

        var handle = loadFrom.ReturnType.GetMethods()
                         .Where(method => method.Name.EndsWith("Handle", StringComparison.Ordinal))
                         .ToArray() is [var only]
            ? only
            : throw Mismatch("exactly one '*Handle' method on the loaded harness", loadFrom.ReturnType);

        if (handle.GetParameters().Length != 1)
            throw Mismatch("a Handle method taking a single request", loadFrom.ReturnType);

        // The delegate the harness calls handle's core export through, replaced per instance to meter it.
        var core = loadFrom.ReturnType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                       .Where(field => field.Name.StartsWith("_invoke_", StringComparison.Ordinal))
                       .ToArray() is [var invoker] && invoker.FieldType == typeof(HandleCore)
            ? invoker
            : throw Mismatch($"exactly one private '_invoke_*' field of type {typeof(HandleCore)}", loadFrom.ReturnType);

        return (loadFrom, handle, core);
    }

    private static object Build(Type type, Dictionary<string, object?> values)
    {
        var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault()
                          ?? throw Mismatch($"a public constructor on {type.Name}", type);

        var parameters = constructor.GetParameters();

        // The harness emits constructors without parameter names; their order follows the
        // record's fields, which is also the declaration order of its properties.
        var properties = type.GetProperties(Flags);
        var names = parameters
            .Select((parameter, i) => parameter.Name is [_, ..] name ? name : properties.ElementAtOrDefault(i)?.Name)
            .ToArray();

        var arguments = parameters
            .Select((parameter, i) => values.TryGetValue(names[i] ?? string.Empty, out var value)
                ? Coerce(value, parameter.ParameterType)
                : throw Mismatch($"constructor parameters named {string.Join(", ", values.Keys)}", type))
            .ToArray();

        var instance = constructor.Invoke(arguments);

        // Records emitted with a parameterless constructor + settable properties.
        if (parameters.Length == 0)
        {
            foreach (var (name, value) in values)
            {
                if (type.GetProperty(name, Flags) is { CanWrite: true } property)
                    property.SetValue(instance, Coerce(value, property.PropertyType));
            }
        }

        return instance;
    }

    /// <summary>Adapts our plain .NET values to whatever list/tuple shapes the harness emitted.</summary>
    private static object? Coerce(object? value, Type target)
    {
        if (value is null || target.IsInstanceOfType(value)) return value;

        // (name, value) pair -> Tuple<,>, KeyValuePair<,> or similar.
        if (value is ITuple { Length: 2 } pair && target.IsGenericType && target.GetGenericArguments() is [var first, var second])
            return Activator.CreateInstance(target, Coerce(pair[0], first), Coerce(pair[1], second));

        // Sequence -> array, List<T>, IReadOnlyList<T>, ...
        if (value is IEnumerable items and not string)
        {
            var elementType = target.IsArray
                ? target.GetElementType()
                : target.IsGenericType ? target.GetGenericArguments()[0] : null;

            if (elementType is not null)
            {
                Array array;
                if (value is byte[] bytes && elementType == typeof(byte))
                {
                    array = bytes;
                }
                else
                {
                    var converted = items.Cast<object?>().Select(item => Coerce(item, elementType)).ToList();
                    array = Array.CreateInstance(elementType, converted.Count);
                    for (var i = 0; i < converted.Count; i++) array.SetValue(converted[i], i);
                }

                return target.IsInstanceOfType(array) ? array : Activator.CreateInstance(target, array);
            }
        }

        throw new InvalidOperationException($"Cannot convert {value.GetType()} to {target}.");
    }

    private static HandlerRunner.Response Read(object output)
    {
        var status = Convert.ToInt32(Member(output, "Status"));

        var headers = ((IEnumerable)Member(output, "Headers")).Cast<object>().Select(Pair).ToArray();

        var body = Member(output, "Body") switch
        {
            byte[] bytes => bytes,
            IEnumerable items => items.Cast<byte>().ToArray(),
            var other => throw Mismatch($"Body to be a byte sequence, got {other.GetType()}", output.GetType()),
        };

        return new HandlerRunner.Response(status, headers, body);
    }

    private static (string, string) Pair(object item) => item switch
    {
        ITuple { Length: 2 } tuple => ((string)tuple[0]!, (string)tuple[1]!),
        _ => ((string)Member(item, "Key"), (string)Member(item, "Value")),
    };

    private static object Member(object target, string name)
    {
        var type = target.GetType();

        return type.GetProperty(name, Flags)?.GetValue(target)
               ?? type.GetField(name, Flags)?.GetValue(target)
               ?? throw Mismatch($"a member named '{name}'", type);
    }

    private static InvalidOperationException Mismatch(string expected, params Type[] types)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var surface = types.SelectMany(type => type.GetMembers(all).Select(member => $"  {type.FullName}: {member}"));

        return new InvalidOperationException(
            $"Harness mismatch: expected {expected}. Public surface:{Environment.NewLine}{string.Join(Environment.NewLine, surface)}"
        );
    }
}