using FormAI.Application.Common.Exceptions;
using FormAI.Domain.Entities;

namespace FormAI.Application.Common.Files;

public static class FileHelper
{
    public const int MaxFileNameLength = 255;

    public static string SanitizeFileName(string? name)
    {
        name ??= string.Empty;
        var clean = Path.GetFileName(name.Replace('\\', '/'));
        clean = clean.Trim();

        return clean.Length > MaxFileNameLength ? clean[^MaxFileNameLength..] : clean;
    }

    public static bool IsPdf(string fileName) =>
       string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase);

    public static void EnsureWithinSizeLimit(byte[] content)
    {
        if (content.Length > FormSourceContent.MaxFileBytes)
        {
            throw new ValidationException(ValidationErrorCode.SourceFileTooLarge,
                "The file is too large. The maximum size is 10 MB.");
        }
    }

    public static void EnsurePdfIsAcceptable(byte[] content)
    {
        EnsureWithinSizeLimit(content);

        if (content.Length == 0)
            throw PdfUnreadable();
    }

    public static ValidationException PdfUnreadable() =>
        new(ValidationErrorCode.SourceFileUnreadable,
            "The PDF could not be processed. It may be damaged, password-protected or too long.");
}