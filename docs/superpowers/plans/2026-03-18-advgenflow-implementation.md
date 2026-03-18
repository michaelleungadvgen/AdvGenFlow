# AdvGenFlow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a custom MediatR alternative in C# (net9.0) with request/response, notifications, pipeline behaviors, streaming, reflection-based dispatch, and an opt-in Roslyn source generator.

**Architecture:** Two packages — `AdvGenFlow` (reflection-based core) and `AdvGenFlow.SourceGen` (incremental source generator that emits compile-time dispatch). `Mediator` uses `dynamic` for reflection-based dispatch; `GeneratedMediator` is fully emitted by the generator using typed switch expressions. `PipelineBuilder` is a typed static helper used by the generated path only.

**Tech Stack:** C# / net9.0, netstandard2.0 (generator), Microsoft.Extensions.DependencyInjection, Microsoft.CodeAnalysis.CSharp 4.x, xUnit 2.x, AwesomeAssertions, Verify.SourceGenerators

---

## File Map

### src/AdvGenFlow/ (net9.0)
| File | Responsibility |
|---|---|
| `Contracts/IRequest.cs` | `IRequest<TResponse>` marker |
| `Contracts/INotification.cs` | `INotification` marker |
| `Contracts/IStreamRequest.cs` | `IStreamRequest<TResponse>` marker |
| `Contracts/IRequestHandler.cs` | `IRequestHandler<TRequest, TResponse>` |
| `Contracts/INotificationHandler.cs` | `INotificationHandler<TNotification>` |
| `Contracts/IStreamRequestHandler.cs` | `IStreamRequestHandler<TRequest, TResponse>` |
| `Contracts/IPipelineBehavior.cs` | `IPipelineBehavior<TRequest, TResponse>` + `RequestHandlerDelegate<T>` delegate |
| `Contracts/ISender.cs` | `ISender` (Send + CreateStream) |
| `Contracts/IPublisher.cs` | `IPublisher` (Publish) |
| `Contracts/IMediator.cs` | `IMediator : ISender, IPublisher` |
| `Pipeline/PipelineBuilder.cs` | Static `Build<TRequest,TResponse>(...)` — composes and invokes behavior pipeline |
| `Mediator.cs` | Reflection-based `IMediator` implementation using `dynamic` dispatch |
| `DependencyInjection/ServiceCollectionExtensions.cs` | `AddAdvGenFlow`, `AddAdvGenFlowGenerated`, `AddAdvGenFlowBehavior<T>` |

### src/AdvGenFlow.SourceGen/ (netstandard2.0)
| File | Responsibility |
|---|---|
| `MediatorDispatchGenerator.cs` | Incremental source generator — discovers handlers, emits `GeneratedMediator` |

### tests/AdvGenFlow.Tests/ (net9.0)
| File | Tests |
|---|---|
| `PipelineBuilderTests.cs` | PipelineBuilder compose and invoke |
| `SenderTests.cs` | `Send` happy path, missing handler |
| `PublisherTests.cs` | Fan-out, no handlers, exception propagation |
| `StreamTests.cs` | `CreateStream` happy path, missing handler |
| `PipelineTests.cs` | Behavior ordering, next-chain, cancellation |
| `DependencyInjectionTests.cs` | Assembly scan registration, `AddAdvGenFlowBehavior` |

### tests/AdvGenFlow.SourceGen.Tests/ (net9.0)
| File | Tests |
|---|---|
| `GeneratorTests.cs` | Snapshot tests for generated `GeneratedMediator` |
| `Snapshots/*.verified.cs` | Committed snapshot files |

---

## Task 1: Solution & project scaffolding

**Files:**
- Create: `AdvGenFlow.sln`
- Create: `src/AdvGenFlow/AdvGenFlow.csproj`
- Create: `src/AdvGenFlow.SourceGen/AdvGenFlow.SourceGen.csproj`
- Create: `tests/AdvGenFlow.Tests/AdvGenFlow.Tests.csproj`
- Create: `tests/AdvGenFlow.SourceGen.Tests/AdvGenFlow.SourceGen.Tests.csproj`

- [ ] **Step 1: Create solution and projects**

```bash
cd c:/Users/advgen10/source/repos/AdvGenFlow
dotnet new sln -n AdvGenFlow
dotnet new classlib -n AdvGenFlow -f net9.0 -o src/AdvGenFlow
dotnet new classlib -n AdvGenFlow.SourceGen -f netstandard2.0 -o src/AdvGenFlow.SourceGen
dotnet new xunit -n AdvGenFlow.Tests -f net9.0 -o tests/AdvGenFlow.Tests
dotnet new xunit -n AdvGenFlow.SourceGen.Tests -f net9.0 -o tests/AdvGenFlow.SourceGen.Tests
```

- [ ] **Step 2: Add projects to solution**

```bash
dotnet sln add src/AdvGenFlow/AdvGenFlow.csproj
dotnet sln add src/AdvGenFlow.SourceGen/AdvGenFlow.SourceGen.csproj
dotnet sln add tests/AdvGenFlow.Tests/AdvGenFlow.Tests.csproj
dotnet sln add tests/AdvGenFlow.SourceGen.Tests/AdvGenFlow.SourceGen.Tests.csproj
```

- [ ] **Step 3: Replace AdvGenFlow.csproj**

`src/AdvGenFlow/AdvGenFlow.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="9.*" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="9.*" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Replace AdvGenFlow.SourceGen.csproj**

`src/AdvGenFlow.SourceGen/AdvGenFlow.SourceGen.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <IsRoslynComponent>true</IsRoslynComponent>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.*" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

- [ ] **Step 5: Replace AdvGenFlow.Tests.csproj**

`tests/AdvGenFlow.Tests/AdvGenFlow.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" PrivateAssets="all" />
    <PackageReference Include="AwesomeAssertions" Version="*" />
    <ProjectReference Include="../../src/AdvGenFlow/AdvGenFlow.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 6: Replace AdvGenFlow.SourceGen.Tests.csproj**

`tests/AdvGenFlow.SourceGen.Tests/AdvGenFlow.SourceGen.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" PrivateAssets="all" />
    <PackageReference Include="AwesomeAssertions" Version="*" />
    <PackageReference Include="Verify.SourceGenerators" Version="*" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.*" />
    <ProjectReference Include="../../src/AdvGenFlow/AdvGenFlow.csproj" />
    <!-- Reference SourceGen as a project (for tests), NOT as an analyzer -->
    <ProjectReference Include="../../src/AdvGenFlow.SourceGen/AdvGenFlow.SourceGen.csproj"
                      OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

