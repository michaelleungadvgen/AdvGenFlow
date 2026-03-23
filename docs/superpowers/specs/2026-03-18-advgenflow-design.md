# AdvGenFlow Design Spec

**Date:** 2026-03-18
**Status:** Approved
**Target:** net9.0

---

## Overview

AdvGenFlow is a custom MediatR alternative written in C# for learning and full implementation ownership. It provides in-process messaging via a mediator pattern with request/response, notifications, pipeline behaviors, and streaming. It ships as two NuGet packages: a reflection-based core and an opt-in source generator.

---

## Goals

- Full feature parity with MediatR's core: Request/Response, Notifications, Pipeline Behaviors, Streaming.
- Microsoft.Extensions.DependencyInjection as the only DI integration.
- Reflection-based dispatch by default; compile-time dispatch via source generator as opt-in.
- Target net9.0 only.
- Clean, teachable implementation — no ceremony, no abstraction layers beyond what is necessary.

---

## Non-Goals

- Support for DI containers other than Microsoft.Extensions.DependencyInjection (v1).
- Multi-targeting (netstandard, net8, etc.).
- MediatR.Contracts-style abstractions-only package.
- Sequential notification fan-out mode (v1 uses Task.WhenAll).
- Pipeline behaviors for notifications or streams.

---

## Solution Structure

```
AdvGenFlow.sln
├── src/
│   ├── AdvGenFlow/                        # Core library (runtime, net9.0)
│   │   ├── Contracts/
│   │   │   ├── IRequest.cs
│   │   │   ├── INotification.cs
│   │   │   ├── IStreamRequest.cs
│   │   │   ├── IRequestHandler.cs
│   │   │   ├── INotificationHandler.cs
│   │   │   ├── IStreamRequestHandler.cs
│   │   │   ├── IPipelineBehavior.cs
│   │   │   ├── ISender.cs
│   │   │   └── IPublisher.cs
│   │   ├── Mediator.cs
│   │   ├── Pipeline/
│   │   │   └── PipelineBuilder.cs
│   │   ├── DependencyInjection/
│   │   │   └── ServiceCollectionExtensions.cs   # AddAdvGenFlow + AddAdvGenFlowGenerated + AddAdvGenFlowBehavior
│   │   └── AdvGenFlow.csproj
│   │
│   └── AdvGenFlow.SourceGen/              # Build-time source generator only (netstandard2.0)
│       ├── MediatorDispatchGenerator.cs   # Incremental generator (Initialize + pipeline steps)
│       └── AdvGenFlow.SourceGen.csproj
│
└── tests/
    ├── AdvGenFlow.Tests/
    └── AdvGenFlow.SourceGen.Tests/
```

---

## Package 1: AdvGenFlow (Core)

### Contracts

```csharp
// Request marker — response type encoded as generic parameter
public interface IRequest<TResponse> { }
public interface INotification { }
public interface IStreamRequest<TResponse> { }

// Handlers
public interface IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

public interface INotificationHandler<TNotification>
    where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}

public interface IStreamRequestHandler<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    IAsyncEnumerable<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

// Pipeline
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

public interface IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}

// Dispatcher interfaces
public interface ISender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default);
    IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default);
}

public interface IPublisher
{
    Task Publish<TNotification>(TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification;
}

public interface IMediator : ISender, IPublisher { }
```

### Mediator (Concrete)

`Mediator` is the single concrete implementation of `IMediator`. It accepts `IServiceProvider` and:

- **Send:** Calls `request.GetType()` to recover the concrete `TRequest` type, then resolves the handler via `typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse))` and `GetRequiredService(handlerType)`. Because the resolved service is `object`-typed, `Handle` is invoked via `dynamic` cast: `((dynamic)handler).Handle((dynamic)request, cancellationToken)`. This yields a `Task<TResponse>` which is then wrapped in `PipelineBuilder.Build`. The same `MakeGenericType` pattern applies for resolving behaviors. **The first registered behavior is the outermost** (executes first on entry, last on exit). The innermost delegate calls the actual handler.
- **Publish:** Resolves all `INotificationHandler<TNotification>` registrations and fans out with `Task.WhenAll`. All handlers run concurrently. `Publish` is a non-`async` method that returns `Task.WhenAll(handlers.Select(...))` directly. When the caller `await`s the result, C# unwraps the faulted task and re-throws the **first** handler exception. To inspect all exceptions, callers must capture the `Task` and access `.Exception` directly. This is an accepted trade-off — all handlers run regardless (none are cancelled by another's failure), and the first exception propagates on `await`.
- **CreateStream:** Resolves `IStreamRequestHandler<TRequest, TResponse>` via `MakeGenericType` and calls `handler.Handle(request, cancellationToken)` inline — no `PipelineBuilder` involved (pipeline behaviors do not apply to streams per Non-Goals).

