# Slice 12: Source text extractor, `.pptx`

Covers SC-3 (pptx) of [`phase-37.md`](./phase-37.md). It adds `.pptx` to `SourceTextExtractor` next to `.txt` and `.docx` ([slice 11](./phase-37-11-2.md)), in the same style. **Nothing calls it yet**: the controller and `GenerateFormHandler` don't change until slice 13.

## Decisions

| Topic | Decision |
|---|---|
| Dependencies | None new. |
| What a pptx is | A zip. Each slide is `ppt/slides/slideN.xml`, each speaker note is `ppt/notesSlides/notesSlideN.xml`. |
| Is it really a pptx | No magic-byte check, as with docx. A non-zip throws, and a zip with **no slide part** (a docx, an xlsx, a plain zip renamed `.pptx`) is rejected. Both answer `SourceFileUnreadable`. |
| Slide order | By the number in the file name (`slide2` before `slide10`, numerically). **Known limit:** PowerPoint keeps the original file name when slides are reordered, so a reordered deck can come out in its original order. Fixing it means reading `presentation.xml` and its relationships; not worth it for question generation. |
| Notes | Appended after all the slides under a `Speaker notes:` label, not placed next to their slide (that mapping also needs the relationships files). |
| Text | `a:t` of runs (`a:r`), one line per paragraph (`a:p`), `a:br` as a newline. This covers titles, text boxes, groups and tables, which all use `a:p`. Slide-number, date and footer **fields** are `a:fld`, not runs, so they are skipped. Empty paragraphs are dropped. |
| Ignored | Images, charts, SmartArt, embedded objects, masters and layouts (their file names don't match `slideN.xml`), comments. Hidden slides are included. |
| Entry count | More than 1,000 entries is rejected, as in docx. |
| Size guard | `MaxCharactersInDocument = 1,000,000` **per XML part** (a slide is normally tens of KB), DTD off. **This is per part, not a total:** worst case is 1,000 entries times 1 M characters of parsing in one request. Accepted: the file is capped at 10 MB, the route is rate limited to 10 per hour per user, and each part is freed before the next is read. |
| Cleaning | Trim only, reject an empty result, as in slice 10. |
| Errors | `ValidationException` with the slice 10 codes. Fixed messages, no file name or content. |
| Duplication | The reader settings and entry limit repeat what `DocxTextExtractor` has. Left as is on purpose; pull them into one helper only if a third format appears. |

## 1. Code

### 1.1 New `src/FormAI.Infrastructure/Files/PptxTextExtractor.cs`

```csharp
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace FormAI.Infrastructure.Files;

internal static class PptxTextExtractor
{
    private const int MaxEntries = 1000;

    // Per XML part, counted by the parser while it reads.
    private const long MaxXmlCharacters = 1_000_000;

    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    // The number is capped so a long digit string in a name can't overflow int.Parse.
    private static readonly Regex SlidePart = new(@"^ppt/slides/slide(\d{1,6})\.xml$");
    private static readonly Regex NotesPart = new(@"^ppt/notesSlides/notesSlide(\d{1,6})\.xml$");

    public static string Extract(byte[] content)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);

            if (archive.Entries.Count > MaxEntries)
                throw SourceTextExtractor.Unreadable();

            var slides = PartTexts(archive, SlidePart);
            var notes = PartTexts(archive, NotesPart);

            // Also rejects a docx, an xlsx or a plain zip renamed to .pptx.
            if (slides.Count == 0)
                throw SourceTextExtractor.Unreadable();

            var text = string.Join("\n\n", slides);
            if (notes.Count > 0)
                text += "\n\nSpeaker notes:\n" + string.Join("\n\n", notes);

            return text.Trim();
        }
        // Not a zip, a corrupt zip, or XML that is malformed, has a DTD or is over the limit.
        catch (Exception ex) when (ex is InvalidDataException or XmlException)
        {
            throw SourceTextExtractor.Unreadable();
        }
    }

    // The text of every part whose name matches, in numeric order, skipping empty ones.
    private static List<string> PartTexts(ZipArchive archive, Regex part) =>
        archive.Entries
            .Select(entry => (Entry: entry, Match: part.Match(entry.FullName)))
            .Where(x => x.Match.Success)
            .OrderBy(x => int.Parse(x.Match.Groups[1].Value))
            .Select(x => PartText(x.Entry))
            .Where(text => text.Length > 0)
            .ToList();

    private static string PartText(ZipArchiveEntry entry)
    {
        using var xml = XmlReader.Create(entry.Open(), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxXmlCharacters
        });

        var paragraphs = XDocument.Load(xml).Descendants(A + "p")
            .Select(ParagraphText)
            .Where(text => text.Length > 0);

        return string.Join("\n", paragraphs).Trim();
    }

    // Runs and line breaks only. Fields (slide number, date) are a:fld and are skipped.
    private static string ParagraphText(XElement paragraph) =>
        string.Concat(paragraph.Elements().Select(e => e.Name.LocalName switch
        {
            "r" => e.Element(A + "t")?.Value,
            "br" => "\n",
            _ => null
        }));
}
```

### 1.2 `src/FormAI.Infrastructure/Files/SourceTextExtractor.cs`

In `Extract`, accept `.pptx` and dispatch with a `switch`. Replace the extension check and the last line:

```csharp
        if (extension is not (".txt" or ".docx" or ".pptx"))
        {
            throw new Application.Common.Exceptions.ValidationException(ValidationErrorCode.SourceFileUnsupported,
                "This file type is not supported. Use a .pdf, .docx, .pptx or .txt file.");
        }
```

```csharp
        return extension switch
        {
            ".txt" => ExtractTxt(content),
            ".docx" => DocxTextExtractor.Extract(content),
            _ => PptxTextExtractor.Extract(content)
        };
```

Nothing else changes in the file.

## 2. Tests

### 2.1 Fix an existing test

In `tests/FormAI.UnitTests/Files/SourceTextExtractorTests.cs`, `notes.pptx` is supported now. Replace that `InlineData` with `notes.doc`:

```csharp
    [Theory]
    [InlineData("notes.doc")]
    [InlineData("notes.pdf")]
    [InlineData("notes")]
    public void Extract_Throws_SourceFileUnsupported_ForAnyOtherExtension(string fileName)
```

### 2.2 New `tests/FormAI.UnitTests/Files/PptxTextExtractorTests.cs`

```csharp
using System.IO.Compression;
using System.Text;
using FormAI.Application.Common.Exceptions;
using FormAI.Domain.Entities;
using FormAI.Infrastructure.Files;

namespace FormAI.UnitTests.Files;

public class PptxTextExtractorTests
{
    private const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private const string P = "http://schemas.openxmlformats.org/presentationml/2006/main";

    private readonly SourceTextExtractor _extractor = new();

    // ---- helpers ----

    private static string Slide(string paragraphs) =>
        $"<p:sld xmlns:p=\"{P}\" xmlns:a=\"{A}\"><p:cSld><p:spTree><p:sp><p:txBody>{paragraphs}</p:txBody></p:sp></p:spTree></p:cSld></p:sld>";

    private static string Para(string text) => $"<a:p><a:r><a:t>{text}</a:t></a:r></a:p>";

    private static byte[] Zip(params (string Name, string Content)[] files) =>
        Zip(zip =>
        {
            foreach (var (name, content) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        });

    private static byte[] Zip(Action<ZipArchive> fill)
    {
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            fill(zip);
        return buffer.ToArray();
    }

    private ValidationErrorCode? CodeOf(string fileName, byte[] content) =>
        Assert.Throws<ValidationException>(() => _extractor.Extract(fileName, content)).Code;

    // ---- extraction ----

    [Fact]
    public void Extract_ReadsTheSlidesInNumericOrder_NotAlphabeticalOrder()
    {
        // Entries are written out of order, and slide10 sorts before slide2 as text.
        var bytes = Zip(
            ("ppt/slides/slide10.xml", Slide(Para("third"))),
            ("ppt/slides/slide2.xml", Slide(Para("second"))),
            ("ppt/slides/slide1.xml", Slide(Para("first"))));

        Assert.Equal("first\n\nsecond\n\nthird", _extractor.Extract("deck.pptx", bytes));
    }

    [Fact]
    public void Extract_ReturnsOneLinePerParagraph_JoiningRunsAndReadingLineBreaks()
    {
        var body = "<a:p><a:r><a:t>Hel</a:t></a:r><a:r><a:t>lo</a:t></a:r><a:br/><a:r><a:t>there</a:t></a:r></a:p>" +
                   Para("Educação &amp; saúde") + "<a:p/>";

        Assert.Equal("Hello\nthere\nEducação & saúde",
            _extractor.Extract("DECK.PPTX", Zip(("ppt/slides/slide1.xml", Slide(body)))));
    }

    [Fact]
    public void Extract_ReadsTableCells()
    {
        var table = $"<a:tbl><a:tr><a:tc><a:txBody>{Para("A")}</a:txBody></a:tc>" +
                    $"<a:tc><a:txBody>{Para("B")}</a:txBody></a:tc></a:tr></a:tbl>";

        Assert.Equal("A\nB",
            _extractor.Extract("deck.pptx", Zip(("ppt/slides/slide1.xml", Slide(table)))));
    }

    [Fact]
    public void Extract_SkipsFieldsLikeTheSlideNumber()
    {
        var body = Para("Title") + "<a:p><a:fld type=\"slidenum\"><a:t>7</a:t></a:fld></a:p>";

        Assert.Equal("Title",
            _extractor.Extract("deck.pptx", Zip(("ppt/slides/slide1.xml", Slide(body)))));
    }

    [Fact]
    public void Extract_AppendsTheSpeakerNotesWithALabel()
    {
        var bytes = Zip(
            ("ppt/slides/slide1.xml", Slide(Para("Slide text"))),
            ("ppt/notesSlides/notesSlide1.xml", Slide(Para("Say this out loud"))));

        Assert.Equal("Slide text\n\nSpeaker notes:\nSay this out loud", _extractor.Extract("deck.pptx", bytes));
    }

    [Fact]
    public void Extract_IgnoresLayoutsAndMasters()
    {
        var bytes = Zip(
            ("ppt/slides/slide1.xml", Slide(Para("Real"))),
            ("ppt/slideLayouts/slideLayout1.xml", Slide(Para("LAYOUT"))),
            ("ppt/slideMasters/slideMaster1.xml", Slide(Para("MASTER"))));

        Assert.Equal("Real", _extractor.Extract("deck.pptx", bytes));
    }

    // ---- rejections ----

    [Fact]
    public void Extract_Throws_SourceFileUnreadable_WhenTheFileIsNotAZip()
    {
        Assert.Equal(ValidationErrorCode.SourceFileUnreadable,
            CodeOf("fake.pptx", Encoding.UTF8.GetBytes("just some text")));
    }

    [Fact]
    public void Extract_Throws_SourceFileUnreadable_WhenThereIsNoSlide()
    {
        // What a docx renamed to .pptx looks like.
        Assert.Equal(ValidationErrorCode.SourceFileUnreadable,
            CodeOf("doc.pptx", Zip(("word/document.xml", "<w/>"))));
    }

    [Theory]
    [InlineData("<p:sld")]                                                         // malformed
    [InlineData("<p:sld xmlns:p=\"" + P + "\" xmlns:a=\"" + A + "\"><a:p/></p:sld>")] // no text
    public void Extract_Throws_SourceFileUnreadable_WhenTheXmlIsBadOrHasNoText(string xml)
    {
        Assert.Equal(ValidationErrorCode.SourceFileUnreadable,
            CodeOf("bad.pptx", Zip(("ppt/slides/slide1.xml", xml))));
    }

    [Fact]
    public void Extract_Throws_SourceFileUnreadable_WhenTheXmlHasADtd()
    {
        var xml = "<?xml version=\"1.0\"?><!DOCTYPE d [<!ENTITY x \"boom\">]>" + Slide(Para("&x;"));

        Assert.Equal(ValidationErrorCode.SourceFileUnreadable,
            CodeOf("xxe.pptx", Zip(("ppt/slides/slide1.xml", xml))));
    }

    [Fact]
    public void Extract_Throws_SourceFileUnreadable_WhenThereAreTooManyEntries()
    {
        var bytes = Zip(zip =>
        {
            zip.CreateEntry("ppt/slides/slide1.xml");
            for (var i = 0; i < 1000; i++)
                zip.CreateEntry($"ppt/media/image{i}.png");
        });

        Assert.Equal(ValidationErrorCode.SourceFileUnreadable, CodeOf("many.pptx", bytes));
    }

    [Fact]
    public void Extract_Throws_SourceFileUnreadable_WhenASlideExpandsPastTheLimit()
    {
        // A few KB zipped, about 2 million characters once decompressed (the limit is 1 million).
        var bytes = Zip(zip =>
        {
            using var stream = zip.CreateEntry("ppt/slides/slide1.xml").Open();
            stream.Write(Encoding.UTF8.GetBytes($"<p:sld xmlns:p=\"{P}\" xmlns:a=\"{A}\"><a:p><a:r><a:t>"));

            var chunk = new byte[1024 * 1024];
            Array.Fill(chunk, (byte)'a');
            stream.Write(chunk);
            stream.Write(chunk);
        });

        Assert.True(bytes.Length < FormSourceContent.MaxFileBytes);
        Assert.Equal(ValidationErrorCode.SourceFileUnreadable, CodeOf("bomb.pptx", bytes));
    }

    [Fact]
    public void Extract_Throws_SourceFileTooLarge_WhenTheFileIsOverTenMegabytes()
    {
        Assert.Equal(ValidationErrorCode.SourceFileTooLarge,
            CodeOf("big.pptx", new byte[FormSourceContent.MaxFileBytes + 1]));
    }
}
```

The "too many entries" test writes 1,001 entries (one slide plus 1,000 others), one over the limit.

## 3. Docs

- **`CLAUDE.md`**: the extractor now handles `.txt`, `.docx` and `.pptx` and is still **not called by the generate flow**. Add that pptx is read part by part (1,000 entries, each XML part capped at 1 M characters, DTD off).
- **`docs/known-gaps.md`**: under *Generation from PDF, Word, image or URL*, note that `.pptx` extraction exists but the endpoint doesn't accept a file yet, with its limits: text of shapes and tables only; slides in file-number order, so a reordered deck may come out in its original order; speaker notes appended at the end, not next to their slide; hidden slides included; the size guard is per XML part, not a total.
- **`docs/plans/phase-37.md`**: mark slice 12 done.
- `CONTEXT.md` and ADRs: nothing.

## 4. Verify

```bash
dotnet build FormAI.sln
dotnet test tests/FormAI.UnitTests/FormAI.UnitTests.csproj --filter "FullyQualifiedName~Files"
```

Also feed it one real `.pptx` saved from PowerPoint: a title slide, a slide with a table, speaker notes on one slide, and a slide dragged to a new position. Check that the text comes out, the slide number and footer text don't, and see the reordering limit for yourself.

## Done when

- `.pptx` extraction works as above; `.pdf` still answers `SourceFileUnsupported`.
- The tests above pass.
- The controller, `GenerateFormHandler` and the frontend are unchanged.

## Suggested commits

1. `feat(infrastructure): extract text from .pptx files`
2. `test: cover the pptx extractor`
3. `docs: record pptx extraction in CLAUDE.md and known-gaps`