- [ ] **Step 7: Delete generated boilerplate**

```bash
rm src/AdvGenFlow/Class1.cs
rm src/AdvGenFlow.SourceGen/Class1.cs
rm tests/AdvGenFlow.Tests/UnitTest1.cs
rm tests/AdvGenFlow.SourceGen.Tests/UnitTest1.cs
```

- [ ] **Step 8: Verify solution builds**

```bash
dotnet build AdvGenFlow.sln
```
Expected: build succeeds (0 warnings, 0 errors)

- [ ] **Step 9: Add global usings file to AdvGenFlow core**

`src/AdvGenFlow/GlobalUsings.cs`:
```csharp
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using Microsoft.Extensions.DependencyInjection;
```

- [ ] **Step 10: Commit**

```bash
git init
git add .
git commit -m "chore: scaffold solution with AdvGenFlow core, SourceGen, and test projects"
```

---

## Task 2: Contracts (interfaces)

No behaviour — pure interface definitions. No tests needed.

**Files:**
- Create: `src/AdvGenFlow/Contracts/IRequest.cs`
- Create: `src/AdvGenFlow/Contracts/INotification.cs`
- Create: `src/AdvGenFlow/Contracts/IStreamRequest.cs`
- Create: `src/AdvGenFlow/Contracts/IRequestHandler.cs`
- Create: `src/AdvGenFlow/Contracts/INotificationHandler.cs`
- Create: `src/AdvGenFlow/Contracts/IStreamRequestHandler.cs`
- Create: `src/AdvGenFlow/Contracts/IPipelineBehavior.cs`
- Create: `src/AdvGenFlow/Contracts/ISender.cs`
- Create: `src/AdvGenFlow/Contracts/IPublisher.cs`
- Create: `src/AdvGenFlow/Contracts/IMediator.cs`

- [ ] **Step 1: Write request/notification/stream markers**

`src/AdvGenFlow/Contracts/IRequest.cs`:
```csharp
namespace AdvGenFlow;
public interface IRequest<TResponse> { }
```

`src/AdvGenFlow/Contracts/INotification.cs`:
```csharp
namespace AdvGenFlow;
public interface INotification { }
```

`src/AdvGenFlow/Contracts/IStreamRequest.cs`:
```csharp
namespace AdvGenFlow;
public interface IStreamRequest<TResponse> { }
```

- [ ] **Step 2: Write handler interfaces**

`src/AdvGenFlow/Contracts/IRequestHandler.cs`:
```csharp
namespace AdvGenFlow;
public interface IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}
```

`src/AdvGenFlow/Contracts/INotificationHandler.cs`:
```csharp
namespace AdvGenFlow;
public interface INotificationHandler<TNotification>
    where TNotification : INotification
{
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}
```

`src/AdvGenFlow/Contracts/IStreamRequestHandler.cs`:
```csharp
namespace AdvGenFlow;
public interface IStreamRequestHandler<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>
{
    IAsyncEnumerable<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Write pipeline interfaces**

`src/AdvGenFlow/Contracts/IPipelineBehavior.cs`:
```csharp
namespace AdvGenFlow;

public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

public interface IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Write dispatcher interfaces**

`src/AdvGenFlow/Contracts/ISender.cs`:
```csharp
namespace AdvGenFlow;
public interface ISender
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request,
        CancellationToken cancellationToken = default);
    IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default);
}
```

`src/AdvGenFlow/Contracts/IPublisher.cs`:
```csharp
namespace AdvGenFlow;
public interface IPublisher
{
    Task Publish<TNotification>(TNotification notification,
        CancellationToken cancellationToken = default)
        where TNotification : INotification;
}
```

`src/AdvGenFlow/Contracts/IMediator.cs`:
```csharp
namespace AdvGenFlow;
public interface IMediator : ISender, IPublisher { }
```

- [ ] **Step 5: Build and verify**

```bash
dotnet build src/AdvGenFlow/AdvGenFlow.csproj
```
Expected: 0 errors

- [ ] **Step 6: Commit**

```bash
git add src/AdvGenFlow/Contracts/
git commit -m "feat: add IRequest, INotification, IStreamRequest, handler and dispatcher contracts"
```

---

## Task 3: PipelineBuilder

`PipelineBuilder` composes `IPipelineBehavior<TRequest, TResponse>` instances around a terminal delegate and immediately invokes the pipeline. This is a clean typed helper — used by the generated mediator and by reflection path via dynamic.

**Files:**
- Create: `src/AdvGenFlow/Pipeline/PipelineBuilder.cs`
- Create: `tests/AdvGenFlow.Tests/PipelineBuilderTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/AdvGenFlow.Tests/PipelineBuilderTests.cs`:
```csharp
using AdvGenFlow;
using AwesomeAssertions;

namespace AdvGenFlow.Tests;

public class PipelineBuilderTests
{
    private record PingRequest(string Message) : IRequest<string>;

    [Fact]
    public async Task Build_NoBehaviors_InvokesTerminalHandler()
    {
        // Arrange
        RequestHandlerDelegate<string> terminal = () => Task.FromResult("pong");

        // Act
        var result = await PipelineBuilder.Build(terminal, [], new PingRequest("hi"), default);

        // Assert
        result.Should().Be("pong");
    }

    [Fact]
    public async Task Build_TwoBehaviors_ExecutesInRegistrationOrder()
    {
        // First registered = outermost = runs first on entry
        var log = new List<string>();

        var behavior1 = new LoggingBehavior<PingRequest, string>("B1", log);
        var behavior2 = new LoggingBehavior<PingRequest, string>("B2", log);

        RequestHandlerDelegate<string> terminal = () =>
        {
            log.Add("handler");
            return Task.FromResult("result");
        };

        await PipelineBuilder.Build(terminal, [behavior1, behavior2], new PingRequest("x"), default);

        // B1 is outermost: enters first, exits last
        log.Should().Equal("B1-before", "B2-before", "handler", "B2-after", "B1-after");
    }

    [Fact]
    public async Task Build_PassesRequestAndCancellationTokenToBehaviors()
    {
        var cts = new CancellationTokenSource();
        PingRequest? capturedRequest = null;
        CancellationToken capturedToken = default;

        var behavior = new CapturingBehavior<PingRequest, string>(
            (req, ct) => { capturedRequest = req; capturedToken = ct; });

        var request = new PingRequest("capture-me");
        RequestHandlerDelegate<string> terminal = () => Task.FromResult("ok");

        await PipelineBuilder.Build(terminal, [behavior], request, cts.Token);

        capturedRequest.Should().Be(request);
        capturedToken.Should().Be(cts.Token);
    }

    // --- Test helpers ---

    private class LoggingBehavior<TRequest, TResponse>(string name, List<string> log)
        : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            log.Add($"{name}-before");
            var result = await next();
            log.Add($"{name}-after");
            return result;
        }
    }

    private class CapturingBehavior<TRequest, TResponse>(Action<TRequest, CancellationToken> capture)
        : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
        {
            capture(request, cancellationToken);
            return next();
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/AdvGenFlow.Tests/AdvGenFlow.Tests.csproj --filter "FullyQualifiedName~PipelineBuilderTests" -v n
```
Expected: compile error — `PipelineBuilder` does not exist yet

