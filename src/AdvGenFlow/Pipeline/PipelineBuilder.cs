namespace AdvGenFlow;

public static class PipelineBuilder
{
    public static Task<TResponse> Build<TRequest, TResponse>(
        RequestHandlerDelegate<TResponse> handler,
        IEnumerable<IPipelineBehavior<TRequest, TResponse>> behaviors,
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        // Compose: first registered behavior is outermost.
        // Reverse so that Aggregate folds outermost last (making it the entry point).
        RequestHandlerDelegate<TResponse> composed = behaviors
            .Reverse()
            .Aggregate(handler, (next, behavior) =>
                () => behavior.Handle(request, next, cancellationToken));

        return composed();
    }
}
