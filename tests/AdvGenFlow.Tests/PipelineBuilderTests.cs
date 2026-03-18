using AdvGenFlow;
using AwesomeAssertions;
using Xunit;

namespace AdvGenFlow.Tests;

public class PipelineBuilderTests
{
    private record PingRequest(string Message) : IRequest<string>;

    [Fact]
    public async Task Build_NoBehaviors_InvokesTerminalHandler()
    {
        // Arrange
        RequestHandlerDelegate<string> terminal = () => Task.FromResult("pong");

        // Act
        var result = await PipelineBuilder.Build(terminal, [], new PingRequest("hi"), default);

        // Assert
        result.Should().Be("pong");
    }

    [Fact]
    public async Task Build_TwoBehaviors_ExecutesInRegistrationOrder()
    {
        // First registered = outermost = runs first on entry
        var log = new List<string>();

        var behavior1 = new LoggingBehavior<PingRequest, string>("B1", log);
        var behavior2 = new LoggingBehavior<PingRequest, string>("B2", log);

        RequestHandlerDelegate<string> terminal = () =>
        {
            log.Add("handler");
            return Task.FromResult("result");
        };

        await PipelineBuilder.Build(terminal, [behavior1, behavior2], new PingRequest("x"), default);

        // B1 is outermost: enters first, exits last
        log.Should().Equal("B1-before", "B2-before", "handler", "B2-after", "B1-after");
    }

    [Fact]
    public async Task Build_PassesRequestAndCancellationTokenToBehaviors()
    {
        var cts = new CancellationTokenSource();
        PingRequest? capturedRequest = null;
        CancellationToken capturedToken = default;

        var behavior = new CapturingBehavior<PingRequest, string>(
            (req, ct) => { capturedRequest = req; capturedToken = ct; });

        var request = new PingRequest("capture-me");
        RequestHandlerDelegate<string> terminal = () => Task.FromResult("ok");

        await PipelineBuilder.Build(terminal, [behavior], request, cts.Token);

        capturedRequest.Should().Be(request);
        capturedToken.Should().Be(cts.Token);
    }

    // --- Test helpers ---

    private class LoggingBehavior<TRequest, TResponse>(string name, List<string> log)
        : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            log.Add($"{name}-before");
            var result = await next();
            log.Add($"{name}-after");
            return result;
        }
    }

    private class CapturingBehavior<TRequest, TResponse>(Action<TRequest, CancellationToken> capture)
        : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            capture(request, cancellationToken);
            return next();
        }
    }
}
