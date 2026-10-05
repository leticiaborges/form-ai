using System.Text;
using FormAI.Application.Common.Exceptions;
using FormAI.Application.Forms.Validation;
using FormAI.Application.Interfaces;
using FormAI.Domain.Entities;
using FormAI.Infrastructure.Files;
using static FormAI.UnitTests.TestsHelper.EntityBuilders;

namespace FormAI.UnitTests.Files;

public class SourceTextExtractorTests
{
    private readonly SourceTextExtractor _extractor = new();

    [Fact]
    public void Extract_ReturnsTheText_WhenTheFileIsPlainUtf8()
    {
        var text = _extractor.Extract("notes.txt", Utf8("Teste arquivo com acentos: João, Marília."));

        Assert.Equal("Teste arquivo com acentos: João, Marília.", text);
    }

    [Theory]
    [InlineData("notes.pdf")]
    [InlineData("notes")]
    public void Extract_Throws_SourceFileUnsupported_ForAnyOtherExtension(string fileName)
    {
        Assert.Equal(ValidationErrorCode.SourceFileUnsupported, CodeOf(fileName, Utf8("text")));
    }

    private ValidationErrorCode? CodeOf(string fileName, byte[] content)
    {
        var ex = Assert.Throws<ValidationException>(() => _extractor.Extract(fileName, content));
        return ex.Code;
    }

    [Fact]
    public void Extract_TrimsLeadingAndTrailingWhitespace()
    {
        var text = _extractor.Extract("notes.txt", Utf8("\n\n  Paris is the capital.  \r\n"));

        Assert.Equal("Paris is the capital.", text);
    }

    [Fact]
    public void Extract_Throws_SourceFileTooLarge_WhenOverTheLimit()
    {
        var content = new byte[FormSourceContent.MaxFileBytes + 1];
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
    public void Extract_FallsBackToWindows1252_WhenTheBytesAreNotValidUtf8()
    {
        // 0xE9 alone is "é" in Windows-1252 and invalid in UTF-8.
        Assert.Equal("café", _extractor.Extract("notes.txt", [0x63, 0x61, 0x66, 0xE9]));
        // 0x93 and 0x94 are the curly quotes in Windows-1252.
        Assert.Equal("“hi”", _extractor.Extract("notes.txt", [0x93, 0x68, 0x69, 0x94]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \r\n\t  ")]
    public void Extract_Throws_SourceFileUnreadable_WhenThereIsNoTextAfterTrimming(string content)
    {
        Assert.Equal(ValidationErrorCode.SourceFileUnreadable, CodeOf("empty.txt", Utf8(content)));
    }

    public static byte[] Utf8(string s)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(s);
        return bytes;
    }
}
