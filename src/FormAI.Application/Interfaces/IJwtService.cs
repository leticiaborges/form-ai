using FormAI.Domain.Entities;

namespace FormAI.Application.Interfaces;

public interface IJwtService
{
    TimeSpan RefreshTokenLifetime { get; }

    string GenerateAccessToken(User user);
    string GenerateRefreshToken();

    string HashRefreshToken(string refreshToken);
}
