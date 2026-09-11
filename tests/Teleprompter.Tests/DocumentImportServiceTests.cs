using System.IO.Compression;
using System.Text;
using Teleprompter.Services;

namespace Teleprompter.Tests;

public sealed class DocumentImportServiceTests
{
    static DocumentImportServiceTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void SplitLines_NormalizesAndDropsBlankLines()
    {
        var result = DocumentImportService.SplitLines("  第一行  \r\n\r第二行\n \t \n第三行 😀  ");
        Assert.Equal(["第一行", "第二行", "第三行 😀"], result);
    }

    [Fact]
    public void DecodeText_ReadsUtf8Utf16AndGb18030()
    {
        const string expected = "中文 😀";
        var utf8Bom = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(expected)).ToArray();
        var utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(expected)).ToArray();
        var gb = Encoding.GetEncoding("GB18030").GetBytes(expected);

        Assert.Equal(expected, DocumentImportService.DecodeText(utf8Bom));
        Assert.Equal(expected, DocumentImportService.DecodeText(utf16));
        Assert.Equal(expected, DocumentImportService.DecodeText(gb));
    }

    [Fact]
    public async Task ImportAsync_ReadsMinimalDocxParagraphsAndTableText()
    {
        var path = Path.Combine(Path.GetTempPath(), $"teleprompter-test-{Guid.NewGuid():N}.docx");
        try
        {
            CreateMinimalDocx(path);
            var result = await new DocumentImportService().ImportAsync(path);
            Assert.Contains(result.Lines, line => line.Contains("第一段", StringComparison.Ordinal));
            Assert.Contains(result.Lines, line => line.Contains("表格内容", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Import_InvalidDocReportsReadableError()
    {
        var path = Path.Combine(Path.GetTempPath(), $"teleprompter-test-{Guid.NewGuid():N}.doc");
        try
        {
            File.WriteAllText(path, "not a doc");
            var error = Assert.Throws<InvalidDataException>(() => new DocumentImportService().Import(path));
            Assert.Contains("无法读取 DOC", error.Message);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void CreateMinimalDocx(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            </Types>
            """);
        WriteEntry(archive, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);
        WriteEntry(archive, "word/document.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p><w:r><w:t>第一段</w:t></w:r></w:p>
                <w:tbl><w:tr><w:tc><w:p><w:r><w:t>表格内容</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
                <w:sectPr/>
              </w:body>
            </w:document>
            """);
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}

