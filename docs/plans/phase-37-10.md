# Slice 10: Source text extractor, `.txt`

Covers SC-3 (txt), SC-4 (txt) and SC-7 of [`phase-37.md`](./phase-37.md). It adds the `ISourceTextExtractor` boundary and its first format. **Nothing calls it yet**: the controller and `GenerateFormHandler` don't change until slice 13. Slices 11 and 12 add docx and pptx behind the same interface.

## Decisions

| Topic | Decision |
|---|---|
| Interface | `ISourceTextExtractor.Extract(string fileName, byte[] content)` in `Application/Interfaces/`. Synchronous: it is CPU work over bytes already in memory. |
| Implementation | `SourceTextExtractor` in `Infrastructure/Files/`. Only `.txt` for now. |
| Encoding | Try **strict UTF-8** first, so UTF-8 text keeps its special characters. If the bytes aren't valid UTF-8, fall back to **Windows-1252** (the realistic legacy encoding: curly quotes, long dash, accents). A UTF-8/16/32 BOM is detected and stripped by `StreamReader`, so UTF-16 saved with a BOM works too. |
| Cleaning | **Trim only**, then reject an empty result. No line-break normalization, no control-character removal (SC-6 is reduced to this on purpose). |
| Rejections | Wrong extension, over 10 MB, a NUL character in the decoded text, empty result. |
| Binary files renamed `.txt` | Not checked by signature. The NUL check rejects most PDFs, zips, docx and pptx. A binary with no NUL byte would pass, decoded as Windows-1252, and that is accepted. |
| Known limits | Another legacy encoding (Windows-1251, Shift-JIS) decodes as 1252 without an error, so the text is garbled. UTF-16 **without** a BOM is rejected (NULs). Both accepted. |
| Errors | `ValidationException(code, message)` with three new `ValidationErrorCode` values. 400, not a new exception type. |
| Size | `SourceFileLimits.MaxFileBytes` (10 MB) enforced by the extractor, so slice 13 needs no second check. |
| Not here | The 100 / 30,000 text limits (combined text, slice 13), file name sanitizing (slice 13), `.docx` / `.pptx` / `.pdf` (slices 11, 12, 14). Those extensions answer `SourceFileUnsupported` for now. |

## 1. Code

### 1.1 `src/FormAI.Application/Common/Exceptions/ValidationErrorCode.cs`

Append the three values at the end (keeps the existing numbers):

```csharp
namespace FormAI.Application.Common.Exceptions;

public enum ValidationErrorCode
{
    GenericError,
    FormExpired,
    AlreadySubmitted,
    EmailNotVerified,
    GenerationOutputInvalid,
    GenerationBudgetReached,
    GenerationUnavailable,
    SourceFileUnsupported,
    SourceFileUnreadable,
    SourceFileTooLarge
}
```

### 1.2 New `src/FormAI.Application/Interfaces/ISourceTextExtractor.cs`

```csharp
namespace FormAI.Application.Interfaces;

// Turns an uploaded file into plain text. Throws ValidationException with a SourceFile*
// code when the file is not acceptable. The original bytes are never stored.
public interface ISourceTextExtractor
{
    string Extract(string fileName, byte[] content);
}
```

### 1.3 New `src/FormAI.Application/Forms/SourceFileLimits.cs`

```csharp
namespace FormAI.Application.Forms;

public static class SourceFileLimits
{
    public const int MaxFileBytes = 10 * 1024 * 1024;
}
```

### 1.4 New `src/FormAI.Infrastructure/Files/SourceTextExtractor.cs`

```csharp
using System.Text;
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Forms;
using FormAI.Application.Interfaces;

namespace FormAI.Infrastructure.Files;

public class SourceTextExtractor : ISourceTextExtractor
{
    // Invalid bytes throw instead of becoming U+FFFD, so a failure means "not UTF-8".
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly Encoding Windows1252 = CreateWindows1252();

    private static Encoding CreateWindows1252()
    {
        // Windows-1252 is not built in on .NET Core. The provider ships with the runtime.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    public string Extract(string fileName, byte[] content)
    {
        // Slices 11 and 12 add .docx and .pptx here.
        if (!string.Equals(Path.GetExtension(fileName), ".txt", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(ValidationErrorCode.SourceFileUnsupported,
                "This file type is not supported. Use a .pdf, .docx, .pptx or .txt file.");
        }

        if (content.Length > SourceFileLimits.MaxFileBytes)
        {
            throw new ValidationException(ValidationErrorCode.SourceFileTooLarge,
                "The file is too large. The maximum size is 10 MB.");
        }

        return ExtractTxt(content);
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

    // The encoding is only the default: a UTF-8/16/32 BOM overrides it and is stripped.
    private static string Read(byte[] content, Encoding encoding)
    {
        using var reader = new StreamReader(new MemoryStream(content), encoding,
            detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static ValidationException Unreadable() =>
        new(ValidationErrorCode.SourceFileUnreadable,
            "The file could not be read or has no text in it.");
}
```

Messages are fixed and never include the file name or content.

### 1.5 `src/FormAI.Infrastructure/DependencyInjection.cs`

Add `using FormAI.Infrastructure.Files;` and, next to the other registrations:

```csharp
services.AddSingleton<ISourceTextExtractor, SourceTextExtractor>();
```

Nothing resolves it yet; register it now so slice 13 doesn't find it missing.

### 1.6 `tests/FormAI.UnitTests/FormAI.UnitTests.csproj`

The extractor lives in Infrastructure, so add next to the other `ProjectReference` lines:

