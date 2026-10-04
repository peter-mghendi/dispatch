namespace Dispatch.Kit.Services;

public class HealthService(HttpClient http)
{
    public async Task<string> FetchAsync() => await http.GetStringAsync("/ready");
}