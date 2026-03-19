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
        var behaviorObjects = _serviceProvider.GetServices(behaviorType).ToList();

        // Terminal delegate: invoke handler.Handle via dynamic
        RequestHandlerDelegate<TResponse> terminal =
            () => (Task<TResponse>)((dynamic)handler).Handle((dynamic)request, cancellationToken);

        if (behaviorObjects.Count == 0)
            return terminal();

        // Compose pipeline dynamically: each behavior is object, wrap via dynamic
        RequestHandlerDelegate<TResponse> composed = behaviorObjects
            .Reverse<object>()
            .Aggregate(terminal, (next, behavior) =>
            {
                var capturedNext = next;
                return () => (Task<TResponse>)((dynamic)behavior).Handle((dynamic)request, capturedNext, cancellationToken);
            });

        return composed();
    }

    public Task Publish<TNotification>(TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        var handlers = _serviceProvider.GetServices<INotificationHandler<TNotification>>();
        return Task.WhenAll(handlers.Select(h =>
        {
            try { return h.Handle(notification, cancellationToken); }
            catch (Exception ex) { return Task.FromException(ex); }
        }));
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        var requestType = request.GetType();
        var handlerType = typeof(IStreamRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));
        var handler = _serviceProvider.GetRequiredService(handlerType);
        return (IAsyncEnumerable<TResponse>)((dynamic)handler).Handle((dynamic)request, cancellationToken);
    }
}
