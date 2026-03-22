using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using VerifyXunit;

namespace AdvGenFlow.SourceGen.Tests;

// [UsesVerify] is not needed with Verify.Xunit v6+ — attribute was deprecated
public class GeneratorTests
{
    private static Compilation CreateCompilation(string source)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

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
