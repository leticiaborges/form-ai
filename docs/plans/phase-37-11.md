# Slice 11: Source text extractor, `.docx`, with zip protections

Covers SC-3 (docx), SC-5 and SC-7 of [`phase-37.md`](./phase-37.md). It adds `.docx` to the `SourceTextExtractor` from [slice 10](./phase-37-10.md), behind the same `ISourceTextExtractor` interface. **Nothing calls it yet**: the controller and `GenerateFormHandler` don't change until slice 13. Slice 12 (pptx) reuses the zip limits introduced here.

## Decisions

| Topic | Decision |
|---|---|
| Dependencies | None new. `System.IO.Compression` and `System.Xml` only, no OpenXML SDK. |
| What a docx is | A ZIP archive. The text is in one entry, `word/document.xml`. |
| Is it really a docx | **No separate PK / magic-byte check.** `ZipArchive` throws `InvalidDataException` when the file is not a ZIP, and a ZIP without `word/document.xml` (a pptx, an xlsx, a plain zip renamed `.docx`) is rejected. Both answer `SourceFileUnreadable`. |
| Entry count | `ZipLimits.MaxEntries = 1000`, checked right after opening, before reading anything. One line; a real docx has tens to a couple of hundred. Reused by pptx in slice 12. |
| Decompressed size | `ZipLimits.MaxDecompressedBytes = 50 MB`, enforced by **counting the bytes actually read** while streaming (`LimitedReadStream`). Header sizes are never trusted. Only one entry is read, so the cap applies to `word/document.xml`. |
| XML | `XmlReader` with `DtdProcessing.Prohibit` and no resolver (DTD / XXE off). Streaming, no DOM. |
| What is extracted | Text of `w:t` runs. A newline at the end of each `w:p`, a tab for `w:tab`, a newline for `w:br` and `w:cr`. Table cells are paragraphs inside `w:tc`, so they come out as lines with no extra code. |
| What is ignored | Headers, footers, footnotes, comments, text boxes' alt text, images, charts, embedded objects, field codes (`w:instrText`) and tracked-deletion text (`w:delText`). |
| Cleaning | **Trim only**, then reject an empty result, as in slice 10. XML 1.0 already forbids NUL and most control characters, and `XmlReader` throws on them, so they end as `SourceFileUnreadable`. |
| Errors | `ValidationException` with the codes from slice 10. 400, no new exception type. Messages are fixed and never include the file name or content. |
| Size of the result | Not capped here. The 30,000 character limit is applied to the combined text in slice 13. A docx at the 50 MB XML limit can produce a very long string for a moment; accepted, because slice 13 rejects it right after. |
| Known limits | Text inside text boxes or SmartArt is not read (it lives in `mc:AlternateContent` / drawing XML, and only `w:t` under the main body is read through the same loop, so some may or may not appear). Password-protected docx is not a ZIP (it is an OLE container) and answers `SourceFileUnreadable`. Old `.doc` stays unsupported. |

## 1. Code

### 1.1 New `src/FormAI.Infrastructure/Files/ZipLimits.cs`

Public so the unit tests can build files at the boundary.

```csharp
namespace FormAI.Infrastructure.Files;

// Zip-bomb protections shared by the docx and pptx extractors.
public static class ZipLimits
{
    // A real docx has tens of entries, a pptx a few hundred.
    public const int MaxEntries = 1000;

    // Counted while reading, never taken from the archive headers.
    public const long MaxDecompressedBytes = 50L * 1024 * 1024;
}
```

### 1.2 New `src/FormAI.Infrastructure/Files/LimitedReadStream.cs`

Wraps the entry stream and aborts once more than `limit` bytes have been read. `ZipArchive`'s entry stream is forward-only, and `XmlReader` only reads, so this is all it needs.

```csharp
namespace FormAI.Infrastructure.Files;

// Throws InvalidDataException as soon as the bytes actually read pass the limit.
internal sealed class LimitedReadStream(Stream inner, long limit) : Stream
{
    private long _read;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Count(inner.Read(buffer, offset, count));

    public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

    private int Count(int bytesRead)
    {
        _read += bytesRead;
        if (_read > limit)
            throw new InvalidDataException("Decompressed size limit exceeded.");
        return bytesRead;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();
        base.Dispose(disposing);
    }
}
```

### 1.3 New `src/FormAI.Infrastructure/Files/DocxTextExtractor.cs`

