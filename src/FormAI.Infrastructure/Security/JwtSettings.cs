namespace FormAI.Infrastructure.Security;

public class JwtSettings
{
    public string Secret { get; init; } = string.Empty;
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public int ExpiresInMinutes { get; init; } = 60;
    public int RefreshTokenExpiryDays { get; init; } = 7;
}
