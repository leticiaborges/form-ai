using System.ComponentModel.DataAnnotations;
using System.Text;
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;

namespace FormAI.Infrastructure.Files;

public class SourceTextExtractor : ISourceTextExtractor
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly Encoding Windows1252 = CreateWindows1252();

    private static Encoding CreateWindows1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    public string Extract(string fileName, byte[] content)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (extension is not (".txt" or ".docx"))
        {
            throw new Application.Common.Exceptions.ValidationException(ValidationErrorCode.SourceFileUnsupported,
                "This file type is not supported. Use a .pdf, .docx, .pptx or .txt file.");
        }

        if (content.Length > FormSourceContent.MaxFileBytes)
        {
            throw new Application.Common.Exceptions.ValidationException(ValidationErrorCode.SourceFileTooLarge,
                "The file is too large. The maximum size is 10 MB.");
        }

        return extension == ".txt" ? ExtractTxt(content) : DocxTextExtractor.Extract(content);
    }

    private static string ExtractTxt(byte[] content)
    {
        var text = Decode(content);

        // Postgres can't store NUL in a text column. UTF-16 without a BOM, and most
        // binaries, end up here.
        if (text.Contains('\0'))
            throw Unreadable();

        text = text.Trim();

        // Also covers an empty file.
        if (text.Length == 0)
            throw Unreadable();

        return text;
    }

    private static string Decode(byte[] content)
    {
        try
        {
            return Read(content, StrictUtf8);
        }
        catch (DecoderFallbackException)
        {
            return Read(content, Windows1252);
        }
    }

    private static string Read(byte[] content, Encoding encoding)
    {
        using var reader = new StreamReader(new MemoryStream(content), encoding,
            detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    internal static Application.Common.Exceptions.ValidationException Unreadable() =>
        new(ValidationErrorCode.SourceFileUnreadable,
            "The file could not be read or has no text in it.");
}
