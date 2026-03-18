using AdvGenFlow;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdvGenFlow.Tests;

public record PingCommand(string Message) : IRequest<string>;

public class PingHandler : IRequestHandler<PingCommand, string>
{
    public Task<string> Handle(PingCommand request, CancellationToken cancellationToken)
        => Task.FromResult($"pong:{request.Message}");
}

public class CapturingHandler(Action<CancellationToken> capture) : IRequestHandler<PingCommand, string>
{
    public Task<string> Handle(PingCommand request, CancellationToken cancellationToken)
    {
        capture(cancellationToken);
        return Task.FromResult("ok");
    }
}

public class SenderTests
{
    private static ISender BuildSender(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddTransient<IMediator, Mediator>();
        services.AddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
        services.AddTransient<IRequestHandler<PingCommand, string>, PingHandler>();
        configure?.Invoke(services);
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    [Fact]
    public async Task Send_WithRegisteredHandler_ReturnsHandlerResult()
    {
        var sender = BuildSender();
        var result = await sender.Send(new PingCommand("hello"));
        result.Should().Be("pong:hello");
    }

    [Fact]
    public async Task Send_WithNoHandler_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddTransient<IMediator, Mediator>();
        services.AddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
        var sender = services.BuildServiceProvider().GetRequiredService<ISender>();

        var act = () => sender.Send(new PingCommand("x"));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Send_PropagatesCancellationToken()
    {
        CancellationToken capturedToken = default;

        var services = new ServiceCollection();
        services.AddTransient<IMediator, Mediator>();
        services.AddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
        services.AddTransient<IRequestHandler<PingCommand, string>>(
            _ => new CapturingHandler(ct => capturedToken = ct));
        var sender = services.BuildServiceProvider().GetRequiredService<ISender>();

        var cts = new CancellationTokenSource();
        await sender.Send(new PingCommand("x"), cts.Token);

        capturedToken.Should().Be(cts.Token);
    }
}