```csharp
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace FormAI.Infrastructure.Files;

internal static class DocxTextExtractor
{
    private const string DocumentEntry = "word/document.xml";
    private const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static string Extract(byte[] content)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);

            // Checked before reading anything.
            if (archive.Entries.Count > ZipLimits.MaxEntries)
                throw SourceTextExtractor.Unreadable();

            // Also rejects a pptx, an xlsx or a plain zip renamed to .docx.
            var entry = archive.GetEntry(DocumentEntry) ?? throw SourceTextExtractor.Unreadable();

            using var stream = new LimitedReadStream(entry.Open(), ZipLimits.MaxDecompressedBytes);
            var text = ReadText(stream).Trim();

            // Also covers a document with only images.
            if (text.Length == 0)
                throw SourceTextExtractor.Unreadable();

            return text;
        }
        // Not a zip, a corrupt zip, the size limit, or malformed XML (including a DTD).
        catch (Exception ex) when (ex is InvalidDataException or XmlException or NotSupportedException)
        {
            throw SourceTextExtractor.Unreadable();
        }
    }

    private static string ReadText(Stream stream)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        var text = new StringBuilder();
        var inText = false;

        using var xml = XmlReader.Create(stream, settings);
        while (xml.Read())
        {
            switch (xml.NodeType)
            {
                case XmlNodeType.Element when xml.NamespaceURI == WordNamespace:
                    switch (xml.LocalName)
                    {
                        case "t":
                            inText = !xml.IsEmptyElement;
                            break;
                        case "tab":
                            text.Append('\t');
                            break;
                        case "br":
                        case "cr":
                            text.Append('\n');
                            break;
                        case "p" when xml.IsEmptyElement:
                            text.Append('\n');
                            break;
                    }
                    break;

                case XmlNodeType.EndElement when xml.NamespaceURI == WordNamespace:
                    if (xml.LocalName == "t")
                        inText = false;
                    else if (xml.LocalName == "p")
                        text.Append('\n');
                    break;

                case XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace
                    when inText:
                    text.Append(xml.Value);
                    break;
            }
        }

        return text.ToString();
    }
}
```

### 1.4 `src/FormAI.Infrastructure/Files/SourceTextExtractor.cs`

Three changes: `.docx` is accepted, `Extract` dispatches by extension, and `Unreadable()` becomes `internal static` so the docx extractor reuses the same message.

Replace the `Extract` method with:

```csharp
    public string Extract(string fileName, byte[] content)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        // Slice 12 adds .pptx here.
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
```

And change the last method's signature (body unchanged):

```csharp
    internal static Application.Common.Exceptions.ValidationException Unreadable() =>
        new(ValidationErrorCode.SourceFileUnreadable,
            "The file could not be read or has no text in it.");
```

Nothing else in the file changes. `DependencyInjection.cs` and the test project reference were done in slice 10.

## 2. Tests

### 2.1 Fix an existing test

`tests/FormAI.UnitTests/Files/SourceTextExtractorTests.cs` lists `notes.docx` as an unsupported extension. It is supported now. Replace that `InlineData` with `.pptx`:

```csharp
    [Theory]
    [InlineData("notes.pptx")]
    [InlineData("notes.pdf")]
    [InlineData("notes")]
    public void Extract_Throws_SourceFileUnsupported_ForAnyOtherExtension(string fileName)
```

### 2.2 New `tests/FormAI.UnitTests/Files/DocxTextExtractorTests.cs`

The tests go through `SourceTextExtractor`, the public entry point. They build real `.docx` archives in memory.

