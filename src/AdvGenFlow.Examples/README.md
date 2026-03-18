# AdvGenFlow Examples

This directory contains example projects demonstrating how to use the AdvGenFlow mediator library.

## Projects

### AdvGenFlow.Examples.Console

A console application demonstrating all features of AdvGenFlow:

1. **Request/Response** — Simple query handling with `IRequest<T>` and `IRequestHandler<TRequest, TResponse>`
2. **Notifications (Fan-out)** — Multiple handlers for a single `INotification`
3. **Streaming** — `IAsyncEnumerable<T>` responses with `IStreamRequest<T>`
4. **Pipeline Behaviors** — Cross-cutting concerns like logging via `IPipelineBehavior<TRequest, TResponse>`

## Running the Example

```bash
cd src/AdvGenFlow.Examples
dotnet run --project AdvGenFlow.Examples.Console
```

## Example Output

```
========================================
   AdvGenFlow Examples Console App
========================================

--- 1. Request/Response Example ---
  [Handler] Fetched user: User 1
User: UserDto { Id = 1, Name = User 1, Email = user1@example.com }

--- 2. Notification (Fan-out) Example ---
  [EmailHandler] Sending confirmation email for order #12345
  [AuditHandler] Logging order #12345 to audit trail
  [InventoryHandler] Updating inventory for order #12345

--- 3. Streaming Example ---
  [StreamHandler] Generating 5 numbers with 100ms delay...
  Received: 1
  Received: 4
  Received: 9
  Received: 16
  Received: 25
  [StreamHandler] Stream complete

--- 4. Pipeline Behavior Example ---
  [Behavior] Starting handling of CalculateRequest
  [Behavior] Request data: CalculateRequest { A = 10, B = 5, Operation = multiply }
  [Handler] Calculated: 10 * 5 = 50
  [Behavior] Completed CalculateRequest in 0ms
  [Behavior] Response: 50
Calculation result: 50

========================================
   All examples completed successfully!
========================================
```

## Key Concepts Demonstrated

### 1. Request/Response Pattern

```csharp
public record GetUserQuery(int UserId) : IRequest<UserDto>;

public class GetUserHandler : IRequestHandler<GetUserQuery, UserDto>
{
    public Task<UserDto> Handle(GetUserQuery request, CancellationToken cancellationToken)
    {
        // Handle the request
        return Task.FromResult(new UserDto(...));
    }
}
```

### 2. Notification Pattern (Fan-out)

Multiple handlers are called for a single notification:

```csharp
public record OrderPlaced(int OrderId) : INotification;

public class EmailHandler : INotificationHandler<OrderPlaced> { ... }
public class AuditHandler : INotificationHandler<OrderPlaced> { ... }
public class InventoryHandler : INotificationHandler<OrderPlaced> { ... }
```

### 3. Streaming Pattern

```csharp
public record NumberStreamRequest(int Count) : IStreamRequest<int>;

public class NumberStreamHandler : IStreamRequestHandler<NumberStreamRequest, int>
{
    public async IAsyncEnumerable<int> Handle(
        NumberStreamRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (int i = 1; i <= request.Count; i++)
        {
            yield return i;
        }
    }
}
```

### 4. Pipeline Behavior Pattern

```csharp
public class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Before handler
        var result = await next();
        // After handler
        return result;
    }
}
```

### 5. Dependency Injection Setup

```csharp
var services = new ServiceCollection();

// Register AdvGenFlow with assembly scanning
services.AddAdvGenFlow(typeof(Program).Assembly);

// Register pipeline behaviors (open generic)
services.AddAdvGenFlowBehavior(typeof(LoggingBehavior<,>));

var serviceProvider = services.BuildServiceProvider();

// Resolve and use
var sender = serviceProvider.GetRequiredService<ISender>();
var publisher = serviceProvider.GetRequiredService<IPublisher>();
```
