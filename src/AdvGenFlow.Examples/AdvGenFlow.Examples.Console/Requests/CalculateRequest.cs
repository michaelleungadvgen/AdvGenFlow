using AdvGenFlow;

namespace AdvGenFlow.Examples.ConsoleApp.Requests;

// Request with operation
public record CalculateRequest(double A, double B, string Operation) : IRequest<double>;

// Handler
public class CalculateHandler : IRequestHandler<CalculateRequest, double>
{
    public Task<double> Handle(CalculateRequest request, CancellationToken cancellationToken)
    {
        double result = request.Operation.ToLower() switch
        {
            "add" => request.A + request.B,
            "subtract" => request.A - request.B,
            "multiply" => request.A * request.B,
            "divide" => request.B != 0 ? request.A / request.B : 0,
            _ => throw new ArgumentException($"Unknown operation: {request.Operation}")
        };

        Console.WriteLine($"  [Handler] Calculated: {request.A} {GetOperator(request.Operation)} {request.B} = {result}");
        return Task.FromResult(result);
    }

    private static string GetOperator(string operation) => operation.ToLower() switch
    {
        "add" => "+",
        "subtract" => "-",
        "multiply" => "*",
        "divide" => "/",
        _ => "?"
    };
}
