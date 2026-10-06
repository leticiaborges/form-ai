using System.Net.Mail;

namespace FormAI.Application.Common;

public static class EmailHelper
{
    public const int MaxEmailLength = 256;
    public static bool IsValidEmail(string? email)
    {
        var trimmed = email?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxEmailLength)
            return false;

        if (!MailAddress.TryCreate(trimmed, out var address) || address.Address != trimmed)
            return false;

        return address.Host.Contains('.');
    }
}