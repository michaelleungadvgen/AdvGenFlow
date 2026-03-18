using AdvGenFlow;

namespace AdvGenFlow.Examples.ConsoleApp.Behaviors;

// Pipeline behavior that logs request handling
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        
        Console.WriteLine($"  [Behavior] Starting handling of {requestName}");
        Console.WriteLine($"  [Behavior] Request data: {request}");
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            var response = await next();
            
            stopwatch.Stop();
            Console.WriteLine($"  [Behavior] Completed {requestName} in {stopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine($"  [Behavior] Response: {response}");
            
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Console.WriteLine($"  [Behavior] Failed {requestName} after {stopwatch.ElapsedMilliseconds}ms");
            Console.WriteLine($"  [Behavior] Error: {ex.Message}");
            throw;
        }
    }
}
