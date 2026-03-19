using AdvGenFlow;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AdvGenFlow.Tests;

public record CountRequest(int Count) : IStreamRequest<int>;

public class CountHandler : IStreamRequestHandler<CountRequest, int>
{
    public async IAsyncEnumerable<int> Handle(CountRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 1; i <= request.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return i;
        }
    }
}

public class StreamTests
{
    private static ISender BuildSender(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddTransient<IMediator, Mediator>();
        services.AddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
        services.AddTransient<IStreamRequestHandler<CountRequest, int>, CountHandler>();
        configure?.Invoke(services);
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    [Fact]
    public async Task CreateStream_YieldsAllItems()
    {
        var sender = BuildSender();
        var results = new List<int>();

        await foreach (var item in sender.CreateStream(new CountRequest(3)))
            results.Add(item);

        results.Should().Equal([1, 2, 3]);
    }

    [Fact]
    public async Task CreateStream_NoHandler_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddTransient<IMediator, Mediator>();
        services.AddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
        var sender = services.BuildServiceProvider().GetRequiredService<ISender>();

        var act = async () =>
        {
            await foreach (var _ in sender.CreateStream(new CountRequest(1))) { }
        };

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CreateStream_PropagatesCancellationToken()
    {
        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var sender = BuildSender();
        var act = async () =>
        {
            await foreach (var _ in sender.CreateStream(new CountRequest(5), cts.Token)) { }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
