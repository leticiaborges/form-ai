using FormAI.Application.Interfaces;

namespace FormAI.Application.Users.Auth;

public class LogoutHandler
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IJwtService _jwtService;

    public LogoutHandler(IRefreshTokenRepository refreshTokens, IJwtService jwtService)
    {
        _refreshTokens = refreshTokens;
        _jwtService = jwtService;
    }

    public async Task HandleAsync(LogoutRequest request,
        CancellationToken cancellationToken = default)
    {
        var token = await _refreshTokens.GetByTokenHashAsync(
            _jwtService.HashRefreshToken(request.RefreshToken), cancellationToken);

        if (token == null || !token.IsActive)
            return;

        await _refreshTokens.RevokeAsync(token.Id, cancellationToken);
    }
}
