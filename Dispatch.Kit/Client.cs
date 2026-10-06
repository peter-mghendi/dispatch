using Dispatch.Kit.Services;

namespace Dispatch.Kit;

public class Client(HttpClient http)
{
    public EndpointService Endpoints => new(http);

    public HandlerService Handlers => new(http);

    public HealthService Health => new(http);
}