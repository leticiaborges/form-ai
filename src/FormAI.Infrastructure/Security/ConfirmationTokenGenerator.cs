using System.Security.Cryptography;
using System.Text;
using FormAI.Application.Interfaces;

namespace FormAI.Infrastructure.Security;

public class ConfirmationTokenGenerator : IConfirmationTokenGenerator
{
    public (string RawToken, string TokenHash) Generate()
    {
        // Generate raw token (sent to user) and hash (stored in DB — raw never persisted)
        var tokenBytes = RandomNumberGenerator.GetBytes(64);
        var rawToken = Convert.ToBase64String(tokenBytes)
            .Replace("+", "-").Replace("/", "_").Replace("=", ""); // URL-safe base64

        var tokenHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        return (rawToken, tokenHash);
    }
}
