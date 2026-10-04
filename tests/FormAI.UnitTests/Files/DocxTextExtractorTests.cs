using System.IO.Compression;
using System.Text;
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Forms.Validation;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Infrastructure.Files;
using static FormAI.UnitTests.TestsHelper.EntityBuilders;

namespace FormAI.UnitTests.Files;

public class DocxTextExtractorTests
{
    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private readonly SourceTextExtractor _extractor = new();


    private static string Document(string body) =>
        $"<w:document xmlns:w=\"{W}\"><w:body>{body}</w:body></w:document>";

    private static string Paragraph(string text) => $"<w:p><w:r><w:t>{text}</w:t></w:r></w:p>";

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

    private static byte[] Docx(string documentXml) => Zip(("word/document.xml", documentXml));

    private ValidationErrorCode? CodeOf(string fileName, byte[] content) =>
        Assert.Throws<ValidationException>(() => _extractor.Extract(fileName, content)).Code;

    [Fact]
    public void Extract_ReturnsOneLinePerParagraph_JoiningTheRuns()
    {
        var body = "<w:p><w:r><w:t>Hel</w:t></w:r><w:r><w:t>lo</w:t></w:r></w:p>" + Paragraph("World");

        Assert.Equal("Hello\nWorld", _extractor.Extract("notes.docx", Docx(Document(body))));
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
        var body = $"<w:tbl><w:tr><w:tc>{Paragraph("A")}</w:tc><w:tc>{Paragraph("B")}</w:tc></w:tr></w:tbl>";

        Assert.Equal("A\nB", _extractor.Extract("notes.docx", Docx(Document(body))));
    }

    [Fact]
    public void Extract_KeepsSpecialCharactersAndTrims()
    {
        var body = "<w:p/>" + Paragraph("Educação &amp; saúde") + "<w:p/>";

        Assert.Equal("Educação & saúde", _extractor.Extract("NOTES.DOCX", Docx(Document(body))));
    }

    [Fact]
    public void Extract_Throws_SourceFileUnreadable_WhenTheFileIsNotAZip()
    {
        Assert.Equal(ValidationErrorCode.SourceFileUnreadable,
            CodeOf("fake.docx", Encoding.UTF8.GetBytes("just some text")));
    }

    [Theory]
    [InlineData("<w:document")] // malformed
    [InlineData("<w:document xmlns:w=\"" + W + "\"><w:body><w:p/><w:p/></w:body></w:document>")] // no text
    public void Extract_Throws_SourceFileUnreadable_WhenTheXmlIsBadOrHasNoText(string xml)
    {
        Assert.Equal(ValidationErrorCode.SourceFileUnreadable, CodeOf("bad.docx", Docx(xml)));
    }

    [Fact]
    public void Extract_Throws_SourceFileUnreadable_WhenTheXmlHasADtd()
    {
        var xml = "<?xml version=\"1.0\"?><!DOCTYPE d [<!ENTITY x \"boom\">]>" + Document(Paragraph("&x;"));

        Assert.Equal(ValidationErrorCode.SourceFileUnreadable, CodeOf("xxe.docx", Docx(xml)));
    }

    [Fact]
    public void Extract_Throws_SourceFileUnreadable_WhenThereAreTooManyEntries()
    {
        var bytes = Zip(zip =>
        {
            zip.CreateEntry("word/document.xml");
            for (var i = 0; i < 1000; i++)
                zip.CreateEntry($"word/media/image{i}.png");
        });

        Assert.Equal(ValidationErrorCode.SourceFileUnreadable, CodeOf("many.docx", bytes));
    }

    [Fact]
    public void Extract_Throws_SourceFileUnreadable_WhenTheXmlExpandsPastTheLimit()
    {
        // A few tens of KB zipped, over 10 million characters once decompressed.
        var bytes = Zip(zip =>
        {
            using var stream = zip.CreateEntry("word/document.xml").Open();
            var head = Encoding.UTF8.GetBytes($"<w:document xmlns:w=\"{W}\"><w:body><w:p><w:r><w:t>");
            stream.Write(head);

            var chunk = new byte[1024 * 1024];
            Array.Fill(chunk, (byte)'a');
            for (var i = 0; i < 11; i++)
                stream.Write(chunk);
        });

        Assert.True(bytes.Length < FormSourceContent.MaxFileBytes);
        Assert.Equal(ValidationErrorCode.SourceFileUnreadable, CodeOf("bomb.docx", bytes));
    }

    [Fact]
    public void Extract_Throws_SourceFileTooLarge_WhenTheFileIsOverTenMegabytes()
    {
        Assert.Equal(ValidationErrorCode.SourceFileTooLarge,
            CodeOf("big.docx", new byte[FormSourceContent.MaxFileBytes + 1]));
    }

}
