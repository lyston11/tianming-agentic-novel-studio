using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using UglyToad.PdfPig;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public sealed record KnowledgeUploadExtraction(byte[] OriginalBytes, string Text, string Format);

public sealed class KnowledgeUploadValidationException : Exception
{
    public KnowledgeUploadValidationException(string message, bool isTooLarge = false, Exception? innerException = null)
        : base(message, innerException)
    {
        IsTooLarge = isTooLarge;
    }

    public bool IsTooLarge { get; }
}

public interface IKnowledgeUploadTextExtractor
{
    Task<KnowledgeUploadExtraction> ExtractAsync(IFormFile file, CancellationToken cancellationToken = default);
}

public sealed class KnowledgeUploadTextExtractor : IKnowledgeUploadTextExtractor
{
    internal const long MaxUploadBytes = 25L * 1024 * 1024;
    internal const int MaxExtractedCharacters = 5_000_000;
    internal const int MaxPdfPages = 2_000;
    internal const int MaxEpubEntries = 2_000;
    internal const long MaxEpubUncompressedBytes = 100L * 1024 * 1024;
    private const long MaxEpubMetadataBytes = 5L * 1024 * 1024;
    private const long MaxEpubSectionBytes = 10L * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly UnicodeEncoding StrictUtf16LittleEndian = new(false, true, true);
    private static readonly UnicodeEncoding StrictUtf16BigEndian = new(true, true, true);

    public async Task<KnowledgeUploadExtraction> ExtractAsync(
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Length <= 0)
            throw new KnowledgeUploadValidationException("知识文件为空。");
        if (file.Length > MaxUploadBytes)
            throw new KnowledgeUploadValidationException("知识文件不能超过 25 MB。", isTooLarge: true);

        var fileName = Path.GetFileName(file.FileName);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var format = ResolveFormat(extension, file.ContentType);
        var bytes = await ReadBytesAsync(file, cancellationToken).ConfigureAwait(false);

        string text;
        try
        {
            text = format switch
            {
                "txt" => ExtractPlainText(bytes),
                "pdf" => ExtractPdfText(bytes),
                "epub" => await ExtractEpubTextAsync(bytes, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unsupported knowledge upload format: {format}")
            };
        }
        catch (KnowledgeUploadValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or DecoderFallbackException)
        {
            throw new KnowledgeUploadValidationException($"无法解析 {fileName}：文件格式无效或已损坏。", innerException: exception);
        }

        text = NormalizeExtractedText(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            var message = format == "pdf"
                ? "PDF 未包含可提取文本；扫描件需要先完成 OCR。"
                : "知识文件未包含可提取文本。";
            throw new KnowledgeUploadValidationException(message);
        }
        if (text.Length > MaxExtractedCharacters)
            throw new KnowledgeUploadValidationException("知识文件提取后的文本不能超过 500 万字符。", isTooLarge: true);

        return new KnowledgeUploadExtraction(bytes, text, format);
    }

    private static string ResolveFormat(string extension, string? contentType)
    {
        var normalizedContentType = contentType?.Split(';', 2)[0].Trim().ToLowerInvariant() ?? string.Empty;
        return extension switch
        {
            ".txt" when IsAllowedMime(normalizedContentType, "text/plain") => "txt",
            ".pdf" when IsAllowedMime(normalizedContentType, "application/pdf") => "pdf",
            ".epub" when IsAllowedMime(normalizedContentType, "application/epub+zip", "application/zip") => "epub",
            ".txt" or ".pdf" or ".epub" => throw new KnowledgeUploadValidationException(
                $"文件扩展名 {extension} 与 MIME 类型 {normalizedContentType} 不匹配。"),
            _ => throw new KnowledgeUploadValidationException("仅支持 .txt、.pdf 和 .epub 知识文件。")
        };
    }

