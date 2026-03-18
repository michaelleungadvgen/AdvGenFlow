using System.Runtime.CompilerServices;
using AdvGenFlow;

namespace AdvGenFlow.Examples.ConsoleApp.Streaming;

// Stream request
public record NumberStreamRequest(int Count, int DelayMs = 100) : IStreamRequest<int>;

// Stream handler
public class NumberStreamHandler : IStreamRequestHandler<NumberStreamRequest, int>
{
    public async IAsyncEnumerable<int> Handle(
        NumberStreamRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Console.WriteLine($"  [StreamHandler] Generating {request.Count} numbers with {request.DelayMs}ms delay...");
        
        for (int i = 1; i <= request.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            await Task.Delay(request.DelayMs, cancellationToken);
            yield return i * i; // Return squares
        }
        
        Console.WriteLine("  [StreamHandler] Stream complete");
    }
}
