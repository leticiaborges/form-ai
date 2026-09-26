using FormAI.Application.Interfaces;
using NSubstitute;

namespace FormAI.UnitTests.Users;

public static class AuthTestData
{
    public const string Name = "TestUser";
    public const string Email = "testuser@example.com";
    public const string Password = "Secret123!";
    public const string HashedPassword = "hashed-value";

    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    public static string HashOf(string rawToken) => $"hash:{rawToken}";

    public static void ConfigureRefreshTokens(this IJwtService jwtService)
    {
        jwtService.RefreshTokenLifetime.Returns(RefreshTokenLifetime);
        jwtService.HashRefreshToken(Arg.Any<string>()).Returns(call => HashOf(call.Arg<string>()));
    }
}
