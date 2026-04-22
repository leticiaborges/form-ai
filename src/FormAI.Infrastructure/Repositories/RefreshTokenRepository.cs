using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FormAI.Infrastructure.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{

    private readonly AppDbContext _context;

    public RefreshTokenRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        return await _context.RefreshTokens.FirstOrDefaultAsync(t => t.Token == token, cancellationToken);
    }

    public async Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default)
    {
        await _context.RefreshTokens.AddAsync(refreshToken, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var token = await _context.RefreshTokens.FindAsync([id], cancellationToken);
        if (token is null)
            return;

        token.Revoke();
        
        await _context.SaveChangesAsync(cancellationToken);
    }
}
