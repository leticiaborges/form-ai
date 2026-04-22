namespace FormAI.Application.Users.Auth;

public record RegisterUserRequest(string Name, string Email, string Password);

public record RegisterUserResponse(Guid Id, string Name, string Email);
