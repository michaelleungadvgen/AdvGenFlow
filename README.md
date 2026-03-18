# AdvGenFlow

A custom MediatR alternative in C# for .NET 9 with request/response, notifications, pipeline behaviors, streaming, reflection-based dispatch, and an opt-in Roslyn source generator for compile-time performance.

## Overview

AdvGenFlow provides a lightweight, extensible mediator pattern implementation with two dispatch modes:

- **Reflection-based** (`Mediator`) — uses `dynamic` dispatch for flexibility
- **Source-generated** (`GeneratedMediator`) — compile-time emitted dispatch using typed switch expressions for maximum performance

## Architecture

The solution consists of two packages:

| Package | Target | Description |
|---------|--------|-------------|
| `AdvGenFlow` | `net9.0` | Core library with contracts, reflection-based mediator, pipeline builder, and DI extensions |
| `AdvGenFlow.SourceGen` | `netstandard2.0` | Incremental Roslyn source generator that emits compile-time `GeneratedMediator` |

## Features

- **Request/Response** — `IRequest<TResponse>` with `IRequestHandler<TRequest, TResponse>`
- **Notifications** — `INotification` with fan-out to multiple `INotificationHandler<TNotification>`
- **Streaming** — `IStreamRequest<TResponse>` with `IStreamRequestHandler<TRequest, TResponse>` for `IAsyncEnumerable<T>`
- **Pipeline Behaviors** — `IPipelineBehavior<TRequest, TResponse>` for cross-cutting concerns (logging, validation, caching, etc.)
- **Dependency Injection** — Assembly scanning registration with `AddAdvGenFlow()` and `AddAdvGenFlowBehavior<T>()`
- **Source Generator** — Opt-in compile-time dispatch generation for zero-reflection performance

## Quick Start

### Installation

```bash
# Core package (required)
dotnet add package AdvGenFlow

# Source generator (optional, for compile-time dispatch)
dotnet add package AdvGenFlow.SourceGen
```

### Basic Usage

#### 1. Define a Request and Handler

```csharp
using AdvGenFlow;

// Request
public record GetUserQuery(int UserId) : IRequest<User>;

// Handler
public class GetUserHandler : IRequestHandler<GetUserQuery, User>
{
    public Task<User> Handle(GetUserQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new User { Id = request.UserId, Name = "John Doe" });
    }
}

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
```

#### 2. Register with DI

```csharp
using AdvGenFlow;

var services = new ServiceCollection();

// Reflection-based approach
services.AddAdvGenFlow(typeof(Program).Assembly);

// OR source-generated approach (requires AdvGenFlow.SourceGen package)
// services.AddAdvGenFlowGenerated<GeneratedMediator>(typeof(Program).Assembly);
```

#### 3. Send Requests

```csharp
public class UserService(ISender sender)
{
    public async Task<User> GetUserAsync(int userId)
    {
        return await sender.Send(new GetUserQuery(userId));
    }
}
```

### Notifications (Fan-out)

```csharp
// Notification
public record OrderPlaced(int OrderId) : INotification;

// Multiple handlers
public class EmailNotificationHandler : INotificationHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        // Send email
        return Task.CompletedTask;
    }
}

public class AuditLogHandler : INotificationHandler<OrderPlaced>
{
    public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
    {
        // Write to audit log
        return Task.CompletedTask;
    }
}

// Publish
await publisher.Publish(new OrderPlaced(orderId));
```

### Streaming

```csharp
// Stream request
public record LivePriceQuery(string Symbol) : IStreamRequest<decimal>;

// Stream handler
public class LivePriceHandler : IStreamRequestHandler<LivePriceQuery, decimal>
{
    public async IAsyncEnumerable<decimal> Handle(
        LivePriceQuery request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            yield return await GetCurrentPriceAsync(request.Symbol);
            await Task.Delay(1000, cancellationToken);
        }
    }
}

// Consume stream
await foreach (var price in sender.CreateStream(new LivePriceQuery("AAPL")))
{
    Console.WriteLine($"Current price: {price}");
}
```

### Pipeline Behaviors

```csharp
// Logging behavior
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Handling {Request}", typeof(TRequest).Name);
        var response = await next();
        _logger.LogInformation("Handled {Request}", typeof(TRequest).Name);
        return response;
    }
}

// Register
services.AddAdvGenFlow(typeof(Program).Assembly);
services.AddAdvGenFlowBehavior<LoggingBehavior<,>>();
```

## Project Structure

```
AdvGenFlow/
├── src/
│   ├── AdvGenFlow/                      # Core library (net9.0)
│   │   ├── Contracts/                   # Interfaces (IRequest, IHandler, etc.)
│   │   ├── Pipeline/                    # PipelineBuilder
│   │   ├── DependencyInjection/         # ServiceCollection extensions
│   │   └── Mediator.cs                  # Reflection-based implementation
│   └── AdvGenFlow.SourceGen/            # Source generator (netstandard2.0)
│       └── MediatorDispatchGenerator.cs # Incremental generator
├── tests/
│   ├── AdvGenFlow.Tests/                # Core tests (xUnit)
│   └── AdvGenFlow.SourceGen.Tests/      # Generator snapshot tests
└── docs/
    └── superpowers/
        ├── plans/                       # Implementation plans
        └── specs/                       # Design specifications
```

## Core Contracts

| Interface | Purpose |
|-----------|---------|
| `IRequest<TResponse>` | Marker for request types |
| `IRequestHandler<TRequest, TResponse>` | Handles requests |
| `INotification` | Marker for notification types |
| `INotificationHandler<TNotification>` | Handles notifications |
| `IStreamRequest<TResponse>` | Marker for streaming requests |
| `IStreamRequestHandler<TRequest, TResponse>` | Handles streaming requests |
| `IPipelineBehavior<TRequest, TResponse>` | Cross-cutting pipeline behavior |
| `ISender` | Send requests and create streams |
| `IPublisher` | Publish notifications |
| `IMediator` | Combines ISender and IPublisher |

## Configuration Options

### Reflection-based (Default)

```csharp
services.AddAdvGenFlow(typeof(Program).Assembly);
services.AddAdvGenFlowBehavior<MyBehavior<,>>();
```

### Source-generated (High Performance)

```csharp
// Add the source generator package first
services.AddAdvGenFlowGenerated<GeneratedMediator>(typeof(Program).Assembly);
```

## Testing

```bash
# Run all tests
dotnet test AdvGenFlow.sln

# Run core tests only
dotnet test tests/AdvGenFlow.Tests/AdvGenFlow.Tests.csproj

# Run source generator tests
dotnet test tests/AdvGenFlow.SourceGen.Tests/AdvGenFlow.SourceGen.Tests.csproj
```

## Tech Stack

- **.NET 9** — Target framework for core library and tests
- **netstandard2.0** — Source generator target for broad compatibility
- **Microsoft.Extensions.DependencyInjection** — DI container integration
- **Microsoft.CodeAnalysis.CSharp 4.x** — Roslyn source generation
- **xUnit 2.x** — Testing framework
- **AwesomeAssertions** — Fluent assertions
- **Verify.SourceGenerators** — Snapshot testing for generators

## License

MIT License — See [LICENSE](LICENSE) for details.

---

Built with ❤️ for high-performance, clean architecture applications.
