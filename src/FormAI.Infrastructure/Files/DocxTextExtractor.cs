using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace FormAI.Infrastructure.Files;

public static class DocxTextExtractor
{
    private const int MaxEntries = 1000;

    private const long MaxXmlCharacters = 10_000_000;

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static string Extract(byte[] content)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);

            if (archive.Entries.Count > MaxEntries)
                throw SourceTextExtractor.Unreadable();

            var entry = archive.GetEntry("word/document.xml") ?? throw SourceTextExtractor.Unreadable();

            using var xml = XmlReader.Create(entry.Open(), new
            XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxXmlCharacters
            });

            var paragraphs = XDocument.Load(xml).Descendants(W + "p").
            Select(ParagraphText);

            var text = string.Join("\n", paragraphs).Trim();

            return text.Length > 0 ? text : throw SourceTextExtractor.Unreadable();

        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException)
        {
            throw SourceTextExtractor.Unreadable();
        }
    }

    private static string ParagraphText(XElement paragraph) =>
       string.Concat(paragraph.Descendants().Select(e => e.Name.LocalName switch
       {
           "t" => e.Value,
           "tab" => "\t",
           "br" => "\n",
           _ => ""
       }));
}
