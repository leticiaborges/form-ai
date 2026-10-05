using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace FormAI.Infrastructure.Files;

internal static class PptxTextExtractor
{
    private const int MaxEntries = 1000;

    // Per XML part, counted by the parser while it reads.
    private const long MaxXmlCharacters = 1_000_000;

    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Rels = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MaxXmlCharacters
    };

    private sealed record Relationship(string Id, string Type, string Target);

    public static string Extract(byte[] content)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);

            if (archive.Entries.Count > MaxEntries)
                throw SourceTextExtractor.Unreadable();

            // Also rejects a docx, an xlsx or a plain zip renamed to .pptx.
            const string presentationPath = "ppt/presentation.xml";
            var presentation = Load(archive, presentationPath) ?? throw SourceTextExtractor.Unreadable();
            var targets = Relationships(archive, presentationPath).ToDictionary(r => r.Id, r => r.Target);

            // The slides in display order. Distinct: one slide listed many times is read once.
            var slidePaths = presentation.Descendants(P + "sldId")
                .Select(slide => (string?)slide.Attribute(R + "id"))
                .Select(id => id is not null && targets.TryGetValue(id, out var target)
                    ? Resolve(DirectoryOf(presentationPath), target)
                    : null)
                .OfType<string>()
                .Distinct();

            var blocks = slidePaths
                .Select(path => SlideBlock(archive, path))
                .Where(block => block.Length > 0)
                .ToList();

            return blocks.Count > 0 ? string.Join("\n\n", blocks) : throw SourceTextExtractor.Unreadable();
        }
        // Not a zip, a corrupt zip, or XML that is malformed, has a DTD or is over the limit.
        catch (Exception ex) when (ex is InvalidDataException or XmlException)
        {
            throw SourceTextExtractor.Unreadable();
        }
    }

    // The slide text, then its notes (if any) under a label.
    private static string SlideBlock(ZipArchive archive, string slidePath)
    {
        var notesRelationship = Relationships(archive, slidePath)
            .FirstOrDefault(r => r.Type.EndsWith("/notesSlide", StringComparison.Ordinal));

        var notes = notesRelationship is null
            ? ""
            : PartText(archive, Resolve(DirectoryOf(slidePath), notesRelationship.Target));

        var parts = new[] { PartText(archive, slidePath), notes.Length > 0 ? "Notes:\n" + notes : "" };
        return string.Join("\n\n", parts.Where(part => part.Length > 0));
    }

    private static string PartText(ZipArchive archive, string path)
    {
        var document = Load(archive, path);
        if (document is null)
            return "";

        var paragraphs = document.Descendants(A + "p")
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

    // The relationships of a part live in <dir>/_rels/<name>.rels.
    private static List<Relationship> Relationships(ZipArchive archive, string partPath)
    {
        var slash = partPath.LastIndexOf('/');
        var rels = Load(archive, $"{partPath[..slash]}/_rels/{partPath[(slash + 1)..]}.rels");
        if (rels is null)
            return [];

        return rels.Descendants(Rels + "Relationship")
            .Select(r => new Relationship(
                (string?)r.Attribute("Id") ?? "",
                (string?)r.Attribute("Type") ?? "",
                (string?)r.Attribute("Target") ?? ""))
            .ToList();
    }

    // Null when the part doesn't exist.
    private static XDocument? Load(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path);
        if (entry is null)
            return null;

        using var stream = entry.Open();
        using var xml = XmlReader.Create(stream, ReaderSettings);
        return XDocument.Load(xml);
    }

    private static string DirectoryOf(string path) => path[..path.LastIndexOf('/')];

    // Targets are relative to the part's folder ("../notesSlides/notesSlide1.xml"), or absolute
    // when they start with "/". Returns a zip entry name.
    private static string Resolve(string baseDirectory, string target)
    {
        var segments = new List<string>();
        if (!target.StartsWith('/'))
            segments.AddRange(baseDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries));

        foreach (var segment in target.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "..")
            {
                if (segments.Count > 0)
                    segments.RemoveAt(segments.Count - 1);
            }
            else if (segment != ".")
            {
                segments.Add(segment);
            }
        }

        return string.Join('/', segments);
    }
}