    private static bool IsAllowedMime(string contentType, params string[] allowed)
    {
        return string.IsNullOrWhiteSpace(contentType) ||
               contentType == "application/octet-stream" ||
               allowed.Contains(contentType, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<byte[]> ReadBytesAsync(IFormFile file, CancellationToken cancellationToken)
    {
        await using var input = file.OpenReadStream();
        using var output = new MemoryStream(checked((int)file.Length));
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            total += read;
            if (total > MaxUploadBytes)
                throw new KnowledgeUploadValidationException("知识文件不能超过 25 MB。", isTooLarge: true);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return output.ToArray();
    }

    private static string ExtractPlainText(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
            return StrictUtf8.GetString(bytes, Encoding.UTF8.Preamble.Length, bytes.Length - Encoding.UTF8.Preamble.Length);
        if (bytes.AsSpan().StartsWith(Encoding.Unicode.Preamble))
            return StrictUtf16LittleEndian.GetString(bytes, Encoding.Unicode.Preamble.Length, bytes.Length - Encoding.Unicode.Preamble.Length);
        if (bytes.AsSpan().StartsWith(Encoding.BigEndianUnicode.Preamble))
            return StrictUtf16BigEndian.GetString(bytes, Encoding.BigEndianUnicode.Preamble.Length, bytes.Length - Encoding.BigEndianUnicode.Preamble.Length);
        return StrictUtf8.GetString(bytes);
    }

    private static string ExtractPdfText(byte[] bytes)
    {
        try
        {
            using var document = PdfDocument.Open(bytes);
            if (document.NumberOfPages > MaxPdfPages)
                throw new KnowledgeUploadValidationException("PDF 不能超过 2000 页。", isTooLarge: true);

            var text = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                AppendWithinCharacterLimit(text, page.Text);
                text.AppendLine().AppendLine();
            }
            return text.ToString();
        }
        catch (KnowledgeUploadValidationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new KnowledgeUploadValidationException("PDF 文件格式无效、已加密或已损坏。", innerException: exception);
        }
    }

    private static async Task<string> ExtractEpubTextAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
            ValidateArchiveLimits(archive);
            var entries = archive.Entries
                .Where(entry => !string.IsNullOrEmpty(entry.Name))
                .ToDictionary(entry => NormalizeArchiveEntryName(entry.FullName), StringComparer.OrdinalIgnoreCase);

            var containerEntry = GetRequiredEntry(entries, "META-INF/container.xml");
            var container = await LoadXmlAsync(containerEntry, MaxEpubMetadataBytes, cancellationToken).ConfigureAwait(false);
            var packagePath = container.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "rootfile")
                ?.Attribute("full-path")?.Value;
            if (string.IsNullOrWhiteSpace(packagePath))
                throw new KnowledgeUploadValidationException("EPUB 缺少 META-INF/container.xml 中的 rootfile。");

            packagePath = NormalizeArchiveEntryName(packagePath);
            var packageEntry = GetRequiredEntry(entries, packagePath);
            var package = await LoadXmlAsync(packageEntry, MaxEpubMetadataBytes, cancellationToken).ConfigureAwait(false);
            var manifest = package.Descendants()
                .Where(element => element.Name.LocalName == "item")
                .Select(element => new
                {
                    Id = element.Attribute("id")?.Value,
                    Href = element.Attribute("href")?.Value,
                    MediaType = element.Attribute("media-type")?.Value
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Href))
                .ToDictionary(item => item.Id!, StringComparer.Ordinal);
            var spineIds = package.Descendants()
                .Where(element => element.Name.LocalName == "itemref")
                .Select(element => element.Attribute("idref")?.Value)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .ToList();
            if (spineIds.Count == 0)
                throw new KnowledgeUploadValidationException("EPUB 未声明可阅读的 spine 章节。");

