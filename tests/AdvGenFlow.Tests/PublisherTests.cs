using AdvGenFlow;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdvGenFlow.Tests;

public class PublisherTests
{
    private record OrderPlaced(int OrderId) : INotification;

    private static IPublisher BuildPublisher(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddTransient<IMediator, Mediator>();
        services.AddTransient<IPublisher>(sp => sp.GetRequiredService<IMediator>());
        configure(services);
        return services.BuildServiceProvider().GetRequiredService<IPublisher>();
    }

    [Fact]
    public async Task Publish_CallsAllRegisteredHandlers()
    {
        var called = new List<string>();
        var publisher = BuildPublisher(s =>
        {
            s.AddTransient<INotificationHandler<OrderPlaced>>(
                _ => new TrackingHandler("H1", called));
            s.AddTransient<INotificationHandler<OrderPlaced>>(
                _ => new TrackingHandler("H2", called));
        });

        await publisher.Publish(new OrderPlaced(1));

        called.Should().BeEquivalentTo(["H1", "H2"]);
    }

    [Fact]
    public async Task Publish_NoHandlers_CompletesWithoutError()
    {
        var publisher = BuildPublisher(_ => { });
        var act = () => publisher.Publish(new OrderPlaced(1));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Publish_OneHandlerThrows_AllHandlersRunAndFirstExceptionPropagates()
    {
        var ran = new List<string>();
        var publisher = BuildPublisher(s =>
        {
            s.AddTransient<INotificationHandler<OrderPlaced>>(
                _ => new ThrowingHandler("H1", ran));
            s.AddTransient<INotificationHandler<OrderPlaced>>(
                _ => new TrackingHandler("H2", ran));
        });

        // Capture the task before awaiting so we can inspect .Exception
        var task = publisher.Publish(new OrderPlaced(1));

        // Ensure the task runs to completion (faulted)
        try { await task; } catch { }

        // Both handlers ran
        ran.Should().Contain("H1").And.Contain("H2");

        // Task has aggregated exceptions
        task.Exception!.InnerExceptions.Should().ContainSingle()
            .Which.Message.Should().Contain("H1-error");
    }

    [Fact]
    public async Task Publish_PropagatesCancellationToken()
    {
        CancellationToken capturedToken = default;
        var publisher = BuildPublisher(s =>
            s.AddTransient<INotificationHandler<OrderPlaced>>(
                _ => new CapturingHandler(ct => capturedToken = ct)));

        var cts = new CancellationTokenSource();
        await publisher.Publish(new OrderPlaced(1), cts.Token);

        capturedToken.Should().Be(cts.Token);
    }

    private class TrackingHandler(string name, List<string> log) : INotificationHandler<OrderPlaced>
    {
        public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
        {
            log.Add(name);
            return Task.CompletedTask;
        }
    }

    private class ThrowingHandler(string name, List<string> log) : INotificationHandler<OrderPlaced>
    {
        public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
        {
            log.Add(name);
            throw new Exception($"{name}-error");
        }
    }

    private class CapturingHandler(Action<CancellationToken> capture) : INotificationHandler<OrderPlaced>
    {
        public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
        {
            capture(cancellationToken);
            return Task.CompletedTask;
        }
    }
}
