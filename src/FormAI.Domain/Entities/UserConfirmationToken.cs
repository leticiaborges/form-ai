using FormAI.Domain.Enums;

namespace FormAI.Domain.Entities;

public class UserConfirmationToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public TokenPurpose Purpose { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? UsedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public User User { get; private set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsUsed => UsedAt is not null;
    public bool IsActive => !IsExpired && !IsUsed;

    private UserConfirmationToken()
    {
        TokenHash = string.Empty;
    }

    public static UserConfirmationToken Create(Guid userId, string hash, TokenPurpose purpose, DateTime expiresAt)
    {
        var createdAt = DateTime.UtcNow;
        return new UserConfirmationToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = hash,
            Purpose = purpose,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt
        };
    }

    public void MarkUsed()
    {
        UsedAt = DateTime.UtcNow;
    }
}
