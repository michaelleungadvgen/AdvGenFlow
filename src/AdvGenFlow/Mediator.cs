namespace AdvGenFlow;

public sealed class Mediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;

    public Mediator(IServiceProvider serviceProvider)
        => _serviceProvider = serviceProvider;

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        var requestType = request.GetType();

        // Resolve handler via reflection — returns object, invoke via dynamic
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));
        var handler = _serviceProvider.GetRequiredService(handlerType);

        // Resolve behaviors via reflection — each is object, wrapped dynamically
        var behaviorType = typeof(IPipelineBehavior<,>).MakeGenericType(requestType, typeof(TResponse));
        var behaviorObjects = _serviceProvider.GetServices(behaviorType).Where(b => b != null).Cast<object>().ToList();

        // Terminal delegate: invoke handler.Handle via interface MethodInfo
        var handleMethod = handlerType.GetMethod("Handle")!;
        RequestHandlerDelegate<TResponse> terminal =
            () => (Task<TResponse>)handleMethod.Invoke(handler, [request, cancellationToken])!;

        if (behaviorObjects.Count == 0)
            return terminal();

        // Compose pipeline dynamically: each behavior is object, invoke Handle via interface MethodInfo
        var behaviorHandleMethod = behaviorType.GetMethod("Handle")!;
        RequestHandlerDelegate<TResponse> composed = behaviorObjects
            .Reverse<object>()
            .Aggregate(terminal, (next, behavior) =>
            {
                var capturedNext = next;
                return () => (Task<TResponse>)behaviorHandleMethod.Invoke(behavior, [request, capturedNext, cancellationToken])!;
            });

        return composed();
    }

    public Task Publish<TNotification>(TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