- [ ] **Step 3: Implement PipelineBuilder**

`src/AdvGenFlow/Pipeline/PipelineBuilder.cs`:
```csharp
namespace AdvGenFlow;

internal static class PipelineBuilder
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
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/AdvGenFlow.Tests/AdvGenFlow.Tests.csproj --filter "FullyQualifiedName~PipelineBuilderTests" -v n
```
Expected: 3 tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/AdvGenFlow/Pipeline/PipelineBuilder.cs tests/AdvGenFlow.Tests/PipelineBuilderTests.cs
git commit -m "feat: add PipelineBuilder with behavior composition and invocation"
```

---

## Task 4: Mediator.Send (reflection path)

`Mediator` uses `dynamic` to invoke reflection-resolved handlers and behaviors. `TRequest` is recovered at runtime via `request.GetType()`.

**Files:**
- Create: `src/AdvGenFlow/Mediator.cs`
- Create: `tests/AdvGenFlow.Tests/SenderTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/AdvGenFlow.Tests/SenderTests.cs`:
```csharp
using AdvGenFlow;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AdvGenFlow.Tests;

public class SenderTests
{
    private record PingCommand(string Message) : IRequest<string>;

    private class PingHandler : IRequestHandler<PingCommand, string>
    {
        public Task<string> Handle(PingCommand request, CancellationToken cancellationToken)
            => Task.FromResult($"pong:{request.Message}");
    }

