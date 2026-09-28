using FormAI.Domain.Entities;

namespace FormAI.Application.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default);
    Task RevokeAsync(Guid id, CancellationToken cancellationToken = default);
    Task<int> DeleteInactiveTokensAsync(DateTime olderThan, CancellationToken cancellationToken = default);
}
