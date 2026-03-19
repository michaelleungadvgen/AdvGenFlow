using AdvGenFlow;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdvGenFlow.Tests;

// Must be public for end-to-end Send test (dynamic dispatch cross-assembly)
public record DependencyInjectionTestRequest(int Value) : IRequest<int>;
public class DependencyInjectionTestRequestHandler : IRequestHandler<DependencyInjectionTestRequest, int>
{
    public Task<int> Handle(DependencyInjectionTestRequest request, CancellationToken cancellationToken)
        => Task.FromResult(request.Value * 2);
}

public class DependencyInjectionTests
{
    // Handlers only used via GetService<T>() typed resolution — private is fine
    private record TestNotification(string Text) : INotification;
    private record TestStreamRequest(int Count) : IStreamRequest<string>;

    private class TestNotificationHandler : INotificationHandler<TestNotification>
    {
        public Task Handle(TestNotification notification, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private class TestStreamHandler : IStreamRequestHandler<TestStreamRequest, string>
    {
        public async IAsyncEnumerable<string> Handle(TestStreamRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var i = 0; i < request.Count; i++)
                yield return i.ToString();
        }
    }

    [Fact]
    public void AddAdvGenFlow_RegistersIMediator()
    {
        var services = new ServiceCollection();
        services.AddAdvGenFlow(typeof(DependencyInjectionTests).Assembly);
        var sp = services.BuildServiceProvider();

        sp.GetService<IMediator>().Should().NotBeNull().And.BeOfType<Mediator>();
    }

    [Fact]
    public void AddAdvGenFlow_RegistersISenderAndIPublisher()
    {
        var services = new ServiceCollection();
        services.AddAdvGenFlow(typeof(DependencyInjectionTests).Assembly);
        var sp = services.BuildServiceProvider();

        sp.GetService<ISender>().Should().NotBeNull();
        sp.GetService<IPublisher>().Should().NotBeNull();
    }

    [Fact]
    public void AddAdvGenFlow_RegistersRequestHandler()
    {
        var services = new ServiceCollection();
        services.AddAdvGenFlow(typeof(DependencyInjectionTests).Assembly);
        var sp = services.BuildServiceProvider();

        sp.GetService<IRequestHandler<DependencyInjectionTestRequest, int>>().Should().NotBeNull()
            .And.BeOfType<DependencyInjectionTestRequestHandler>();
    }

    [Fact]
    public void AddAdvGenFlow_RegistersNotificationHandler()
    {
        var services = new ServiceCollection();
        services.AddAdvGenFlow(typeof(DependencyInjectionTests).Assembly);
        var sp = services.BuildServiceProvider();

        sp.GetServices<INotificationHandler<TestNotification>>().Should().ContainSingle()
            .Which.Should().BeOfType<TestNotificationHandler>();
    }

    [Fact]
    public void AddAdvGenFlow_RegistersStreamHandler()
    {
        var services = new ServiceCollection();
        services.AddAdvGenFlow(typeof(DependencyInjectionTests).Assembly);
        var sp = services.BuildServiceProvider();

        sp.GetService<IStreamRequestHandler<TestStreamRequest, string>>().Should().NotBeNull()
            .And.BeOfType<TestStreamHandler>();
    }

    [Fact]
    public void AddAdvGenFlowBehavior_RegistersBehaviorAsOpenGeneric()
    {
        var services = new ServiceCollection();
        services.AddAdvGenFlow(typeof(DependencyInjectionTests).Assembly);
        services.AddAdvGenFlowBehavior(typeof(TestOpenBehavior<,>));
        var sp = services.BuildServiceProvider();

        sp.GetServices<IPipelineBehavior<DependencyInjectionTestRequest, int>>().Should().ContainSingle()
            .Which.Should().BeOfType<TestOpenBehavior<DependencyInjectionTestRequest, int>>();
    }

    [Fact]
    public async Task AddAdvGenFlow_EndToEnd_SendReturnsResult()
    {
        var services = new ServiceCollection();
        services.AddAdvGenFlow(typeof(DependencyInjectionTests).Assembly);
        var sender = services.BuildServiceProvider().GetRequiredService<ISender>();

        var result = await sender.Send(new DependencyInjectionTestRequest(21));

        result.Should().Be(42);
    }

    private class TestOpenBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken) => next();
    }
}