```csharp
using System.IO.Compression;
using System.Text;
using FormAI.Application.Common.Exceptions;
using FormAI.Domain.Entities;
using FormAI.Infrastructure.Files;

namespace FormAI.UnitTests.Files;

public class DocxTextExtractorTests
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private readonly SourceTextExtractor _extractor = new();

    // ---- helpers ----

    private static string Document(string body) =>
        $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<w:document xmlns:w=\"{W}\"><w:body>{body}</w:body></w:document>";

    private static string Para(string text) => $"<w:p><w:r><w:t>{text}</w:t></w:r></w:p>";

    private static byte[] Docx(string documentXml, int extraEntries = 0) =>
        Zip(zip =>
        {
            Write(zip, "[Content_Types].xml", "<Types/>");
            Write(zip, "word/document.xml", documentXml);
            for (var i = 0; i < extraEntries; i++)
                Write(zip, $"word/media/image{i}.png", "x");
        });

    private static byte[] Zip(Action<ZipArchive> fill)
    {
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            fill(zip);
        return buffer.ToArray();
    }

    private static void Write(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private ValidationErrorCode? CodeOf(string fileName, byte[] content)
    {
        var ex = Assert.Throws<ValidationException>(() => _extractor.Extract(fileName, content));
        return ex.Code;
    }

    // ---- extraction ----

    [Fact]
    public void Extract_ReturnsOneLinePerParagraph()
    {
        var text = _extractor.Extract("notes.docx", Docx(Document(Para("Hello") + Para("World"))));

        Assert.Equal("Hello\nWorld", text);
    }

    [Fact]
    public void Extract_JoinsTheRunsOfAParagraph()
    {
        var body = "<w:p><w:r><w:t>Hel</w:t></w:r><w:r><w:t>lo</w:t></w:r></w:p>";

        Assert.Equal("Hello", _extractor.Extract("notes.docx", Docx(Document(body))));
    }

    [Fact]
    public void Extract_KeepsSpacesMarkedAsPreserved()
    {
        var body = "<w:p><w:r><w:t xml:space=\"preserve\">Hello </w:t></w:r><w:r><w:t>world</w:t></w:r></w:p>";

        Assert.Equal("Hello world", _extractor.Extract("notes.docx", Docx(Document(body))));
    }

    [Fact]
    public void Extract_ReadsTabsAndLineBreaks()
    {
        var body = "<w:p><w:r><w:t>a</w:t><w:tab/><w:t>b</w:t><w:br/><w:t>c</w:t></w:r></w:p>";

        Assert.Equal("a\tb\nc", _extractor.Extract("notes.docx", Docx(Document(body))));
    }

    [Fact]
    public void Extract_ReadsTableCellsAsLines()
    {
        var body = "<w:tbl><w:tr>" +
                   $"<w:tc>{Para("A")}</w:tc><w:tc>{Para("B")}</w:tc><w:tc>{Para("C")}</w:tc>" +
                   "</w:tr></w:tbl>";

        Assert.Equal("A\nB\nC", _extractor.Extract("notes.docx", Docx(Document(body))));
    }

    [Fact]
    public void Extract_KeepsSpecialCharactersAndEntities()
    {
        var text = _extractor.Extract("notes.docx", Docx(Document(Para("Educação &amp; saúde"))));

        Assert.Equal("Educação & saúde", text);
    }

    [Fact]
    public void Extract_IgnoresTrackedDeletionsAndFieldCodes()
    {
        var body = "<w:p><w:r><w:t>kept</w:t></w:r>" +
                   "<w:del><w:r><w:delText>deleted</w:delText></w:r></w:del>" +
                   "<w:r><w:instrText>PAGE</w:instrText></w:r></w:p>";

        Assert.Equal("kept", _extractor.Extract("notes.docx", Docx(Document(body))));
    }

    [Fact]
    public void Extract_TrimsLeadingAndTrailingWhitespace()
    {
        var body = "<w:p/>" + Para("Paris is the capital.") + "<w:p/>";

        Assert.Equal("Paris is the capital.", _extractor.Extract("notes.docx", Docx(Document(body))));
    }

    [Fact]
    public void Extract_AcceptsAnUpperCaseExtension()
    {
        Assert.Equal("Hi", _extractor.Extract("NOTES.DOCX", Docx(Document(Para("Hi")))));
    }

    // ---- rejections ----

    [Fact]
    public void Extract_Throws_SourceFileUnreadable_WhenTheFileIsNotAZip()
    {
        Assert.Equal(ValidationErrorCode.SourceFileUnreadable,
            CodeOf("fake.docx", Encoding.UTF8.GetBytes("just some text")));
    }

    [Fact]
    public void Extract_Throws_SourceFileUnreadable_WhenTheZipIsTruncated()
    {
        var bytes = Docx(Document(Para("Hello")));

        Assert.Equal(ValidationErrorCode.SourceFileUnreadable,
            CodeOf("cut.docx", bytes[..(bytes.Length / 2)]));
    }

    [Fact]
    public void Extract_Throws_SourceFileUnreadable_WhenThereIsNoWordDocumentEntry()
    {
        // What a pptx renamed to .docx looks like.
        var bytes = Zip(zip => Write(zip, "ppt/presentation.xml", "<p/>"));

        Assert.Equal(ValidationErrorCode.SourceFileUnreadable, CodeOf("deck.docx", bytes));
    }

    [Fact]
    public void Extract_Throws_SourceFileUnreadable_WhenTheXmlIsMalformed()
    {
        Assert.Equal(ValidationErrorCode.SourceFileUnreadable,
            CodeOf("bad.docx", Docx("<w:document xmlns:w=\"" + W + "\"><w:body>")));
    }

    [Fact]
    public void Extract_Throws_SourceFileUnreadable_WhenThereIsNoText()
    {
        Assert.Equal(ValidationErrorCode.SourceFileUnreadable,
            CodeOf("empty.docx", Docx(Document("<w:p/><w:p/>"))));
    }

    [Fact]
    public void Extract_Throws_SourceFileUnreadable_WhenTheXmlHasADtd()
    {
        var xml = "<?xml version=\"1.0\"?><!DOCTYPE d [<!ENTITY x \"boom\">]>" +
                  $"<w:document xmlns:w=\"{W}\"><w:body>{Para("&x;")}</w:body></w:document>";

        Assert.Equal(ValidationErrorCode.SourceFileUnreadable, CodeOf("xxe.docx", Docx(xml)));
    }

    [Fact]
    public void Extract_Throws_SourceFileTooLarge_WhenTheFileIsOverTenMegabytes()
    {
        var content = new byte[FormSourceContent.MaxFileBytes + 1];

        Assert.Equal(ValidationErrorCode.SourceFileTooLarge, CodeOf("big.docx", content));
    }

    // ---- zip protections ----

    [Fact]
    public void Extract_Accepts_ExactlyTheMaximumNumberOfEntries()
    {
        // Two entries are the content types and the document.
        var bytes = Docx(Document(Para("Hi")), extraEntries: ZipLimits.MaxEntries - 2);

        Assert.Equal("Hi", _extractor.Extract("full.docx", bytes));
    }

    [Fact]
    public void Extract_Throws_SourceFileUnreadable_WhenThereAreTooManyEntries()
    {
        var bytes = Docx(Document(Para("Hi")), extraEntries: ZipLimits.MaxEntries - 1);

        Assert.Equal(ValidationErrorCode.SourceFileUnreadable, CodeOf("many.docx", bytes));
    }

    [Fact]
    public void Extract_Throws_SourceFileUnreadable_WhenTheDecompressedXmlIsOverTheLimit()
    {
        // About 50 KB zipped, 50 MB + 1 once decompressed. The file passes the 10 MB check.
        var bytes = Zip(zip =>
        {
            using var stream = zip.CreateEntry("word/document.xml").Open();
            var head = Encoding.UTF8.GetBytes($"<w:document xmlns:w=\"{W}\"><w:body><w:p><w:r><w:t>");
            stream.Write(head);

            var chunk = new byte[1024 * 1024];
            Array.Fill(chunk, (byte)'a');
            var remaining = ZipLimits.MaxDecompressedBytes + 1 - head.Length;
            while (remaining > 0)
            {
                var size = (int)Math.Min(chunk.Length, remaining);
                stream.Write(chunk, 0, size);
                remaining -= size;
            }
        });

        Assert.True(bytes.Length < FormSourceContent.MaxFileBytes);
        Assert.Equal(ValidationErrorCode.SourceFileUnreadable, CodeOf("bomb.docx", bytes));
    }

    [Fact]
    public void Extract_DoesNotLeakTheFileNameInTheMessage()
    {
        var ex = Assert.Throws<ValidationException>(() =>
            _extractor.Extract("secret-report.docx", Encoding.UTF8.GetBytes("not a zip")));

        Assert.DoesNotContain("secret-report", ex.Message);
    }
}
```