No handler lookup caching in v1. The source generator replaces this path when added.

### Pipeline Builder

`PipelineBuilder` is a static helper with this signature:

```csharp
internal static class PipelineBuilder
{
    // Builds and immediately invokes the pipeline, returning the result Task.
    public static Task<TResponse> Build<TRequest, TResponse>(
        RequestHandlerDelegate<TResponse> handler,
        IEnumerable<IPipelineBehavior<TRequest, TResponse>> behaviors,
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>;
}
```

`Build` returns `Task<TResponse>` directly (it builds and invokes the pipeline in one step). Call sites do not need to invoke the returned delegate — `Build(...)` is the final call.

`request` and `cancellationToken` are passed into `Build` and captured in the closures built during composition. `request` is needed because each behavior's `Handle(TRequest request, next, ct)` must receive it. `cancellationToken` is captured for the same reason. Neither is a parameter of `RequestHandlerDelegate<TResponse>` (it takes no parameters), so both must be captured in closures.

The pipeline is composed by reversing the behavior list, folding with `Aggregate` to produce the outermost delegate, then invoking it:

```csharp
// Build implementation
// `handler` is already a RequestHandlerDelegate<TResponse> — the caller wraps the actual
// handler.Handle(request, ct) call into this no-arg delegate before passing it to Build.
RequestHandlerDelegate<TResponse> composed = behaviors
    .Reverse()
    .Aggregate(handler, (next, behavior) =>
        () => behavior.Handle(request, next, cancellationToken));
// `request` and `cancellationToken` from Build's parameters are captured here,
// threading them into each behavior.Handle call.

return composed(); // invoke the outermost delegate, return the resulting Task
```

The reversal ensures the first registered behavior becomes the outermost wrapper — it is the last one folded in and thus the first to execute.

### DI Registration

```csharp
// Register all handlers from provided assemblies
services.AddAdvGenFlow(typeof(Program).Assembly);

// Register behaviors explicitly (order = registration order)
services.AddAdvGenFlowBehavior<LoggingBehavior<,>>();
services.AddAdvGenFlowBehavior<ValidationBehavior<,>>();
```

- `IMediator`, `ISender`, `IPublisher` all registered as `Transient` pointing to `Mediator`.
- Handlers registered as `Transient` via assembly scan. The scan inspects each type's `ImplementedInterfaces` to find closed implementations of `IRequestHandler<,>`, `INotificationHandler<>`, and `IStreamRequestHandler<,>`. Each match is registered as `services.AddTransient(closedInterfaceType, concreteType)`.
- Behaviors registered as open-generic `Transient` against `IPipelineBehavior<,>` using the `Type`-accepting overload: `services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TBehavior))`. The extension method `AddAdvGenFlowBehavior<TBehavior>()` wraps this call — the generic overload of `AddTransient` cannot be used for open-generic registration.
- Behaviors are NOT auto-scanned — explicit registration keeps order intentional and visible.
- **Execution order:** The first behavior registered via `AddAdvGenFlowBehavior<>` is the outermost in the pipeline (runs first on entry). In the example above, `LoggingBehavior` wraps `ValidationBehavior` which wraps the handler.
- **Open-generic constraint correctness:** When the DI container resolves `IPipelineBehavior<TRequest, TResponse>` for a specific request, it closes the open-generic registration against the exact `TRequest`/`TResponse` pair. A behavior registered for `IRequest<Foo>` cannot be inadvertently resolved when dispatching `IRequest<Bar>` because the generic type system enforces the closed type match at resolution time.

---

## Package 2: AdvGenFlow.SourceGen

### Project Configuration

