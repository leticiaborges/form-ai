using System.Net.Mail;
using System.Text;
using FormAI.Application.Common;
using FormAI.Application.Common.Exceptions;

namespace FormAI.Application.Users.Auth;

public static class RegisterRequestValidator
{
    public const int MinNameLength = 2;
    public const int MaxNameLength = 100;
    public const int MinPasswordLength = 8;

    // bcrypt only reads the first 72 bytes, so a longer password would silently be cut.
    public const int MaxPasswordBytes = 72;

    public static void Validate(RegisterUserRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length < MinNameLength || name.Length > MaxNameLength)
            errors["name"] = [$"Name must be between {MinNameLength} and {MaxNameLength} characters."];

        if (!EmailHelper.IsValidEmail(request.Email))
            errors["email"] = ["Enter a valid email address."];

        var passwordError = PasswordError(request.Password);
        if (passwordError is not null)
            errors["password"] = [passwordError];

        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private static string? PasswordError(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            return $"Password must be at least {MinPasswordLength} characters.";

        if (Encoding.UTF8.GetByteCount(password) > MaxPasswordBytes)
            return $"Password must be at most {MaxPasswordBytes} bytes.";

        if (!password.Any(char.IsUpper))
            return "Password must contain at least one uppercase letter.";

        if (!password.Any(char.IsDigit))
            return "Password must contain at least one number.";

        return null;
    }
}
