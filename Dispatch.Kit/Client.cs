using Dispatch.Kit.Services;

namespace Dispatch.Kit;

public class Client(HttpClient http)
{
    public HealthService Health => new(http);
}