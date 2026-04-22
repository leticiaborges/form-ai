

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

    public async Task<LoginResponse> HandleAsync(LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByEmailAsync(request.Email, cancellationToken);

        if (user == null || !_hasher.Verify(request.Password, user.PasswordHash))
            throw new NotFoundException("Invalid user or password.");

        var accessToken = _jwtService.GenerateAccessToken(user);
        var refreshTokenStr = _jwtService.GenerateRefreshToken();

        var refreshToken = RefreshToken.Create(user.Id, refreshTokenStr, DateTime.UtcNow.AddDays(7));

        await _refreshTokens.AddAsync(refreshToken);
        
        return new LoginResponse(accessToken, refreshTokenStr);
    }
}