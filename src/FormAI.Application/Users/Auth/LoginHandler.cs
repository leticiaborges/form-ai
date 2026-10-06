

using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;

namespace FormAI.Application.Users.Auth;

public class LoginHandler
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtService _jwtService;

    public LoginHandler(IUserRepository users, IRefreshTokenRepository refreshTokens,
        IPasswordHasher hasher, IJwtService jwtService)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _hasher = hasher;
        _jwtService = jwtService;
    }

    public async Task<AuthTokens> HandleAsync(LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByEmailAsync(User.NormalizeEmail(request.Email ?? string.Empty), cancellationToken);

        if (user == null || !_hasher.Verify(request.Password ?? string.Empty, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid user or password.");

        if (!user.IsEmailVerified)
            throw new ValidationException(ValidationErrorCode.EmailNotVerified,
                "Please confirm your email address before logging in.");

        var accessToken = _jwtService.GenerateAccessToken(user);
        var refreshTokenStr = _jwtService.GenerateRefreshToken();
        var refreshTokenExpiresAt = DateTime.UtcNow.Add(_jwtService.RefreshTokenLifetime);

        var refreshToken = RefreshToken.Create(user.Id, _jwtService.HashRefreshToken(refreshTokenStr), refreshTokenExpiresAt);

        await _refreshTokens.AddAsync(refreshToken, cancellationToken);

        return new AuthTokens(accessToken, refreshTokenStr, refreshTokenExpiresAt);
    }
}