- Targets `netstandard2.0` (Roslyn requirement).
- Marked as `IsRoslynComponent = true` so it ships as an analyzer/generator in the NuGet package.
- References `Microsoft.CodeAnalysis.CSharp` with `PrivateAssets="all"`.

### Generator Behavior

An Incremental Source Generator (`MediatorDispatchGenerator`) uses `SyntaxValueProvider` / `IncrementalValuesProvider` (the Incremental Generator API — not the legacy `ISyntaxReceiver`) to inspect the compilation for all closed implementations of the handler interfaces and emits a `partial GeneratedMediator` class containing `switch` expression dispatch tables — one per dispatcher method.

**Structure of `GeneratedMediator`:**

The generator emits the **entire** `GeneratedMediator` class — both the constructor/field scaffolding and the dispatch methods — as a single generated file. There is no hand-written partial in the core library. This avoids a compile error when consumers reference `AdvGenFlow` without `AdvGenFlow.SourceGen` (since `GeneratedMediator` simply does not exist in that case).

The generator emits two logical sections in one file:
1. The class declaration, `_serviceProvider` field, and constructor.
2. `Send`, `Publish`, and `CreateStream` method implementations.

**Constraints:**
- The generator only sees types in the **consuming project's local compilation** — it does not scan handler types from referenced assemblies. All handlers that should appear in the generated dispatch must be defined in the same project that references `AdvGenFlow.SourceGen`. Handlers in separate class libraries remain resolvable from DI (runtime) but will not be included in the typed switch — they fall through to the `_ => throw` arm. This is a known constraint of v1.
- The generated dispatcher calls `PipelineBuilder.Build` for `Send` — the generated dispatch only replaces handler resolution (typed switch vs. `MakeGenericType`), not pipeline composition.

**Cast safety:** The `(Task<TResponse>)(object)` cast is safe because each switch arm is only reachable for a specific concrete request type, which fixes `TResponse` at that site. `Task<T>` is not covariant in C#, so the double-cast through `object` is required to satisfy the compiler. The arms are exhaustive and type-matched, so the cast never fails at runtime.

**Example generated output (with two request handlers and one notification handler):**
```csharp
// <auto-generated/>
using AdvGenFlow;
using Microsoft.Extensions.DependencyInjection;

internal sealed class GeneratedMediator : IMediator
{
    private readonly IServiceProvider _serviceProvider;
    public GeneratedMediator(IServiceProvider serviceProvider)
        => _serviceProvider = serviceProvider;

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        return request switch
        {
            CreateOrderCommand cmd => (Task<TResponse>)(object)SendCore_CreateOrderCommand(cmd, cancellationToken),
            GetOrderQuery q       => (Task<TResponse>)(object)SendCore_GetOrderQuery(q, cancellationToken),
            _ => throw new InvalidOperationException($"No handler registered for {request.GetType().Name}")
        };

        Task<OrderId> SendCore_CreateOrderCommand(CreateOrderCommand r, CancellationToken ct)
        {
            var handler   = _serviceProvider.GetRequiredService<IRequestHandler<CreateOrderCommand, OrderId>>();
            var behaviors = _serviceProvider.GetServices<IPipelineBehavior<CreateOrderCommand, OrderId>>();
            return PipelineBuilder.Build(() => handler.Handle(r, ct), behaviors, r, ct);
        }

        Task<Order> SendCore_GetOrderQuery(GetOrderQuery r, CancellationToken ct)
        {
            var handler   = _serviceProvider.GetRequiredService<IRequestHandler<GetOrderQuery, Order>>();
            var behaviors = _serviceProvider.GetServices<IPipelineBehavior<GetOrderQuery, Order>>();
            return PipelineBuilder.Build(() => handler.Handle(r, ct), behaviors, r, ct);
        }
    }

    public Task Publish<TNotification>(TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification
    {
        // Notification dispatch is not a switch — multiple handlers per type are possible.
        // Generated code falls back to the same Task.WhenAll pattern as the reflection path.
        var handlers = _serviceProvider.GetServices<INotificationHandler<TNotification>>();
        return Task.WhenAll(handlers.Select(h => h.Handle(notification, cancellationToken)));
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        return request switch
        {
            LivePriceQuery q => (IAsyncEnumerable<TResponse>)(object)
                _serviceProvider.GetRequiredService<IStreamRequestHandler<LivePriceQuery, decimal>>()
                    .Handle(q, cancellationToken),
            _ => throw new InvalidOperationException($"No stream handler registered for {request.GetType().Name}")
        };
    }
}
```