The boundary tests rely on `Docx(...)` always writing two entries (`[Content_Types].xml` and `word/document.xml`). If you change the helper, change those two numbers with it.

## 3. Docs

- **`CLAUDE.md`**: update the sentence added in slice 10: the extractor now handles `.txt` and `.docx`, still **not called by the generate flow**. Add one line to the Architecture/AI area: zip files are protected by `ZipLimits` (1000 entries, 50 MB counted while streaming) and XML is read with DTD off.
- **`docs/known-gaps.md`**: under *Generation from PDF, Word, image or URL*, note that `.docx` extraction exists (body text only: no headers, footers, footnotes or text boxes) but the endpoint doesn't accept a file yet.
- **`CONTEXT.md`**: no new term. Check that **Extracted text** still reads correctly.
- **`docs/plans/phase-37.md`**: in the slice table, mark 11 as done when it is.
- No ADR.

## 4. Verify

```bash
dotnet build FormAI.sln
dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~Files"
dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj
```

Optional manual check: create a real `.docx` in Word with a heading, a paragraph with accents and a small table, then call the extractor from a throwaway test or `dotnet fsi` with `File.ReadAllBytes`. Confirm the text comes out in order. Word writes `xml:space="preserve"` and splits runs on formatting changes, which the unit tests only imitate.

## Done when

- `.docx` extraction follows the rules above, and `.pptx` / `.pdf` still answer `SourceFileUnsupported`.
- All the tests above pass, including the 50 MB bomb and the 1000-entry boundary.
- `GenerateFormHandler`, the controller and the frontend are unchanged.
- `CLAUDE.md` and `docs/known-gaps.md` describe the docx extractor as built and not yet wired.

## Suggested commits

1. `feat(infrastructure): add zip limits and a size-limited read stream`
2. `feat(infrastructure): extract text from .docx files`
3. `test: cover the docx extractor and its zip protections`
4. `docs: record docx extraction in CLAUDE.md and known-gaps`
