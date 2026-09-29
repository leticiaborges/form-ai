using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;

namespace FormAI.Application.Users.Auth;

public class StartDemoHandler
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtService _jwtService;
    private readonly DemoAccountSettings _settings;

    public StartDemoHandler(IUserRepository users, IRefreshTokenRepository refreshTokens,
        IPasswordHasher hasher, IJwtService jwtService, DemoAccountSettings settings)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _hasher = hasher;
        _jwtService = jwtService;
        _settings = settings;
    }

    public async Task<AuthTokens> HandleAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.Password))
            throw new NotFoundException("Not found.");

        var user = User.CreateDemo(_hasher.Hash(_settings.Password));
        await _users.AddAsync(user, cancellationToken);

        var accessToken = _jwtService.GenerateAccessToken(user);
        var refreshTokenStr = _jwtService.GenerateRefreshToken();
        var refreshTokenExpiresAt = DateTime.UtcNow.Add(_jwtService.RefreshTokenLifetime);

        var refreshToken = RefreshToken.Create(user.Id, _jwtService.HashRefreshToken(refreshTokenStr), refreshTokenExpiresAt);
        await _refreshTokens.AddAsync(refreshToken, cancellationToken);

        return new AuthTokens(accessToken, refreshTokenStr, refreshTokenExpiresAt);
    }
}
