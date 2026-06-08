using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public partial class WorkspacePlugin
    {
        private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".json", ".xml", ".yaml", ".yml", ".csv", ".log",
            ".cs", ".xaml", ".csproj", ".sln", ".props", ".targets",
            ".html", ".css", ".js", ".ts", ".py", ".sh", ".bat", ".ps1",
            ".ini", ".cfg", ".toml", ".env", ".gitignore", ".editorconfig"
        };

        #region 读工具

        [KernelFunction("ListDirectory")]
        [Description("列出指定目录下的文件和子目录。返回名称、类型（文件/目录）、大小。relativePath 为空则列出项目根目录。")]
        public string ListDirectory(
            [Description("相对路径（相对于项目存储根），留空则为根目录")] string relativePath = "",
            [Description("递归深度（1=仅当前目录，2=展开一层子目录...），默认 1，最大 5")] int maxDepth = 1,
            [Description("排除的目录名（逗号分隔），如 bin,obj,.vs")] string excludePatterns = "")
        {
            try
            {
                string dirPath;
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    dirPath = Path.GetFullPath(StoragePathHelper.GetCurrentProjectPath());
                }
                else
                {
                    if (!SafePathHelper.TryResolveSafePath(relativePath, out dirPath, out var error, allowBusinessPaths: true))
                        return $"[错误] {error}";
                }

                if (!Directory.Exists(dirPath))
                    return $"[错误] 目录不存在: {relativePath}";

                maxDepth = Math.Clamp(maxDepth, 1, 5);

                var excludeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(excludePatterns))
                {
                    foreach (var part in excludePatterns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        excludeDirs.Add(part);
                }

                var sb = new StringBuilder();
                sb.AppendLine($"=== 目录: {(string.IsNullOrWhiteSpace(relativePath) ? "/" : relativePath)} ===");

                int totalDirs = 0, totalFiles = 0;
                ListDirectoryRecursive(dirPath, sb, indent: "  ", currentDepth: 1, maxDepth, ref totalDirs, ref totalFiles, excludeDirs);

                if (totalDirs == 0 && totalFiles == 0)
                    sb.AppendLine("  (空目录)");

                sb.AppendLine($"\n共 {totalDirs} 个目录, {totalFiles} 个文件");
                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorkspacePlugin] ListDirectory 异常: {ex.Message}");
                return $"[错误] 列目录失败: {ex.Message}";
            }
        }

        [KernelFunction("SearchFiles")]
        [Description("在项目目录中搜索文件或目录。支持通配符、扩展名过滤、深度限制和类型筛选。")]
        public string SearchFiles(
            [Description("文件名搜索模式，支持通配符 * 和 ?，如 *.md、config*.json。留空则匹配全部")] string pattern = "*",
            [Description("搜索起始目录的相对路径，留空则搜索整个项目")] string relativePath = "",
            [Description("最大返回结果数，默认 50")] int maxResults = 50,
            [Description("扩展名过滤（逗号分隔，不含点），如 md,json,cs。留空则不过滤扩展名")] string extensions = "",
            [Description("最大搜索深度（1=仅当前目录，2=含一层子目录...），默认 0 表示不限")] int maxDepth = 0,
            [Description("搜索类型：file=仅文件，directory=仅目录，any=全部。默认 file")] string type = "file",
            [Description("排除的目录名（逗号分隔），如 bin,obj,.vs")] string excludePatterns = "")
        {
            try
            {
                string searchRoot;
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    searchRoot = Path.GetFullPath(StoragePathHelper.GetCurrentProjectPath());
                }
                else
                {
                    if (!SafePathHelper.TryResolveSafePath(relativePath, out searchRoot, out var error, allowBusinessPaths: true))
                        return $"[错误] {error}";
                }

                if (!Directory.Exists(searchRoot))
                    return $"[错误] 目录不存在: {relativePath}";

                var projectRoot = Path.GetFullPath(StoragePathHelper.GetCurrentProjectPath());
                var rootWithSep = projectRoot.EndsWith(Path.DirectorySeparatorChar)
                    ? projectRoot : projectRoot + Path.DirectorySeparatorChar;

                maxResults = Math.Clamp(maxResults, 1, 200);
                var searchPattern = string.IsNullOrWhiteSpace(pattern) ? "*" : pattern;
                var searchType = (type ?? "file").Trim().ToLowerInvariant();

                var excludeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(excludePatterns))
                {
                    foreach (var part in excludePatterns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        excludeDirs.Add(part);
                }

                var extFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(extensions))
                {
                    foreach (var ext in extensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        extFilter.Add(ext.StartsWith('.') ? ext : "." + ext);
                }

                var sb = new StringBuilder();
                sb.AppendLine($"=== 搜索: \"{searchPattern}\" ===");
                int count = 0;

                if (searchType is "file" or "any")
                {
                    var files = maxDepth > 0
                        ? EnumerateFilesWithDepth(searchRoot, searchPattern, maxDepth)
                        : Directory.EnumerateFiles(searchRoot, searchPattern, SearchOption.AllDirectories);

                    foreach (var f in files)
                    {
                        if (count >= maxResults) break;
                        if (excludeDirs.Count > 0 && ShouldExclude(f, excludeDirs))
                            continue;
                        if (extFilter.Count > 0 && !extFilter.Contains(Path.GetExtension(f)))
                            continue;

                        var rel = f.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase)
                            ? f[rootWithSep.Length..] : Path.GetFileName(f);
                        var size = FormatFileSize(new FileInfo(f).Length);
                        sb.AppendLine($"  📄 {rel}  ({size})");
                        count++;
                    }
                }

                if (searchType is "directory" or "any")
                {
                    var dirs = maxDepth > 0
                        ? EnumerateDirectoriesWithDepth(searchRoot, searchPattern, maxDepth)
                        : Directory.EnumerateDirectories(searchRoot, searchPattern, SearchOption.AllDirectories);

                    foreach (var d in dirs)
                    {
                        if (count >= maxResults) break;
                        if (excludeDirs.Count > 0 && ShouldExclude(d, excludeDirs))
                            continue;
                        var rel = d.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase)
                            ? d[rootWithSep.Length..] : Path.GetFileName(d);
                        var itemCount = 0;
                        try { itemCount = Directory.GetFileSystemEntries(d).Length; } catch { }
                        sb.AppendLine($"  📁 {rel}/  ({itemCount} 项)");
                        count++;
                    }
                }

                if (count == 0)
                    return $"未找到匹配 \"{searchPattern}\" 的{(searchType == "directory" ? "目录" : searchType == "any" ? "文件或目录" : "文件")}";

                if (count >= maxResults)
                    sb.AppendLine($"\n  ... 结果已截断（超过 {maxResults} 条）");

                sb.AppendLine($"\n共 {count} 个匹配项");
                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorkspacePlugin] SearchFiles 异常: {ex.Message}");
                return $"[错误] 搜索失败: {ex.Message}";
            }
        }

        [KernelFunction("GrepInFiles")]
        [Description("在项目文本文件中搜索包含指定关键词的行。返回匹配的文件路径、行号和行内容。仅搜索文本文件。")]
        public string GrepInFiles(
            [Description("搜索关键词（支持正则表达式，fixedStrings=true 时为纯文本）")] string keyword,
            [Description("搜索起始目录的相对路径，留空则搜索整个项目")] string relativePath = "",
            [Description("文件名过滤模式，如 *.md、*.cs，留空则搜索所有文本文件")] string filePattern = "",
            [Description("最大返回匹配数，默认 30")] int maxMatches = 30,
            [Description("是否区分大小写，默认 false（不区分）")] bool caseSensitive = false,
            [Description("是否为纯文本匹配（true 时不解析正则，直接字符串匹配），默认 false")] bool fixedStrings = false,
            [Description("返回匹配行前后各 N 行上下文，默认 0（仅匹配行），最大 5")] int contextLines = 0,
            [Description("排除的目录名（逗号分隔），如 bin,obj,.vs")] string excludePatterns = "",
            [Description("单行最大显示字符数，默认 200，最大 500")] int maxLineLength = 200)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(keyword))
                    return "[错误] 搜索关键词不能为空";

                string searchRoot;
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    searchRoot = Path.GetFullPath(StoragePathHelper.GetCurrentProjectPath());
                }
                else
                {
                    if (!SafePathHelper.TryResolveSafePath(relativePath, out searchRoot, out var error, allowBusinessPaths: true))
                        return $"[错误] {error}";
                }

                if (!Directory.Exists(searchRoot))
                    return $"[错误] 目录不存在: {relativePath}";

                var projectRoot = Path.GetFullPath(StoragePathHelper.GetCurrentProjectPath());
                var rootWithSep = projectRoot.EndsWith(Path.DirectorySeparatorChar)
                    ? projectRoot : projectRoot + Path.DirectorySeparatorChar;

                maxMatches = Math.Clamp(maxMatches, 1, 100);
                contextLines = Math.Clamp(contextLines, 0, 5);
                maxLineLength = Math.Clamp(maxLineLength, 50, 500);

                var excludeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(excludePatterns))
                {
                    foreach (var part in excludePatterns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        excludeDirs.Add(part);
                }

                Regex? regex = null;
                if (!fixedStrings)
                {
                    try
                    {
                        var regexOptions = RegexOptions.Compiled | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
                        regex = new Regex(keyword, regexOptions, TimeSpan.FromSeconds(2));
                    }
                    catch
                    {
                        regex = null;
                    }
                }

                var stringComparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

                var searchPattern = string.IsNullOrWhiteSpace(filePattern) ? "*.*" : filePattern;
                var files = Directory.EnumerateFiles(searchRoot, searchPattern, SearchOption.AllDirectories)
                    .Where(f => TextExtensions.Contains(Path.GetExtension(f)))
                    .Where(f => !ShouldExclude(f, excludeDirs));

                var sb = new StringBuilder();
                sb.AppendLine($"=== Grep: \"{keyword}\" ===");
                int totalMatches = 0;
                int fileCount = 0;

                foreach (var filePath in files)
                {
                    if (totalMatches >= maxMatches) break;

                    try
                    {
                        var bufferSize = contextLines > 0 ? contextLines : 0;
                        var ringBuffer = bufferSize > 0 ? new string[bufferSize] : null;
                        int ringIdx = 0;
                        bool fileHeaderWritten = false;
                        int lineNum = 0;

                        int pendingAfterLines = 0;
                        int lastOutputLineNum = 0;

                        using var reader = new StreamReader(filePath);
                        string? currentLine;
                        while ((currentLine = reader.ReadLine()) != null)
                        {
                            lineNum++;
                            if (totalMatches >= maxMatches && pendingAfterLines <= 0) break;

                            bool matched = regex != null
                                ? regex.IsMatch(currentLine)
                                : currentLine.Contains(keyword, stringComparison);

                            if (pendingAfterLines > 0)
                            {
                                if (matched && totalMatches < maxMatches)
                                {
                                    pendingAfterLines = 0;
                                }
                                else
                                {
                                    if (lineNum > lastOutputLineNum)
                                    {
                                        var ctxLine = TruncateLine(currentLine, maxLineLength);
                                        sb.AppendLine($"  {lineNum,5} | {ctxLine}");
                                        lastOutputLineNum = lineNum;
                                    }
                                    pendingAfterLines--;
                                    if (pendingAfterLines == 0)
                                        sb.AppendLine();

                                    if (ringBuffer != null)
                                    {
                                        ringBuffer[ringIdx % bufferSize] = currentLine;
                                        ringIdx++;
                                    }
                                    continue;
                                }
                            }

                            if (matched && totalMatches < maxMatches)
                            {
                                if (!fileHeaderWritten)
                                {
                                    var rel = filePath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase)
                                        ? filePath.Substring(rootWithSep.Length)
                                        : Path.GetFileName(filePath);
                                    sb.AppendLine($"\n📄 {rel}:");
                                    fileHeaderWritten = true;
                                    fileCount++;
                                }

                                if (ringBuffer != null)
                                {
                                    var stored = Math.Min(ringIdx, bufferSize);
                                    var start = ringIdx - stored;
                                    for (int c = start; c < ringIdx; c++)
                                    {
                                        var ctxLineNum = lineNum - (ringIdx - c);
                                        if (ctxLineNum <= lastOutputLineNum) continue;
                                        var bufLine = ringBuffer[c % bufferSize];
                                        var ctxLine = TruncateLine(bufLine, maxLineLength);
                                        sb.AppendLine($"  {ctxLineNum,5} | {ctxLine}");
                                    }
                                }

                                var trimmedLine = TruncateLine(currentLine, maxLineLength);
                                sb.AppendLine($"  {lineNum,5} > {trimmedLine}");
                                totalMatches++;
                                lastOutputLineNum = lineNum;

                                if (contextLines > 0)
                                    pendingAfterLines = contextLines;
                            }

                            if (ringBuffer != null)
                            {
                                ringBuffer[ringIdx % bufferSize] = currentLine;
                                ringIdx++;
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                if (totalMatches == 0)
                    return $"未找到包含 \"{keyword}\" 的内容";

                sb.AppendLine($"\n共 {totalMatches} 处匹配（{fileCount} 个文件）");
                if (totalMatches >= maxMatches)
                    sb.AppendLine($"  结果已截断（最多 {maxMatches} 条）");

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorkspacePlugin] GrepInFiles 异常: {ex.Message}");
                return $"[错误] 搜索失败: {ex.Message}";
            }
        }

        [KernelFunction("ReadFileLines")]
        [Description("读取文件内容，支持指定行范围。返回带行号的文件内容。仅限文本文件。")]
        public Task<string> ReadFileLinesAsync(
            [Description("文件的相对路径")] string relativePath,
            [Description("起始行号（从 1 开始），默认 1")] int startLine = 1,
            [Description("读取的行数，默认 200，最大 500")] int lineCount = 200,
            [Description("单行最大显示字符数，默认 500，最大 2000")] int maxLineLength = 500)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(relativePath))
                    return Task.FromResult("[错误] 文件路径不能为空");

                if (!SafePathHelper.TryResolveSafePath(relativePath, out var fullPath, out var error, allowBusinessPaths: true))
                    return Task.FromResult($"[错误] {error}");

                if (!File.Exists(fullPath))
                    return Task.FromResult($"[错误] 文件不存在: {relativePath}");

                var ext = Path.GetExtension(fullPath);
                if (!TextExtensions.Contains(ext))
                    return Task.FromResult($"[错误] 不支持的文件类型: {ext}（仅支持文本文件）");

                startLine = Math.Max(1, startLine);
                lineCount = Math.Clamp(lineCount, 1, 500);
                maxLineLength = Math.Clamp(maxLineLength, 100, 2000);

                var endLine = startLine + lineCount - 1;
                var sb = new StringBuilder();
                int currentLine = 0;
                int collectedLines = 0;

                using (var reader = new StreamReader(fullPath))
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        currentLine++;
                        if (currentLine < startLine) continue;
                        if (currentLine > endLine) break;

                        if (line.Length > maxLineLength)
                            line = line[..maxLineLength] + "...";
                        sb.AppendLine($"{currentLine,5} | {line}");
                        collectedLines++;
                    }
                    while (reader.ReadLine() != null)
                        currentLine++;
                }

                var totalLines = currentLine;

                if (collectedLines == 0)
                    return Task.FromResult($"文件共 {totalLines} 行，起始行 {startLine} 超出范围");

                var actualEnd = startLine + collectedLines - 1;
                var header = $"=== {relativePath} (行 {startLine}-{actualEnd} / 共 {totalLines} 行) ===\n";
                sb.Insert(0, header);

                if (actualEnd < totalLines)
                    sb.AppendLine($"\n... 后续还有 {totalLines - actualEnd} 行");

                return Task.FromResult(sb.ToString().TrimEnd());
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorkspacePlugin] ReadFileLines 异常: {ex.Message}");
                return Task.FromResult($"[错误] 读取文件失败: {ex.Message}");
            }
        }

        #endregion

        #region 工具方法

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }

        private static void ListDirectoryRecursive(string dirPath, StringBuilder sb, string indent, int currentDepth, int maxDepth, ref int totalDirs, ref int totalFiles, HashSet<string>? excludeDirs = null)
        {
            var dirs = Directory.GetDirectories(dirPath);
            foreach (var d in dirs.OrderBy(x => x))
            {
                var name = Path.GetFileName(d);
                if (excludeDirs != null && excludeDirs.Count > 0 && excludeDirs.Contains(name))
                    continue;

                var itemCount = 0;
                try { itemCount = Directory.GetFileSystemEntries(d).Length; } catch { }
                sb.AppendLine($"{indent}📁 {name}/  ({itemCount} 项)");
                totalDirs++;

                if (currentDepth < maxDepth)
                {
                    ListDirectoryRecursive(d, sb, indent + "  ", currentDepth + 1, maxDepth, ref totalDirs, ref totalFiles, excludeDirs);
                }
            }

            var files = Directory.GetFiles(dirPath);
            foreach (var f in files.OrderBy(x => x))
            {
                var name = Path.GetFileName(f);
                var sizeStr = FormatFileSize(new FileInfo(f).Length);
                sb.AppendLine($"{indent}📄 {name}  ({sizeStr})");
                totalFiles++;
            }
        }

        private static string TruncateLine(string line, int maxLength)
        {
            return line.Length > maxLength ? line[..maxLength] + "..." : line;
        }

        private static bool ShouldExclude(string filePath, HashSet<string> excludeDirs)
        {
            if (excludeDirs.Count == 0) return false;
            var parts = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (var part in parts)
            {
                if (excludeDirs.Contains(part)) return true;
                foreach (var pattern in excludeDirs)
                {
                    if ((pattern.Contains('*') || pattern.Contains('?'))
                        && WildcardMatch(pattern, part))
                        return true;
                }
            }
            return false;
        }

        private static bool WildcardMatch(string pattern, string input)
        {
            var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
        }

        private static IEnumerable<string> EnumerateFilesWithDepth(string root, string pattern, int maxDepth)
        {
            return EnumerateWithDepth(root, pattern, maxDepth, searchFiles: true);
        }

        private static IEnumerable<string> EnumerateDirectoriesWithDepth(string root, string pattern, int maxDepth)
        {
            return EnumerateWithDepth(root, pattern, maxDepth, searchFiles: false);
        }

        private static IEnumerable<string> EnumerateWithDepth(string root, string pattern, int maxDepth, bool searchFiles)
        {
            if (maxDepth <= 0) yield break;

            if (searchFiles)
            {
                foreach (var f in Directory.EnumerateFiles(root, pattern))
                    yield return f;
            }
            else
            {
                foreach (var d in Directory.EnumerateDirectories(root, pattern))
                    yield return d;
            }

            if (maxDepth > 1)
            {
                foreach (var subDir in Directory.EnumerateDirectories(root))
                {
                    foreach (var item in EnumerateWithDepth(subDir, pattern, maxDepth - 1, searchFiles))
                        yield return item;
                }
            }
        }

        #endregion
    }
}
