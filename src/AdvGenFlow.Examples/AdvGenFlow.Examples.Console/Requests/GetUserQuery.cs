using AdvGenFlow;

namespace AdvGenFlow.Examples.ConsoleApp.Requests;

// Request
public record GetUserQuery(int UserId) : IRequest<UserDto>;

// Response DTO
public record UserDto(int Id, string Name, string Email);

// Handler
public class GetUserHandler : IRequestHandler<GetUserQuery, UserDto>
{
    public Task<UserDto> Handle(GetUserQuery request, CancellationToken cancellationToken)
    {
        // Simulate database lookup
        var user = new UserDto(
            Id: request.UserId,
            Name: $"User {request.UserId}",
            Email: $"user{request.UserId}@example.com"
        );
        
        Console.WriteLine($"  [Handler] Fetched user: {user.Name}");
        return Task.FromResult(user);
    }
}
