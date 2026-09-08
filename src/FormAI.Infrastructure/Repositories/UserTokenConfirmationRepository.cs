using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Domain.Enums;
using FormAI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FormAI.Infrastructure.Repositories;

public class UserTokenConfirmationRepository : IUserTokenConfirmationRepository
{
    private readonly AppDbContext _context;

    public UserTokenConfirmationRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(UserConfirmationToken token, CancellationToken cancellationToken = default)
    {
        await _context.UserConfirmationTokens.AddAsync(token, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<UserConfirmationToken?> FindActiveAsync(string tokenHash, TokenPurpose purpose, CancellationToken cancellationToken = default)
    {
        return await _context.UserConfirmationTokens.FirstOrDefaultAsync(t =>
            t.TokenHash == tokenHash &&
            t.UsedAt == null &&
            t.Purpose == purpose &&
            t.ExpiresAt > DateTime.UtcNow, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }

}