    private static ISender BuildSender(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddTransient<IMediator, Mediator>();
        services.AddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
        services.AddTransient<IRequestHandler<PingCommand, string>, PingHandler>();
        configure?.Invoke(services);
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    [Fact]
    public async Task Send_WithRegisteredHandler_ReturnsHandlerResult()
    {
        var sender = BuildSender();
        var result = await sender.Send(new PingCommand("hello"));
        result.Should().Be("pong:hello");
    }

    [Fact]
    public async Task Send_WithNoHandler_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddTransient<IMediator, Mediator>();
        services.AddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
        var sender = services.BuildServiceProvider().GetRequiredService<ISender>();

        var act = () => sender.Send(new PingCommand("x"));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Send_PropagatesCancellationToken()
    {
        CancellationToken capturedToken = default;

        var services = new ServiceCollection();
        services.AddTransient<IMediator, Mediator>();
        services.AddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
        services.AddTransient<IRequestHandler<PingCommand, string>>(
            _ => new CapturingHandler(ct => capturedToken = ct));
        var sender = services.BuildServiceProvider().GetRequiredService<ISender>();

        var cts = new CancellationTokenSource();
        await sender.Send(new PingCommand("x"), cts.Token);

        capturedToken.Should().Be(cts.Token);
    }

    private class CapturingHandler(Action<CancellationToken> capture) : IRequestHandler<PingCommand, string>
    {
        public Task<string> Handle(PingCommand request, CancellationToken cancellationToken)
        {
            capture(cancellationToken);
            return Task.FromResult("ok");
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/AdvGenFlow.Tests/AdvGenFlow.Tests.csproj --filter "FullyQualifiedName~SenderTests" -v n
```
Expected: compile error — `Mediator` does not exist yet

- [ ] **Step 3: Implement Mediator.Send**

`src/AdvGenFlow/Mediator.cs`:
```csharp
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
        throw new NotImplementedException();
    }

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/AdvGenFlow.Tests/AdvGenFlow.Tests.csproj --filter "FullyQualifiedName~SenderTests" -v n
```
Expected: 3 tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/AdvGenFlow/Mediator.cs tests/AdvGenFlow.Tests/SenderTests.cs
git commit -m "feat: implement Mediator.Send with dynamic reflection dispatch"
```

---

## Task 5: Mediator.Publish

**Files:**
- Modify: `src/AdvGenFlow/Mediator.cs` (replace `NotImplementedException` stub)
- Create: `tests/AdvGenFlow.Tests/PublisherTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/AdvGenFlow.Tests/PublisherTests.cs`:
```csharp
using AdvGenFlow;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AdvGenFlow.Tests;

public class PublisherTests
{
    private record OrderPlaced(int OrderId) : INotification;

    private static IPublisher BuildPublisher(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddTransient<IMediator, Mediator>();
        services.AddTransient<IPublisher>(sp => sp.GetRequiredService<IMediator>());
        configure(services);
        return services.BuildServiceProvider().GetRequiredService<IPublisher>();
    }

    [Fact]
    public async Task Publish_CallsAllRegisteredHandlers()
    {
        var called = new List<string>();
        var publisher = BuildPublisher(s =>
        {
            s.AddTransient<INotificationHandler<OrderPlaced>>(
                _ => new TrackingHandler("H1", called));
            s.AddTransient<INotificationHandler<OrderPlaced>>(
                _ => new TrackingHandler("H2", called));
        });

        await publisher.Publish(new OrderPlaced(1));

        called.Should().BeEquivalentTo(["H1", "H2"]);
    }

    [Fact]
    public async Task Publish_NoHandlers_CompletesWithoutError()
    {
        var publisher = BuildPublisher(_ => { });
        var act = () => publisher.Publish(new OrderPlaced(1));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Publish_OneHandlerThrows_AllHandlersRunAndFirstExceptionPropagates()
    {
        var ran = new List<string>();
        var publisher = BuildPublisher(s =>
        {
            s.AddTransient<INotificationHandler<OrderPlaced>>(
                _ => new ThrowingHandler("H1", ran));
            s.AddTransient<INotificationHandler<OrderPlaced>>(
                _ => new TrackingHandler("H2", ran));
        });

        // Capture the task before awaiting so we can inspect .Exception
        var task = publisher.Publish(new OrderPlaced(1));

        // Ensure the task runs to completion (faulted)
        try { await task; } catch { }

        // Both handlers ran
        ran.Should().Contain("H1").And.Contain("H2");

        // Task has aggregated exceptions
        task.Exception!.InnerExceptions.Should().ContainSingle()
            .Which.Message.Should().Contain("H1-error");
    }

    [Fact]
    public async Task Publish_PropagatesCancellationToken()
    {
        CancellationToken capturedToken = default;
        var publisher = BuildPublisher(s =>
            s.AddTransient<INotificationHandler<OrderPlaced>>(
                _ => new CapturingHandler(ct => capturedToken = ct)));

        var cts = new CancellationTokenSource();
        await publisher.Publish(new OrderPlaced(1), cts.Token);

        capturedToken.Should().Be(cts.Token);
    }

    private class TrackingHandler(string name, List<string> log) : INotificationHandler<OrderPlaced>
    {
        public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
        {
            log.Add(name);
            return Task.CompletedTask;
        }
    }

    private class ThrowingHandler(string name, List<string> log) : INotificationHandler<OrderPlaced>
    {
        public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
        {
            log.Add(name);
            throw new Exception($"{name}-error");
        }
    }

    private class CapturingHandler(Action<CancellationToken> capture) : INotificationHandler<OrderPlaced>
    {
        public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
        {
            capture(cancellationToken);
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/AdvGenFlow.Tests/AdvGenFlow.Tests.csproj --filter "FullyQualifiedName~PublisherTests" -v n
```
Expected: FAIL — `NotImplementedException`

- [ ] **Step 3: Implement Mediator.Publish**

Replace the `Publish` stub in `src/AdvGenFlow/Mediator.cs`:
```csharp
public Task Publish<TNotification>(TNotification notification,
    CancellationToken cancellationToken = default)
    where TNotification : INotification
{
    var handlers = _serviceProvider.GetServices<INotificationHandler<TNotification>>();
    return Task.WhenAll(handlers.Select(h => h.Handle(notification, cancellationToken)));
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/AdvGenFlow.Tests/AdvGenFlow.Tests.csproj --filter "FullyQualifiedName~PublisherTests" -v n
```
Expected: 4 tests PASS

- [ ] **Step 5: Commit**

```bash
git add src/AdvGenFlow/Mediator.cs tests/AdvGenFlow.Tests/PublisherTests.cs
git commit -m "feat: implement Mediator.Publish with Task.WhenAll fan-out"
```

---

## Task 6: Mediator.CreateStream

**Files:**
- Modify: `src/AdvGenFlow/Mediator.cs` (replace `NotImplementedException` stub)
- Create: `tests/AdvGenFlow.Tests/StreamTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/AdvGenFlow.Tests/StreamTests.cs`:
```csharp
using AdvGenFlow;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AdvGenFlow.Tests;

public class StreamTests
{
    private record CountRequest(int Count) : IStreamRequest<int>;

    private class CountHandler : IStreamRequestHandler<CountRequest, int>
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
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/AdvGenFlow.Tests/AdvGenFlow.Tests.csproj --filter "FullyQualifiedName~StreamTests" -v n
```
Expected: FAIL — `NotImplementedException`

- [ ] **Step 3: Implement Mediator.CreateStream**

Replace the `CreateStream` stub in `src/AdvGenFlow/Mediator.cs`:
```csharp
public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request,
    CancellationToken cancellationToken = default)
{
    var requestType = request.GetType();
    var handlerType = typeof(IStreamRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));
    var handler = _serviceProvider.GetRequiredService(handlerType);
    return (IAsyncEnumerable<TResponse>)((dynamic)handler).Handle((dynamic)request, cancellationToken);
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/AdvGenFlow.Tests/AdvGenFlow.Tests.csproj --filter "FullyQualifiedName~StreamTests" -v n
```
Expected: 3 tests PASS

- [ ] **Step 5: Run all core tests**

```bash
dotnet test tests/AdvGenFlow.Tests/AdvGenFlow.Tests.csproj -v n
```
Expected: all tests PASS

- [ ] **Step 6: Commit**

```bash
git add src/AdvGenFlow/Mediator.cs tests/AdvGenFlow.Tests/StreamTests.cs
git commit -m "feat: implement Mediator.CreateStream with dynamic reflection dispatch"
```

---

## Task 7: Pipeline behaviors integration test

Verify the full behavior pipeline works via the reflection `Mediator.Send` path (behaviors + handler).

**Files:**
- Create: `tests/AdvGenFlow.Tests/PipelineTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/AdvGenFlow.Tests/PipelineTests.cs`:
```csharp
using AdvGenFlow;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AdvGenFlow.Tests;

public class PipelineTests
{
    private record EchoRequest(string Value) : IRequest<string>;

    private class EchoHandler : IRequestHandler<EchoRequest, string>
    {
        public Task<string> Handle(EchoRequest request, CancellationToken cancellationToken)
            => Task.FromResult(request.Value);
    }

    private class PrefixBehavior(string prefix, List<string> log) : IPipelineBehavior<EchoRequest, string>
    {
        public async Task<string> Handle(EchoRequest request, RequestHandlerDelegate<string> next,
            CancellationToken cancellationToken)
        {
            log.Add($"{prefix}-enter");
            var result = await next();
            log.Add($"{prefix}-exit");
            return result;
        }
    }

    [Fact]
    public async Task Send_WithBehaviors_ExecutesInRegistrationOrder()
    {
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddTransient<IMediator, Mediator>();
        services.AddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
        services.AddTransient<IRequestHandler<EchoRequest, string>, EchoHandler>();
        // B1 registered first → outermost (runs first on entry, last on exit)
        services.AddTransient<IPipelineBehavior<EchoRequest, string>>(
            _ => new PrefixBehavior("B1", log));
        services.AddTransient<IPipelineBehavior<EchoRequest, string>>(
            _ => new PrefixBehavior("B2", log));

        var sender = services.BuildServiceProvider().GetRequiredService<ISender>();
        var result = await sender.Send(new EchoRequest("hello"));

        result.Should().Be("hello");
        log.Should().Equal("B1-enter", "B2-enter", "B1-exit", "B2-exit");
    }

    [Fact]
    public async Task Send_BehaviorCanShortCircuit()
    {
        var services = new ServiceCollection();
        services.AddTransient<IMediator, Mediator>();
        services.AddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
        services.AddTransient<IRequestHandler<EchoRequest, string>, EchoHandler>();
        services.AddTransient<IPipelineBehavior<EchoRequest, string>>(
            _ => new ShortCircuitBehavior());

        var sender = services.BuildServiceProvider().GetRequiredService<ISender>();
        var result = await sender.Send(new EchoRequest("ignored"));

        result.Should().Be("short-circuited");
    }

    private class ShortCircuitBehavior : IPipelineBehavior<EchoRequest, string>
    {
        public Task<string> Handle(EchoRequest request, RequestHandlerDelegate<string> next,
            CancellationToken cancellationToken)
            => Task.FromResult("short-circuited"); // does NOT call next()
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/AdvGenFlow.Tests/AdvGenFlow.Tests.csproj --filter "FullyQualifiedName~PipelineTests" -v n
```
Expected: FAIL — `PipelineTests` class not found

- [ ] **Step 3: Run tests to verify they pass**

```bash
dotnet test tests/AdvGenFlow.Tests/AdvGenFlow.Tests.csproj --filter "FullyQualifiedName~PipelineTests" -v n
```
Expected: 2 tests PASS

- [ ] **Step 4: Commit**

```bash
git add tests/AdvGenFlow.Tests/PipelineTests.cs
git commit -m "test: add pipeline behavior ordering and short-circuit tests"
```

---

## Task 8: DI Registration

**Files:**
- Create: `src/AdvGenFlow/DependencyInjection/ServiceCollectionExtensions.cs`
- Create: `tests/AdvGenFlow.Tests/DependencyInjectionTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/AdvGenFlow.Tests/DependencyInjectionTests.cs`:
```csharp
using AdvGenFlow;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AdvGenFlow.Tests;

public class DependencyInjectionTests
{
    // Handlers defined in the test assembly to be discovered by AddAdvGenFlow
    private record TestRequest(int Value) : IRequest<int>;
    private record TestNotification(string Text) : INotification;
    private record TestStreamRequest(int Count) : IStreamRequest<string>;

    private class TestRequestHandler : IRequestHandler<TestRequest, int>
    {
        public Task<int> Handle(TestRequest request, CancellationToken cancellationToken)
            => Task.FromResult(request.Value * 2);
    }

    private class TestNotificationHandler : INotificationHandler<TestNotification>
    {
        public Task Handle(TestNotification notification, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private class TestStreamHandler : IStreamRequestHandler<TestStreamRequest, string>
    {
        public async IAsyncEnumerable<string> Handle(TestStreamRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var i = 0; i < request.Count; i++)
                yield return i.ToString();
        }
    }

    [Fact]
    public void AddAdvGenFlow_RegistersIMediator()
    {
        var services = new ServiceCollection();
        services.AddAdvGenFlow(typeof(DependencyInjectionTests).Assembly);
        var sp = services.BuildServiceProvider();

        sp.GetService<IMediator>().Should().NotBeNull().And.BeOfType<Mediator>();
    }

    [Fact]
    public void AddAdvGenFlow_RegistersISenderAndIPublisher()
    {
        var services = new ServiceCollection();
        services.AddAdvGenFlow(typeof(DependencyInjectionTests).Assembly);
        var sp = services.BuildServiceProvider();

        sp.GetService<ISender>().Should().NotBeNull();
        sp.GetService<IPublisher>().Should().NotBeNull();
    }

    [Fact]
    public void AddAdvGenFlow_RegistersRequestHandler()
    {
        var services = new ServiceCollection();
        services.AddAdvGenFlow(typeof(DependencyInjectionTests).Assembly);
        var sp = services.BuildServiceProvider();

        sp.GetService<IRequestHandler<TestRequest, int>>().Should().NotBeNull()
            .And.BeOfType<TestRequestHandler>();
    }

    [Fact]
    public void AddAdvGenFlow_RegistersNotificationHandler()
    {
        var services = new ServiceCollection();
        services.AddAdvGenFlow(typeof(DependencyInjectionTests).Assembly);
        var sp = services.BuildServiceProvider();

        sp.GetServices<INotificationHandler<TestNotification>>().Should().ContainSingle()
            .Which.Should().BeOfType<TestNotificationHandler>();
    }

    [Fact]
    public void AddAdvGenFlow_RegistersStreamHandler()
    {
        var services = new ServiceCollection();
        services.AddAdvGenFlow(typeof(DependencyInjectionTests).Assembly);
        var sp = services.BuildServiceProvider();

        sp.GetService<IStreamRequestHandler<TestStreamRequest, string>>().Should().NotBeNull()
            .And.BeOfType<TestStreamHandler>();
    }

    [Fact]
    public void AddAdvGenFlowBehavior_RegistersBehaviorAsOpenGeneric()
    {
        var services = new ServiceCollection();
        services.AddAdvGenFlow(typeof(DependencyInjectionTests).Assembly);
        services.AddAdvGenFlowBehavior<TestOpenBehavior<,>>();
        var sp = services.BuildServiceProvider();

        sp.GetServices<IPipelineBehavior<TestRequest, int>>().Should().ContainSingle()
            .Which.Should().BeOfType<TestOpenBehavior<TestRequest, int>>();
    }

    [Fact]
    public async Task AddAdvGenFlow_EndToEnd_SendReturnsResult()
    {
        var services = new ServiceCollection();
        services.AddAdvGenFlow(typeof(DependencyInjectionTests).Assembly);
        var sender = services.BuildServiceProvider().GetRequiredService<ISender>();

        var result = await sender.Send(new TestRequest(21));

        result.Should().Be(42);
    }

    private class TestOpenBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken) => next();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/AdvGenFlow.Tests/AdvGenFlow.Tests.csproj --filter "FullyQualifiedName~DependencyInjectionTests" -v n
```
Expected: compile error — `AddAdvGenFlow` does not exist

- [ ] **Step 3: Implement ServiceCollectionExtensions**

`src/AdvGenFlow/DependencyInjection/ServiceCollectionExtensions.cs`:
```csharp
using System.Reflection;

namespace AdvGenFlow;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAdvGenFlow(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        services.AddTransient<IMediator, Mediator>();
        services.AddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
        services.AddTransient<IPublisher>(sp => sp.GetRequiredService<IMediator>());

        foreach (var assembly in assemblies)
            RegisterHandlers(services, assembly);

        return services;
    }

    public static IServiceCollection AddAdvGenFlowBehavior<TBehavior>(
        this IServiceCollection services)
        where TBehavior : class
    {
        // Open-generic registration requires the Type overload
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TBehavior));
        return services;
    }

    private static void RegisterHandlers(IServiceCollection services, Assembly assembly)
    {
        var openInterfaces = new[]
        {
            typeof(IRequestHandler<,>),
            typeof(INotificationHandler<>),
            typeof(IStreamRequestHandler<,>),
        };

        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface) continue;

            foreach (var iface in type.GetInterfaces())
            {
                if (!iface.IsGenericType) continue;

                var generic = iface.GetGenericTypeDefinition();
                if (Array.Exists(openInterfaces, o => o == generic))
                    services.AddTransient(iface, type);
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test tests/AdvGenFlow.Tests/AdvGenFlow.Tests.csproj --filter "FullyQualifiedName~DependencyInjectionTests" -v n
```
Expected: all tests PASS

- [ ] **Step 5: Run full test suite**

```bash
dotnet test tests/AdvGenFlow.Tests/AdvGenFlow.Tests.csproj -v n
```
Expected: all tests PASS

- [ ] **Step 6: Commit**

```bash
git add src/AdvGenFlow/DependencyInjection/ tests/AdvGenFlow.Tests/DependencyInjectionTests.cs
git commit -m "feat: add AddAdvGenFlow assembly scan DI registration and AddAdvGenFlowBehavior"
```

---

## Task 9: Source Generator — skeleton and snapshot infrastructure

Set up the incremental generator project, snapshot test infrastructure, and verify the empty-compilation case.

**Files:**
- Create: `src/AdvGenFlow.SourceGen/MediatorDispatchGenerator.cs`
- Create: `tests/AdvGenFlow.SourceGen.Tests/GeneratorTests.cs`
- Create: `tests/AdvGenFlow.SourceGen.Tests/ModuleInitializer.cs`

- [ ] **Step 1: Write the snapshot test for empty compilation**

`tests/AdvGenFlow.SourceGen.Tests/ModuleInitializer.cs`:
```csharp
using System.Runtime.CompilerServices;
using VerifyTests;

namespace AdvGenFlow.SourceGen.Tests;

public static class ModuleInitializer
{
    [ModuleInitializer]
    public static void Init() => VerifySourceGenerators.Initialize();
}
```

`tests/AdvGenFlow.SourceGen.Tests/GeneratorTests.cs`:
```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VerifyXunit;

namespace AdvGenFlow.SourceGen.Tests;

[UsesVerify]
public class GeneratorTests
{
    private static Compilation CreateCompilation(string source)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        // Add AdvGenFlow contracts assembly
        references.Add(MetadataReference.CreateFromFile(
            typeof(AdvGenFlow.IMediator).Assembly.Location));

        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static Task<VerifyResult> RunGenerator(string source)
    {
        var compilation = CreateCompilation(source);
        var generator = new MediatorDispatchGenerator();
        var driver = CSharpGeneratorDriver.Create(generator)
            .RunGenerators(compilation);

        return Verifier.Verify(driver).UseDirectory("Snapshots");
    }

    [Fact]
    public Task Generate_EmptyCompilation_EmitsValidClass()
        => RunGenerator("// no handlers");

    [Fact]
    public Task Generate_WithSingleRequestHandler_EmitsSwitchArm()
        => RunGenerator("""
            using AdvGenFlow;
            using System.Threading;
            using System.Threading.Tasks;

            public record PingCommand(string Message) : IRequest<string>;

            public class PingHandler : IRequestHandler<PingCommand, string>
            {
                public Task<string> Handle(PingCommand request, CancellationToken cancellationToken)
                    => Task.FromResult("pong");
            }
            """);

    [Fact]
    public Task Generate_WithMultipleHandlerTypes_EmitsAllDispatchMethods()
        => RunGenerator("""
            using AdvGenFlow;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Threading;
            using System.Threading.Tasks;

            public record CreateOrderCommand(int Id) : IRequest<int>;
            public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, int>
            {
                public Task<int> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
                    => Task.FromResult(request.Id);
            }

            public record OrderPlaced(int Id) : INotification;
            public class OrderPlacedHandler : INotificationHandler<OrderPlaced>
            {
                public Task Handle(OrderPlaced notification, CancellationToken cancellationToken)
                    => Task.CompletedTask;
            }

            public record LivePriceQuery(string Symbol) : IStreamRequest<decimal>;
            public class LivePriceHandler : IStreamRequestHandler<LivePriceQuery, decimal>
            {
                public async IAsyncEnumerable<decimal> Handle(LivePriceQuery request,
                    [EnumeratorCancellation] CancellationToken cancellationToken)
                {
                    yield return 1.0m;
                }
            }
            """);
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test tests/AdvGenFlow.SourceGen.Tests/AdvGenFlow.SourceGen.Tests.csproj -v n
```
Expected: compile error — `MediatorDispatchGenerator` does not exist

- [ ] **Step 3: Create the generator skeleton**

`src/AdvGenFlow.SourceGen/MediatorDispatchGenerator.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AdvGenFlow.SourceGen;

[Generator]
public sealed class MediatorDispatchGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Collect all class declarations that might be handlers
        var handlerCandidates = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => GetHandlerInfo(ctx))
            .Where(static info => info is not null)
            .Collect();

        context.RegisterSourceOutput(handlerCandidates, static (spc, handlers) =>
            Execute(spc, handlers!));
    }

    private static HandlerInfo? GetHandlerInfo(GeneratorSyntaxContext ctx)
    {
        if (ctx.Node is not ClassDeclarationSyntax classDecl) return null;
        var symbol = ctx.SemanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;
        if (symbol is null || symbol.IsAbstract) return null;

        var requestHandlers   = new List<(string requestType, string responseType, string handlerType)>();
        var notificationHandlers = new List<(string notificationType, string handlerType)>();
        var streamHandlers    = new List<(string requestType, string responseType, string handlerType)>();

        foreach (var iface in symbol.AllInterfaces)
        {
            if (!iface.IsGenericType) continue;
            var name = iface.OriginalDefinition.ToDisplayString();

            if (name == "AdvGenFlow.IRequestHandler<TRequest, TResponse>")
            {
                var req  = iface.TypeArguments[0].ToDisplayString();
                var resp = iface.TypeArguments[1].ToDisplayString();
                var handler = symbol.ToDisplayString();
                requestHandlers.Add((req, resp, handler));
            }
            else if (name == "AdvGenFlow.INotificationHandler<TNotification>")
            {
                var notif   = iface.TypeArguments[0].ToDisplayString();
                var handler = symbol.ToDisplayString();
                notificationHandlers.Add((notif, handler));
            }
            else if (name == "AdvGenFlow.IStreamRequestHandler<TRequest, TResponse>")
            {
                var req  = iface.TypeArguments[0].ToDisplayString();
                var resp = iface.TypeArguments[1].ToDisplayString();
                var handler = symbol.ToDisplayString();
                streamHandlers.Add((req, resp, handler));
            }
        }

        if (requestHandlers.Count == 0 && notificationHandlers.Count == 0 && streamHandlers.Count == 0)
            return null;

        return new HandlerInfo(requestHandlers, notificationHandlers, streamHandlers);
    }

    private static void Execute(SourceProductionContext context,
        IEnumerable<HandlerInfo> allHandlers)
    {
        // Aggregate all handlers from all classes
        var requestHandlers      = new List<(string req, string resp, string handler)>();
        var notificationHandlers = new List<(string notif, string handler)>();
        var streamHandlers       = new List<(string req, string resp, string handler)>();

        foreach (var info in allHandlers)
        {
            requestHandlers.AddRange(info.RequestHandlers);
            notificationHandlers.AddRange(info.NotificationHandlers);
            streamHandlers.AddRange(info.StreamHandlers);
        }

        var source = GenerateSource(requestHandlers, notificationHandlers, streamHandlers);
        context.AddSource("GeneratedMediator.g.cs", SourceText.From(source, Encoding.UTF8));
    }

    private static string GenerateSource(
        List<(string req, string resp, string handler)> requestHandlers,
        List<(string notif, string handler)> notificationHandlers,
        List<(string req, string resp, string handler)> streamHandlers)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine("using AdvGenFlow;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("internal sealed class GeneratedMediator : IMediator");
        sb.AppendLine("{");
        sb.AppendLine("    private readonly IServiceProvider _serviceProvider;");
        sb.AppendLine("    public GeneratedMediator(IServiceProvider serviceProvider)");
        sb.AppendLine("        => _serviceProvider = serviceProvider;");
        sb.AppendLine();

        // Send
        sb.AppendLine("    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request,");
        sb.AppendLine("        CancellationToken cancellationToken = default)");
        sb.AppendLine("    {");
        sb.AppendLine("        return request switch");
        sb.AppendLine("        {");
        foreach (var (req, resp, _) in requestHandlers)
        {
            var safeName = req.Replace(".", "_").Replace("<", "_").Replace(">", "_").Replace(",", "_").Replace(" ", "");
            sb.AppendLine($"            {req} __r => (Task<TResponse>)(object)SendCore_{safeName}(__r, cancellationToken),");
        }
        sb.AppendLine("            _ => throw new InvalidOperationException($\"No handler registered for {request.GetType().Name}\")");
        sb.AppendLine("        };");
        sb.AppendLine();
        foreach (var (req, resp, _) in requestHandlers)
        {
            var safeName = req.Replace(".", "_").Replace("<", "_").Replace(">", "_").Replace(",", "_").Replace(" ", "");
            sb.AppendLine($"        Task<{resp}> SendCore_{safeName}({req} r, CancellationToken ct)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var handler = _serviceProvider.GetRequiredService<IRequestHandler<{req}, {resp}>>();");
            sb.AppendLine($"            var behaviors = _serviceProvider.GetServices<IPipelineBehavior<{req}, {resp}>>();");
            sb.AppendLine($"            return AdvGenFlow.PipelineBuilder.Build(() => handler.Handle(r, ct), behaviors, r, ct);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
        sb.AppendLine("    }");
        sb.AppendLine();

        // Publish
        sb.AppendLine("    public Task Publish<TNotification>(TNotification notification,");
        sb.AppendLine("        CancellationToken cancellationToken = default)");
        sb.AppendLine("        where TNotification : INotification");
        sb.AppendLine("    {");
        sb.AppendLine("        var handlers = _serviceProvider.GetServices<INotificationHandler<TNotification>>();");
        sb.AppendLine("        return Task.WhenAll(handlers.Select(h => h.Handle(notification, cancellationToken)));");
        sb.AppendLine("    }");
        sb.AppendLine();

        // CreateStream
        sb.AppendLine("    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request,");
        sb.AppendLine("        CancellationToken cancellationToken = default)");
        sb.AppendLine("    {");
        sb.AppendLine("        return request switch");
        sb.AppendLine("        {");
        foreach (var (req, resp, _) in streamHandlers)
        {
            sb.AppendLine($"            {req} __r => (IAsyncEnumerable<TResponse>)(object)");
            sb.AppendLine($"                _serviceProvider.GetRequiredService<IStreamRequestHandler<{req}, {resp}>>()");
            sb.AppendLine($"                    .Handle(__r, cancellationToken),");
        }
        sb.AppendLine("            _ => throw new InvalidOperationException($\"No stream handler registered for {request.GetType().Name}\")");
        sb.AppendLine("        };");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private sealed class HandlerInfo(
        List<(string req, string resp, string handler)> requestHandlers,
        List<(string notif, string handler)> notificationHandlers,
        List<(string req, string resp, string handler)> streamHandlers)
    {
        public IReadOnlyList<(string req, string resp, string handler)> RequestHandlers => requestHandlers;
        public IReadOnlyList<(string notif, string handler)> NotificationHandlers => notificationHandlers;
        public IReadOnlyList<(string req, string resp, string handler)> StreamHandlers => streamHandlers;
    }
}
```

> **Note about `PipelineBuilder` access:** The generator emits a call to `AdvGenFlow.PipelineBuilder.Build(...)`. For this to compile, `PipelineBuilder` must be `public` (not `internal`). Change the `PipelineBuilder` class modifier from `internal` to `public` in `src/AdvGenFlow/Pipeline/PipelineBuilder.cs`.

- [ ] **Step 4: Make PipelineBuilder public**

Edit `src/AdvGenFlow/Pipeline/PipelineBuilder.cs` — change `internal static class` to `public static class`.

- [ ] **Step 5: Run snapshot tests to generate initial snapshots**

```bash
dotnet test tests/AdvGenFlow.SourceGen.Tests/AdvGenFlow.SourceGen.Tests.csproj -v n
```
Expected: tests FAIL on first run with "snapshot not found" — this is correct. Verify files are created in `tests/AdvGenFlow.SourceGen.Tests/Snapshots/`.

- [ ] **Step 6: Accept snapshots**

Rename the `.received.` files to `.verified.` to commit the expected output:
```bash
cd tests/AdvGenFlow.SourceGen.Tests/Snapshots
for f in *.received.*; do mv "$f" "${f/received/verified}"; done
```

- [ ] **Step 7: Run snapshot tests to verify they now pass**

```bash
dotnet test tests/AdvGenFlow.SourceGen.Tests/AdvGenFlow.SourceGen.Tests.csproj -v n
```
Expected: 3 tests PASS

- [ ] **Step 8: Commit**

```bash
git add src/AdvGenFlow.SourceGen/ src/AdvGenFlow/Pipeline/PipelineBuilder.cs
git add tests/AdvGenFlow.SourceGen.Tests/
git commit -m "feat: add incremental source generator emitting GeneratedMediator with typed dispatch"
```

---

## Task 10: AddAdvGenFlowGenerated DI registration

Register `GeneratedMediator` as the mediator when the source gen package is in use.

**Files:**
- Modify: `src/AdvGenFlow/DependencyInjection/ServiceCollectionExtensions.cs`
- No new tests — `GeneratedMediator` only exists in a consuming project's compilation (generated at build time). We verify it works via a manual integration check.

- [ ] **Step 1: Add AddAdvGenFlowGenerated to ServiceCollectionExtensions**

Add the following method to `src/AdvGenFlow/DependencyInjection/ServiceCollectionExtensions.cs` **only if `GeneratedMediator` is defined** in the consuming project. Since this is a conditional compilation concern, add it as a regular method — the consumer must call it only when `GeneratedMediator` exists (i.e., when they have the source gen package):

```csharp
// GeneratedMediator is emitted into the consumer's compilation by AdvGenFlow.SourceGen.
// It cannot be referenced from this library. The consumer passes it as a type argument.
// Consumer usage: services.AddAdvGenFlowGenerated<GeneratedMediator>(typeof(Program).Assembly)
public static IServiceCollection AddAdvGenFlowGenerated<TGeneratedMediator>(
    this IServiceCollection services,
    params Assembly[] assemblies)
    where TGeneratedMediator : class, IMediator
{
    services.AddTransient<IMediator, TGeneratedMediator>();
    services.AddTransient<ISender>(sp => sp.GetRequiredService<IMediator>());
    services.AddTransient<IPublisher>(sp => sp.GetRequiredService<IMediator>());

    foreach (var assembly in assemblies)
        RegisterHandlers(services, assembly);

    return services;
}
```

> **Design note:** `GeneratedMediator` is emitted into the consumer's compilation, not the `AdvGenFlow` assembly. Therefore the registration must be done via the generic `AddAdvGenFlowGenerated<TGeneratedMediator>` overload, where the consumer passes `GeneratedMediator` as the type argument. This is the only approach that works without requiring `AdvGenFlow` to reference a type it doesn't know about at compile time.
>
> Consumer usage:
> ```csharp
> services.AddAdvGenFlowGenerated<GeneratedMediator>(typeof(Program).Assembly);
> ```

- [ ] **Step 2: Build to verify**

```bash
dotnet build AdvGenFlow.sln
```
Expected: 0 errors

- [ ] **Step 3: Run all tests**

```bash
dotnet test AdvGenFlow.sln -v n
```
Expected: all tests PASS

- [ ] **Step 4: Commit**

```bash
git add src/AdvGenFlow/DependencyInjection/ServiceCollectionExtensions.cs
git commit -m "feat: add AddAdvGenFlowGenerated<T> for source-generated mediator DI registration"
```

---

## Task 11: Final verification

- [ ] **Step 1: Run the complete test suite**

```bash
dotnet test AdvGenFlow.sln -v normal
```
Expected: all tests in `AdvGenFlow.Tests` and `AdvGenFlow.SourceGen.Tests` PASS with 0 failures

- [ ] **Step 2: Build in Release mode**

```bash
dotnet build AdvGenFlow.sln -c Release
```
Expected: 0 errors, 0 warnings

- [ ] **Step 3: Update spec status to Approved**

Edit `docs/superpowers/specs/2026-03-18-advgenflow-design.md`:
Change `**Status:** Draft` to `**Status:** Approved`

- [ ] **Step 4: Final commit**

```bash
git add .
git commit -m "feat: complete AdvGenFlow v1 — core mediator, pipeline, DI, and source generator"
```

---

## Summary

| Task | Deliverable |
|---|---|
| 1 | Solution scaffolding, project files |
| 2 | All 10 contract interfaces |
| 3 | `PipelineBuilder` with TDD |
| 4 | `Mediator.Send` with TDD |
| 5 | `Mediator.Publish` with TDD |
| 6 | `Mediator.CreateStream` with TDD |
| 7 | Pipeline integration tests |
| 8 | `AddAdvGenFlow` + `AddAdvGenFlowBehavior` with TDD |
| 9 | `MediatorDispatchGenerator` with snapshot tests |
| 10 | `AddAdvGenFlowGenerated<T>` |
| 11 | Final build + test verification |
