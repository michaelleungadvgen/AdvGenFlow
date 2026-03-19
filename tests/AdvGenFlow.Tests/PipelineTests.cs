using AdvGenFlow;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdvGenFlow.Tests;

// Must be public for dynamic dispatch from Mediator (cross-assembly DLR requirement)
public record EchoRequest(string Value) : IRequest<string>;

public class EchoHandler : IRequestHandler<EchoRequest, string>
{
    public Task<string> Handle(EchoRequest request, CancellationToken cancellationToken)
        => Task.FromResult(request.Value);
}

public class PrefixBehavior(string prefix, List<string> log) : IPipelineBehavior<EchoRequest, string>
{
    public async Task<string> Handle(EchoRequest request, RequestHandlerDelegate<string> next,
        CancellationToken cancellationToken)
    {
        log.Add($"{prefix}-enter");
        var result = await next();
        log.Add($"{prefix}-exit");
        return result;
    }
}

public class ShortCircuitBehavior : IPipelineBehavior<EchoRequest, string>
{
    public Task<string> Handle(EchoRequest request, RequestHandlerDelegate<string> next,
        CancellationToken cancellationToken)
        => Task.FromResult("short-circuited"); // does NOT call next()
}

public class PipelineTests
{
    [Fact]
    public async Task Send_WithBehaviors_ExecutesInRegistrationOrder()
    {
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddTransient<IMediator, Mediator>();
        services.AddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
        services.AddTransient<IRequestHandler<EchoRequest, string>, EchoHandler>();
        // B1 registered first → outermost (runs first on entry, last on exit)
        services.AddTransient<IPipelineBehavior<EchoRequest, string>>(
            _ => new PrefixBehavior("B1", log));
        services.AddTransient<IPipelineBehavior<EchoRequest, string>>(
            _ => new PrefixBehavior("B2", log));

        var sender = services.BuildServiceProvider().GetRequiredService<ISender>();
        var result = await sender.Send(new EchoRequest("hello"));

        result.Should().Be("hello");
        // B1 is outermost: enters first, exits LAST (after B2 exits)
        log.Should().Equal("B1-enter", "B2-enter", "B2-exit", "B1-exit");
    }

    [Fact]
    public async Task Send_BehaviorCanShortCircuit()
    {
        var services = new ServiceCollection();
        services.AddTransient<IMediator, Mediator>();
        services.AddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
        services.AddTransient<IRequestHandler<EchoRequest, string>, EchoHandler>();
        services.AddTransient<IPipelineBehavior<EchoRequest, string>>(
            _ => new ShortCircuitBehavior());

        var sender = services.BuildServiceProvider().GetRequiredService<ISender>();
        var result = await sender.Send(new EchoRequest("ignored"));

        result.Should().Be("short-circuited");
    }
}
