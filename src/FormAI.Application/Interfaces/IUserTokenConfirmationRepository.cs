using FormAI.Domain.Entities;
using FormAI.Domain.Enums;

namespace FormAI.Application.Interfaces;

public interface IUserTokenConfirmationRepository
{
    Task AddAsync(UserConfirmationToken token, CancellationToken cancellationToken = default);
    Task<UserConfirmationToken?> FindActiveAsync(string tokenHash, TokenPurpose purpose,
        CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
