namespace FormAI.Application.Users.Auth;

public record LoginRequest(string Email, string Password);

public record LoginResponse(string AccessToken, string RefreshToken);

public record AuthTokens(string AccessToken, string RefreshToken, DateTime RefreshTokenExpiresAt);