```xml
<ProjectReference Include="..\..\src\FormAI.Infrastructure\FormAI.Infrastructure.csproj" />
```

## 2. Test examples

New file `tests/FormAI.UnitTests/Files/SourceTextExtractorTests.cs`. A few examples to start from; add more in the same style as you go (for instance a `.TXT` upper-case name, UTF-16 **without** a BOM expecting `SourceFileUnreadable`, and the exact `MaxFileBytes` boundary).

```csharp
using System.Text;
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Forms;
using FormAI.Infrastructure.Files;

namespace FormAI.UnitTests.Files;

public class SourceTextExtractorTests
{
    private readonly SourceTextExtractor _extractor = new();

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static ValidationErrorCode? CodeOf(string fileName, byte[] content)
    {
        var ex = Assert.Throws<ValidationException>(() => new SourceTextExtractor().Extract(fileName, content));
        return ex.Code;
    }

    [Fact]
    public void Extract_ReturnsTheText_WhenTheFileIsPlainUtf8()
    {
        var text = _extractor.Extract("notes.txt", Utf8("Educação não é gasto 🙂"));

        Assert.Equal("Educação não é gasto 🙂", text);
    }

    [Fact]
    public void Extract_TrimsLeadingAndTrailingWhitespace()
    {
        var text = _extractor.Extract("notes.txt", Utf8("\n\n  Paris is the capital.  \r\n"));

        Assert.Equal("Paris is the capital.", text);
    }

    [Theory]
    [InlineData("notes.docx")]
    [InlineData("notes.pdf")]
    [InlineData("notes")]
    public void Extract_Throws_SourceFileUnsupported_ForAnyOtherExtension(string fileName)
    {
        Assert.Equal(ValidationErrorCode.SourceFileUnsupported, CodeOf(fileName, Utf8("text")));
    }

    [Fact]
    public void Extract_Throws_SourceFileTooLarge_WhenOverTheLimit()
    {
        var content = new byte[SourceFileLimits.MaxFileBytes + 1];
        Array.Fill(content, (byte)'a');

        Assert.Equal(ValidationErrorCode.SourceFileTooLarge, CodeOf("big.txt", content));
    }

    [Fact]
    public void Extract_Throws_SourceFileUnreadable_WhenTheFileHasANulByte()
    {
        Assert.Equal(ValidationErrorCode.SourceFileUnreadable,
            CodeOf("bin.txt", [.. Utf8("abc"), 0x00, .. Utf8("def")]));
    }

    [Fact]
    public void Extract_KeepsSpecialCharacters_WhenTheFileIsUtf8()
    {
        // Must not come back as "cafÃ©".
        Assert.Equal("café", _extractor.Extract("notes.txt", Utf8("café")));
    }

    [Fact]
    public void Extract_FallsBackToWindows1252_WhenTheBytesAreNotValidUtf8()
    {
        // 0xE9 alone is "é" in Windows-1252 and invalid in UTF-8.
        Assert.Equal("café", _extractor.Extract("notes.txt", [0x63, 0x61, 0x66, 0xE9]));
        // 0x93 and 0x94 are the curly quotes in Windows-1252.
        Assert.Equal("“hi”", _extractor.Extract("notes.txt", [0x93, 0x68, 0x69, 0x94]));
    }

    [Fact]
    public void Extract_ReadsUtf16_WhenItHasABom()
    {
        var content = new UnicodeEncoding(false, true).GetPreamble()
            .Concat(Encoding.Unicode.GetBytes("Hello café")).ToArray();

        Assert.Equal("Hello café", _extractor.Extract("notes.txt", content));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n\t  ")]
    public void Extract_Throws_SourceFileUnreadable_WhenThereIsNoTextAfterTrimming(string content)
    {
        Assert.Equal(ValidationErrorCode.SourceFileUnreadable, CodeOf("empty.txt", Utf8(content)));
    }

    [Fact]
    public void Extract_DoesNotLeakTheFileNameInTheMessage()
    {
        var ex = Assert.Throws<ValidationException>(() =>
            _extractor.Extract("secret-report.txt", [0x61, 0x00, 0x62]));

        Assert.DoesNotContain("secret-report", ex.Message);
    }
}
```

## 3. Docs

- **`CLAUDE.md`**:
  - One sentence under Architecture: `ISourceTextExtractor` (`Application/Interfaces`) is implemented in `Infrastructure/Files/`, handles `.txt` only so far, and **is not called by the generate flow yet**.
  - Add `SourceFileUnsupported`, `SourceFileUnreadable` and `SourceFileTooLarge` to the `ValidationErrorCode` list.
  - Tests: `FormAI.UnitTests` also references Infrastructure, for pure Infrastructure classes such as the extractors.
- **`docs/known-gaps.md`**: under *Generation from PDF, Word, image or URL*, note that a `.txt` extractor exists but the endpoint doesn't accept a file yet.
- **`CONTEXT.md`**: check that **Extracted text** reads as "text taken from a file, trimmed". No new term otherwise.
- No ADR.

## 4. Verify

```bash
dotnet build FormAI.sln
dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj
```

## Done when

- `ISourceTextExtractor` exists and is registered, and `.txt` extraction follows the rules above.
- The example tests pass.
- `GenerateFormHandler`, the controller and the frontend are unchanged.
- `CLAUDE.md` and `docs/known-gaps.md` describe the extractor as built and not yet wired.

## Suggested commits

1. `feat(application): add ISourceTextExtractor and the source file error codes`
2. `feat(infrastructure): extract text from .txt files`
3. `test: cover the txt extractor`
4. `docs: record the text extractor in CLAUDE.md and known-gaps`
