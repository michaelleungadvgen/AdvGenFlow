using AdvGenFlow;
using AdvGenFlow.Examples.ConsoleApp.Requests;
using AdvGenFlow.Examples.ConsoleApp.Notifications;
using AdvGenFlow.Examples.ConsoleApp.Streaming;
using AdvGenFlow.Examples.ConsoleApp.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace AdvGenFlow.Examples.ConsoleApp;

public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("========================================");
        Console.WriteLine("   AdvGenFlow Examples Console App");
        Console.WriteLine("========================================\n");

        // Setup DI
        var services = new ServiceCollection();

        // Register AdvGenFlow with assembly scanning
        services.AddAdvGenFlow(typeof(Program).Assembly);

        // Register a pipeline behavior (open generic)
        services.AddAdvGenFlowBehavior(typeof(LoggingBehavior<,>));

        var serviceProvider = services.BuildServiceProvider();

        // Get sender and publisher
        var sender = serviceProvider.GetRequiredService<ISender>();
        var publisher = serviceProvider.GetRequiredService<IPublisher>();

        // ========================================
        // 1. Request/Response Example
        // ========================================
        Console.WriteLine("--- 1. Request/Response Example ---");
        var user = await sender.Send(new GetUserQuery(1));
        Console.WriteLine($"User: {user}\n");

        // ========================================
        // 2. Notification (Fan-out) Example
        // ========================================
        Console.WriteLine("--- 2. Notification (Fan-out) Example ---");
        await publisher.Publish(new OrderPlaced(12345));
        Console.WriteLine();

        // ========================================
        // 3. Streaming Example
        // ========================================
        Console.WriteLine("--- 3. Streaming Example ---");
        await foreach (var number in sender.CreateStream(new NumberStreamRequest(Count: 5)))
        {
            Console.WriteLine($"  Received: {number}");
        }
        Console.WriteLine();

        // ========================================
        // 4. Pipeline Behavior Example
        // ========================================
        Console.WriteLine("--- 4. Pipeline Behavior Example ---");
        var result = await sender.Send(new CalculateRequest(10, 5, "multiply"));
        Console.WriteLine($"Calculation result: {result}\n");

        Console.WriteLine("========================================");
        Console.WriteLine("   All examples completed successfully!");
        Console.WriteLine("========================================");
    }
}
