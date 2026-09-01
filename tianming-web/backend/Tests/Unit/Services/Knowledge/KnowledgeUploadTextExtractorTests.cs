using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Http;
using TM.Web.NovelAgentWeb.Services.Knowledge;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Tests.Unit.Services.Knowledge;

public sealed class KnowledgeUploadTextExtractorTests
{
    private readonly KnowledgeUploadTextExtractor _extractor = new();

    [Fact]
    public async Task ExtractAsync_Utf8BomText_ReturnsNormalizedText()
    {
        var content = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("第一行\r\n第二行")).ToArray();

        var result = await _extractor.ExtractAsync(CreateFormFile(content, "knowledge.txt", "text/plain"));

        Assert.Equal("txt", result.Format);
        Assert.Equal("第一行\n第二行", result.Text);
        Assert.Equal(content, result.OriginalBytes);
    }

    [Fact]
    public async Task ExtractAsync_InvalidUtf8Text_IsRejected()
    {
        var file = CreateFormFile([0xC3, 0x28], "knowledge.txt", "text/plain");

        var exception = await Assert.ThrowsAsync<KnowledgeUploadValidationException>(
            () => _extractor.ExtractAsync(file));

        Assert.Contains("文件格式无效或已损坏", exception.Message);
    }

    [Fact]
    public async Task ExtractAsync_Pdf_ReturnsPageText()
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        page.AddText("Novel knowledge from PDF", 12, new PdfPoint(72, 720), font);
        var file = CreateFormFile(builder.Build(), "knowledge.pdf", "application/pdf");

        var result = await _extractor.ExtractAsync(file);

        Assert.Equal("pdf", result.Format);
        Assert.Contains("Novel knowledge from PDF", result.Text);
    }

    [Fact]
    public async Task ExtractAsync_Epub_FollowsSpineOrderAndExtractsXhtml()
    {
        var file = CreateFormFile(CreateEpub(), "knowledge.epub", "application/epub+zip");

        var result = await _extractor.ExtractAsync(file);

        Assert.Equal("epub", result.Format);
        Assert.Contains("第一章 灯城封锁", result.Text);
        Assert.Contains("第二章 旧城区档案", result.Text);
        Assert.True(result.Text.IndexOf("第一章", StringComparison.Ordinal) < result.Text.IndexOf("第二章", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExtractAsync_MismatchedMimeType_IsRejected()
    {
        var file = CreateFormFile(Encoding.UTF8.GetBytes("not a pdf"), "knowledge.pdf", "text/plain");

        var exception = await Assert.ThrowsAsync<KnowledgeUploadValidationException>(
            () => _extractor.ExtractAsync(file));

        Assert.Contains("MIME 类型", exception.Message);
    }

    [Fact]
    public async Task ExtractAsync_OversizedUpload_IsRejectedAsTooLarge()
    {
        var file = new FormFile(
            new MemoryStream([1]),
            0,
            KnowledgeUploadTextExtractor.MaxUploadBytes + 1,
            "file",
            "knowledge.txt")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };

        var exception = await Assert.ThrowsAsync<KnowledgeUploadValidationException>(
            () => _extractor.ExtractAsync(file));

        Assert.True(exception.IsTooLarge);
    }

    private static IFormFile CreateFormFile(byte[] content, string fileName, string contentType)
    {
        return new FormFile(new MemoryStream(content), 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private static byte[] CreateEpub()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "META-INF/container.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <container xmlns="urn:oasis:names:tc:opendocument:xmlns:container" version="1.0">
                  <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml" /></rootfiles>
                </container>
                """);
            WriteEntry(archive, "OEBPS/content.opf", """
                <?xml version="1.0" encoding="UTF-8"?>
                <package xmlns="http://www.idpf.org/2007/opf" version="3.0">
                  <manifest>
                    <item id="chapter-1" href="chapter-1.xhtml" media-type="application/xhtml+xml" />
                    <item id="chapter-2" href="chapter-2.xhtml" media-type="application/xhtml+xml" />
                  </manifest>
                  <spine><itemref idref="chapter-1" /><itemref idref="chapter-2" /></spine>
                </package>
                """);
            WriteEntry(archive, "OEBPS/chapter-1.xhtml", "<html xmlns=\"http://www.w3.org/1999/xhtml\"><body><h1>第一章</h1><p>灯城封锁</p></body></html>");
            WriteEntry(archive, "OEBPS/chapter-2.xhtml", "<html xmlns=\"http://www.w3.org/1999/xhtml\"><body><h1>第二章</h1><p>旧城区档案</p></body></html>");
        }
        return output.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
