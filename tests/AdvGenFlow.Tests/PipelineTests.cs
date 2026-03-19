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

public class TrackingEchoHandler(Action onHandle) : IRequestHandler<EchoRequest, string>
{
    public Task<string> Handle(EchoRequest request, CancellationToken cancellationToken)
    {
        onHandle();
        return Task.FromResult(request.Value);
    }
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

public class TokenCapturingBehavior(Action<CancellationToken> capture) : IPipelineBehavior<EchoRequest, string>
{
    public Task<string> Handle(EchoRequest request, RequestHandlerDelegate<string> next,
        CancellationToken cancellationToken)
    {
        capture(cancellationToken);
        return next();
    }
}

public class PipelineTests
{
    private static ISender BuildSender(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddTransient<IMediator, Mediator>();
        services.AddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
        services.AddTransient<IRequestHandler<EchoRequest, string>, EchoHandler>();
        configure?.Invoke(services);
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    [Fact]
    public async Task Send_WithBehaviors_ExecutesInRegistrationOrder()
    {
        var log = new List<string>();
        var sender = BuildSender(s =>
        {
            // B1 registered first → outermost (runs first on entry, last on exit)
            s.AddTransient<IPipelineBehavior<EchoRequest, string>>(
                _ => new PrefixBehavior("B1", log));
            s.AddTransient<IPipelineBehavior<EchoRequest, string>>(
                _ => new PrefixBehavior("B2", log));
        });

        var result = await sender.Send(new EchoRequest("hello"));

        result.Should().Be("hello");
        // B1 is outermost: enters first, exits LAST (after B2 exits)
        log.Should().Equal("B1-enter", "B2-enter", "B2-exit", "B1-exit");
    }

    [Fact]
    public async Task Send_BehaviorCanShortCircuit()
    {
        bool handlerCalled = false;
        var sender = BuildSender(s =>
        {
            // Override the default EchoHandler with a tracking one
            s.AddTransient<IRequestHandler<EchoRequest, string>>(
                _ => new TrackingEchoHandler(() => handlerCalled = true));
            s.AddTransient<IPipelineBehavior<EchoRequest, string>>(
                _ => new ShortCircuitBehavior());
        });

        var result = await sender.Send(new EchoRequest("ignored"));

        result.Should().Be("short-circuited");
        handlerCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Send_WithBehaviors_PropagatesCancellationTokenThroughPipeline()
    {
        CancellationToken capturedToken = default;

        var cts = new CancellationTokenSource();
        var sender = BuildSender(s =>
        {
            s.AddTransient<IPipelineBehavior<EchoRequest, string>>(
                _ => new TokenCapturingBehavior(ct => capturedToken = ct));
        });

        await sender.Send(new EchoRequest("test"), cts.Token);

        capturedToken.Should().Be(cts.Token);
    }
}
