using Dispatch.Kit;
using Dispatch.Web;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Http.Resilience;
using MudBlazor.Services;

const string server = nameof(server);

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddServiceDiscovery();
builder.Services.ConfigureHttpClientDefaults(http =>
{
    http.AddServiceDiscovery();
    http.AddStandardResilienceHandler().Configure(resilience =>
    {
        resilience.AttemptTimeout.Timeout = TimeSpan.FromMinutes(2);
        resilience.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(4);
        resilience.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(6);
    });
});

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();
builder.Services.AddHttpClient(server, client => client.BaseAddress = new Uri($"https+http://{server}"));
builder.Services.AddScoped<Client>();

builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient(server));

await builder.Build().RunAsync();