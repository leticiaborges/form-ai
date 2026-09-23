

using FormAI.Application.Common.Exceptions;
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

    public async Task<LoginResponse> HandleAsync(RefreshTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        var token = await _refreshTokens.GetByTokenAsync(request.RefreshToken, cancellationToken);
        if (token == null || !token.IsActive)
            throw new NotFoundException("Invalid refresh token");

        var user = await _users.GetByIdAsync(token.UserId, cancellationToken);
        if (user == null)
            throw new NotFoundException("Invalid refresh token");

        var accessToken = _jwtService.GenerateAccessToken(user);
        var refreshTokenStr = _jwtService.GenerateRefreshToken();

        var newRefreshToken = RefreshToken.Create(user.Id, refreshTokenStr, DateTime.UtcNow.AddDays(RefreshToken.ExpiryDays));

        token.Revoke(refreshTokenStr);

        await _refreshTokens.AddAsync(newRefreshToken, cancellationToken);

        return new LoginResponse(accessToken, refreshTokenStr);
    }
}