using FormAI.Domain.Entities;
using FormAI.Infrastructure.Data;
using FormAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace FormAI.IntegrationTests;

public class RefreshTokenRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
    .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task DeleteInactiveTokensAsync_RemovesOnlyRowsInactiveBeforeCutoff()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(_postgres.GetConnectionString()).UseSnakeCaseNamingConvention().Options;

        await using var context = new AppDbContext(options);
        await context.Database.MigrateAsync();

        var repo = new RefreshTokenRepository(context);
        var user = User.Create("Test User", "cleanup-test@example.com", "hash");
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var userId = user.Id;
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(-10);

        var active = RefreshToken.Create(userId, "hash-active", now.AddDays(5));

        var revokedOld = RefreshToken.Create(userId, "hash-revoked-old", now.AddDays(5));
        revokedOld.Revoke();
        revokedOld.RevokeWithTime(now.AddDays(-11), "replace");

        var revokedRecent = RefreshToken.Create(userId, "hash-revoked-recent", now.AddDays(5));
        revokedRecent.RevokeWithTime(now.AddDays(-1), "replace");

        var expiredOld = RefreshToken.Create(userId, "hash-expired-old", now.AddDays(-11));

        var expiredRecent = RefreshToken.Create(userId, "hash-expired-recent", now.AddDays(-1));

        context.RefreshTokens.AddRange(active, revokedOld, revokedRecent, expiredOld, expiredRecent);
        await context.SaveChangesAsync();

        var deleted = await repo.DeleteInactiveTokensAsync(cutoff);

        Assert.Equal(2, deleted);

        var remainingHashes = await context.RefreshTokens.Select(t => t.TokenHash).ToListAsync();
        Assert.Contains("hash-active", remainingHashes);
        Assert.Contains("hash-revoked-recent", remainingHashes);
        Assert.Contains("hash-expired-recent", remainingHashes);
        Assert.DoesNotContain("hash-revoked-old", remainingHashes);
        Assert.DoesNotContain("hash-expired-old", remainingHashes);
    }
}
