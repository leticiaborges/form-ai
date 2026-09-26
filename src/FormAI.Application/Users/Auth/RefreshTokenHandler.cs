

using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;

namespace FormAI.Application.Users.Auth;

public class RefreshTokenHandler
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IJwtService _jwtService;

    public RefreshTokenHandler(IUserRepository users, IRefreshTokenRepository refreshTokens, IJwtService jwtService)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _jwtService = jwtService;
    }

    public async Task<AuthTokens> HandleAsync(RefreshTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        var token = await _refreshTokens.GetByTokenHashAsync(
            _jwtService.HashRefreshToken(request.RefreshToken), cancellationToken);
        if (token == null || !token.IsActive)
            throw new UnauthorizedAccessException("Invalid refresh token");

        var user = await _users.GetByIdAsync(token.UserId, cancellationToken);
        if (user == null)
            throw new UnauthorizedAccessException("Invalid refresh token");

        var accessToken = _jwtService.GenerateAccessToken(user);
        var refreshTokenStr = _jwtService.GenerateRefreshToken();
        var refreshTokenExpiresAt = DateTime.UtcNow.Add(_jwtService.RefreshTokenLifetime);

        var newRefreshToken = RefreshToken.Create(user.Id, _jwtService.HashRefreshToken(refreshTokenStr), refreshTokenExpiresAt);

        token.Revoke(newRefreshToken.TokenHash);

        await _refreshTokens.AddAsync(newRefreshToken, cancellationToken);

        return new AuthTokens(accessToken, refreshTokenStr, refreshTokenExpiresAt);
    }
}
