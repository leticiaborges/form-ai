using FormAI.Domain.Enums;

namespace FormAI.Domain.Entities;

public class RefreshToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }

    private RefreshToken()
    {
        TokenHash = string.Empty;
    }

    public static RefreshToken Create(Guid userId, string tokenHash, DateTime expiresAt)
    {
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt
        };
    }

    public void Revoke(string? replacedByTokenHash = null)
    {
        RevokeWithTime(DateTime.UtcNow, replacedByTokenHash);
    }

    public void RevokeWithTime(DateTime revokedAt, string? replacedByTokenHash = null)
    {
        RevokedAt = revokedAt;
        ReplacedByTokenHash = replacedByTokenHash;
    }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    public bool IsRevoked => RevokedAt is not null;

    public bool IsActive => !IsRevoked && !IsExpired;
}