            var packageDirectory = GetArchiveDirectory(packagePath);
            var text = new StringBuilder();
            foreach (var spineId in spineIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!manifest.TryGetValue(spineId, out var item) ||
                    !string.Equals(item.MediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var sectionPath = ResolveArchivePath(packageDirectory, item.Href!);
                var sectionEntry = GetRequiredEntry(entries, sectionPath);
                var section = await LoadXmlAsync(sectionEntry, MaxEpubSectionBytes, cancellationToken).ConfigureAwait(false);
                var body = section.Descendants().FirstOrDefault(element => element.Name.LocalName == "body");
                if (body == null)
                    continue;
                var sectionText = string.Join(' ', body.DescendantNodes()
                    .OfType<XText>()
                    .Select(value => WebUtility.HtmlDecode(value.Value).Trim())
                    .Where(value => value.Length > 0));
                AppendWithinCharacterLimit(text, sectionText);
                text.AppendLine().AppendLine();
            }

            return text.ToString();
        }
        catch (KnowledgeUploadValidationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or IOException or ArgumentException)
        {
            throw new KnowledgeUploadValidationException("EPUB 文件格式无效或已损坏。", innerException: exception);
        }
    }

    private static void ValidateArchiveLimits(ZipArchive archive)
    {
        if (archive.Entries.Count > MaxEpubEntries)
            throw new KnowledgeUploadValidationException("EPUB 文件条目不能超过 2000 个。", isTooLarge: true);

        long totalUncompressedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > MaxEpubSectionBytes && !string.IsNullOrEmpty(entry.Name))
                throw new KnowledgeUploadValidationException("EPUB 中的单个文件不能超过 10 MB。", isTooLarge: true);
            totalUncompressedBytes = checked(totalUncompressedBytes + entry.Length);
            if (totalUncompressedBytes > MaxEpubUncompressedBytes)
                throw new KnowledgeUploadValidationException("EPUB 解压后的总大小不能超过 100 MB。", isTooLarge: true);
        }
    }

    private static async Task<XDocument> LoadXmlAsync(
        ZipArchiveEntry entry,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        await using var input = entry.Open();
        using var bounded = new MemoryStream();
        var buffer = new byte[32 * 1024];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            total += read;
            if (total > maxBytes)
                throw new KnowledgeUploadValidationException("EPUB 内部文档超过允许大小。", isTooLarge: true);
            await bounded.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        bounded.Position = 0;
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = maxBytes
        };
        using var reader = XmlReader.Create(bounded, settings);
        return await XDocument.LoadAsync(reader, LoadOptions.PreserveWhitespace, cancellationToken).ConfigureAwait(false);
    }

    private static ZipArchiveEntry GetRequiredEntry(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        string fullName)
    {
        if (entries.TryGetValue(NormalizeArchiveEntryName(fullName), out var entry))
            return entry;
        throw new KnowledgeUploadValidationException($"EPUB 缺少必需文件：{fullName}");
    }

    private static string ResolveArchivePath(string directory, string href)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out _))
            throw new KnowledgeUploadValidationException("EPUB spine 不能引用外部地址。");

        var baseUri = new Uri($"https://epub.local/{directory.TrimStart('/')}");
        var resolved = new Uri(baseUri, href);
        if (!string.Equals(resolved.Host, "epub.local", StringComparison.Ordinal))
            throw new KnowledgeUploadValidationException("EPUB spine 路径超出文件边界。");
        return NormalizeArchiveEntryName(Uri.UnescapeDataString(resolved.AbsolutePath.TrimStart('/')));
    }

    private static string GetArchiveDirectory(string fullName)
    {
        var slash = fullName.LastIndexOf('/');
        return slash < 0 ? string.Empty : fullName[..(slash + 1)];
    }

    private static string NormalizeArchiveEntryName(string fullName)
    {
        return fullName.Replace('\\', '/').TrimStart('/');
    }

    private static void AppendWithinCharacterLimit(StringBuilder builder, string value)
    {
        if (builder.Length + value.Length > MaxExtractedCharacters)
            throw new KnowledgeUploadValidationException("知识文件提取后的文本不能超过 500 万字符。", isTooLarge: true);
        builder.Append(value);
    }

    private static string NormalizeExtractedText(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }
}