**Empty compilation (no handlers found):** The generator emits the class with empty switch bodies — `Send` and `CreateStream` have only the `_ => throw` default arm; `Publish` remains a `Task.WhenAll` of an empty sequence (returns a completed `Task`).

### DI Integration

`AddAdvGenFlowGenerated(params Assembly[] assemblies)` is used instead of `AddAdvGenFlow()` when the source gen package is in use. It lives in `AdvGenFlow` core (in `DependencyInjection/ServiceCollectionExtensions.cs`) alongside `AddAdvGenFlow` — it is a runtime method and cannot be placed in `AdvGenFlow.SourceGen` (a build-time Roslyn tool).

```csharp
// AdvGenFlow — DI registration for generated mediator
public static IServiceCollection AddAdvGenFlowGenerated(
    this IServiceCollection services,
    params Assembly[] assemblies)
{
    services.AddTransient<IMediator, GeneratedMediator>();
    services.AddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
    services.AddTransient<IPublisher>(sp => sp.GetRequiredService<IMediator>());

    // Same handler scan as AddAdvGenFlow — handlers must be in DI
    // so generated dispatch can call GetRequiredService<IRequestHandler<,>>
    foreach (var assembly in assemblies)
    {
        RegisterOpenGenericImplementations(services, assembly, typeof(IRequestHandler<,>), ServiceLifetime.Transient);
        RegisterOpenGenericImplementations(services, assembly, typeof(INotificationHandler<>), ServiceLifetime.Transient);
        RegisterOpenGenericImplementations(services, assembly, typeof(IStreamRequestHandler<,>), ServiceLifetime.Transient);
    }

    return services;
}
```

Behavior registration (`AddAdvGenFlowBehavior<>`) is unchanged — callers still use it explicitly.

---

## Error Handling

| Scenario | Behavior |
|---|---|
| No handler registered | `InvalidOperationException` from `GetRequiredService` with clear type name |
| No handlers for notification | No-op (empty `Task.WhenAll`) |
| One notification handler throws | All handlers still run; `await Publish(...)` re-throws the first handler exception; to see all, inspect the returned `Task.Exception` property before awaiting |
| Missing handler in generated dispatch | `InvalidOperationException` with request type name in message |
| CancellationToken cancelled | Propagated to handler; handler is responsible for observing it |

---

## Testing Strategy

### AdvGenFlow.Tests (xUnit + FluentAssertions)

- `SenderTests` — Send returns correct response; missing handler throws.
- `PublisherTests` — All handlers called; fan-out with Task.WhenAll. Multi-handler exception test: when two handlers throw, verify both ran (using a shared tracking list) and that `await Publish(...)` surfaces the first exception. To verify all exceptions are captured, the test stores the `Task` from `Publish(...)`, wraps `await task` in a try/catch to ensure the task completes, then asserts on `task.Exception!.InnerExceptions` (which contains all handler exceptions).
- `PipelineTests` — Behaviors execute in registration order; next delegate chains correctly.
- `StreamTests` — IAsyncEnumerable yields all items.
- `DependencyInjectionTests` — AddAdvGenFlow registers all handler types from test assembly.
- Cancellation propagation verified per dispatcher method.

All tests use in-memory `ServiceCollection` — no mocking frameworks. Uses **AwesomeAssertions** (MIT) instead of FluentAssertions — identical API, no license concerns.

### AdvGenFlow.SourceGen.Tests (xUnit + Verify.SourceGenerators)

- Snapshot tests validate generated `GeneratedMediator` code against committed `.verified.cs` files.
- Covers: single handler, multiple handlers, empty compilation (no handlers → empty switch arms, valid class).

---

## Dependencies

| Package | Purpose |
|---|---|
| Microsoft.Extensions.DependencyInjection | DI container integration (core) |
| Microsoft.CodeAnalysis.CSharp 4.x | Roslyn source generation (SourceGen only) |
| xUnit | Test runner |
| AwesomeAssertions | Test assertions (MIT-licensed community fork of FluentAssertions; drop-in API replacement) |
| Verify.SourceGenerators | Snapshot testing for generated code |
