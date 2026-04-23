namespace FormAI.Domain.Entities;

public class User
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }

    public DateTime? ConfirmationSentAt { get; private set;}
    public DateTime? PendingRegistrationExpiresAt { get; private set;}
    public bool IsEmailVerified { get; private set; }
    public DateTime? VerifiedAt { get; private set; }
    
    public List<Form> Forms { get; private set; } = new();
    public List<RefreshToken> RefreshTokens { get; private set; } = new ();

    public List<UserConfirmationToken> ConfirmationTokens {get; private set;} = new ();

    private User() { }

    public static User Create(string name, string email, string passwordHash)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            Name = name,
            Email = email,
            PasswordHash = passwordHash,
            CreatedAt = DateTime.UtcNow,
            IsEmailVerified = false
        };
    }

    public void SetConfirmationSent()
    {
        ConfirmationSentAt = DateTime.UtcNow;
        PendingRegistrationExpiresAt = ConfirmationSentAt.GetValueOrDefault().AddMinutes(15);
    }

    public void MarkAsVerified()
    {
        IsEmailVerified = true;
        VerifiedAt = DateTime.UtcNow;
    }
}
