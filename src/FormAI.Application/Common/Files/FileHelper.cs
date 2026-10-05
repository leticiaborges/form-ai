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
}