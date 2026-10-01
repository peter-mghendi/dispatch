using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Dispatch.Web;
using MudBlazor.Services;

const string server = nameof(server);

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddServiceDiscovery();
builder.Services.ConfigureHttpClientDefaults(http =>
{
    http.AddStandardResilienceHandler();
    http.AddServiceDiscovery();
});

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();
builder.Services.AddHttpClient(server, client => client.BaseAddress = new Uri($"https+http://{server}"));

builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient(server));

await builder.Build().RunAsync();