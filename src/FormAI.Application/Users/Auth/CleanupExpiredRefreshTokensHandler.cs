using FormAI.Application.Interfaces;

namespace FormAI.Application.Users.Auth;

public class CleanupExpiredRefreshTokensHandler
{
    private readonly IRefreshTokenRepository _refreshTokens;

    public CleanupExpiredRefreshTokensHandler(IRefreshTokenRepository refreshTokens)
    {
        _refreshTokens = refreshTokens;
    }

    public Task<int> HandleAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        return _refreshTokens.DeleteInactiveTokensAsync(cutoff, cancellationToken);
    }
}
