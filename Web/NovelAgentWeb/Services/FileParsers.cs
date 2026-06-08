using System.IO.Compression;
using System.Text.RegularExpressions;

namespace TM.Web.NovelAgentWeb.Services;

public static class FileParsers
{
    public static async Task<string> ParseAsync(Stream stream, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".epub" => await ParseEpubAsync(stream),
            _ => await ParseTxtAsync(stream),
        };
    }

    public static async Task<string> ParseTxtAsync(Stream stream)
    {
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    public static async Task<string> ParseEpubAsync(Stream stream)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        ms.Position = 0;

        var texts = new List<string>();
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.ToLowerInvariant();
            if (!name.EndsWith(".xhtml") && !name.EndsWith(".html") && !name.EndsWith(".htm"))
                continue;

            await using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream);
            var html = await reader.ReadToEndAsync();

            // Strip HTML tags
            var text = Regex.Replace(html, "<[^>]+>", " ");
            // Decode HTML entities
            text = System.Net.WebUtility.HtmlDecode(text);
            // Normalize whitespace
            text = Regex.Replace(text, @"\s+", " ").Trim();

            if (text.Length > 50) // Skip tiny fragments
                texts.Add(text);
        }

        return string.Join("\n\n", texts);
    }

    public static string ChunkForAnalysis(string text, int maxChars = 12000)
    {
        if (text.Length <= maxChars)
            return text;

        // Extract key segments: start, 10%, 30%, 60%, end
        var chunkSize = maxChars / 5;
        var segments = new[]
        {
            ExtractSegment(text, 0, chunkSize),
            ExtractSegment(text, (int)(text.Length * 0.1), chunkSize),
            ExtractSegment(text, (int)(text.Length * 0.3), chunkSize),
            ExtractSegment(text, (int)(text.Length * 0.6), chunkSize),
            ExtractSegment(text, text.Length - chunkSize, chunkSize),
        };

        return string.Join("\n\n---\n\n", segments);
    }

    private static string ExtractSegment(string text, int start, int length)
    {
        start = Math.Max(0, Math.Min(start, text.Length - 1));
        length = Math.Min(length, text.Length - start);
        return text.Substring(start, length);
    }
}
