using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    public partial class WorkspacePlugin
    {
        #region 局部替换闭环（ReplaceInFile / ConfirmFileEdit / RollbackFileEdit / ExecuteReplaceInFile）

        [KernelFunction("ReplaceInFile")]
        [Description("在文件中执行局部文本替换（预览模式，不落盘）。返回 previewId 和 diff。确认无误后调用 ConfirmFileEdit(previewId) 落盘。")]
        public async Task<string> ReplaceInFileAsync(
            [Description("文件的相对路径")] string relativePath,
            [Description("要替换的原始文本（精确匹配）")] string oldText,
            [Description("替换后的新文本")] string newText)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(relativePath))
                    return "[错误] 文件路径不能为空";
                if (string.IsNullOrEmpty(oldText))
                    return "[错误] 原始文本不能为空";

                if (!SafePathHelper.TryResolveSafePath(relativePath, out var fullPath, out var error))
                    return $"[错误] {error}";

                if (!File.Exists(fullPath))
                    return $"[错误] 文件不存在: {relativePath}";

                var content = await File.ReadAllTextAsync(fullPath).ConfigureAwait(false);
                var idx = content.IndexOf(oldText, StringComparison.Ordinal);
                if (idx < 0)
                    return "[错误] 未找到要替换的文本（请确保精确匹配）";

                var secondIdx = content.IndexOf(oldText, idx + oldText.Length, StringComparison.Ordinal);
                if (secondIdx >= 0)
                    return "[错误] 找到多处匹配，请提供更多上下文以精确定位唯一匹配";

                var newContent = content[..idx] + newText + content[(idx + oldText.Length)..];
                var hash = FilePreviewStore.ComputeHash(content);
                var diff = BuildReplaceDiff(oldText, newText, idx, content);
                var previewId = FilePreviewStore.CreateReplacePreview(relativePath, fullPath, content, newContent, diff, hash);

                var result = new
                {
                    previewId,
                    file = relativePath,
                    operation = "replace",
                    summary = diff
                };
                return JsonSerializer.Serialize(result, JsonHelper.Default);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorkspacePlugin] ReplaceInFile 异常: {ex.Message}");
                return $"[错误] 替换预览失败: {ex.Message}";
            }
        }

        [KernelFunction("ConfirmFileEdit")]
        [Description("确认执行预览的文件操作（落盘）。适用于 ReplaceInFile / PreviewWriteFile / PreviewDeleteFile / PreviewRenameFile 产生的 previewId。")]
        public async Task<string> ConfirmFileEditAsync(
            [Description("预览时返回的 previewId")] string previewId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(previewId))
                    return "[错误] previewId 不能为空";

                var entry = FilePreviewStore.GetPreview(previewId);
                if (entry == null)
                    return "[错误] 预览不存在或已过期（5分钟 TTL），请重新执行预览操作";

                if (entry.OperationType != FileOperationType.Rename)
                {
                    if (File.Exists(entry.FullPath))
                    {
                        var currentContent = await File.ReadAllTextAsync(entry.FullPath).ConfigureAwait(false);
                        var currentHash = FilePreviewStore.ComputeHash(currentContent);
                        if (currentHash != entry.OriginalHash)
                        {
                            FilePreviewStore.Remove(previewId);
                            return "[错误] 文件在预览后已被外部修改，为避免冲突已取消操作。请重新执行预览。";
                        }
                    }
                }

                string resultMsg;
                switch (entry.OperationType)
                {
                    case FileOperationType.Replace:
                    case FileOperationType.Write:
                        EnsureDirectory(entry.FullPath);
                        var tmp = entry.FullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        await File.WriteAllTextAsync(tmp, entry.NewContent, Encoding.UTF8).ConfigureAwait(false);
                        File.Move(tmp, entry.FullPath, overwrite: true);
                        resultMsg = $"✅ 文件已更新: {entry.RelativePath}";
                        break;

                    case FileOperationType.Delete:
                        if (File.Exists(entry.FullPath))
                        {
                            File.Delete(entry.FullPath);
                            resultMsg = $"✅ 文件已删除: {entry.RelativePath}";
                        }
                        else
                        {
                            resultMsg = $"⚠ 文件已不存在: {entry.RelativePath}";
                        }
                        break;

                    case FileOperationType.Rename:
                        if (!File.Exists(entry.FullPath))
                        {
                            FilePreviewStore.Remove(previewId);
                            return $"[错误] 源文件已不存在: {entry.RelativePath}";
                        }
                        if (string.IsNullOrEmpty(entry.NewFullPath))
                        {
                            FilePreviewStore.Remove(previewId);
                            return "[错误] 目标路径为空";
                        }
                        EnsureDirectory(entry.NewFullPath);
                        File.Move(entry.FullPath, entry.NewFullPath, overwrite: false);
                        resultMsg = $"✅ 文件已重命名: {entry.RelativePath} → {entry.NewRelativePath}";
                        break;

                    default:
                        resultMsg = $"[错误] 未知操作类型: {entry.OperationType}";
                        break;
                }

                FileEditHistoryLog.Append(entry, resultMsg);
                FilePreviewStore.Remove(previewId);

                TM.App.Log($"[WorkspacePlugin] ConfirmFileEdit 完成: {previewId} → {entry.OperationType} {entry.RelativePath}");

                try
                {
                    GlobalToast.Success("文件操作完成", resultMsg.Replace("✅ ", ""));
                }
                catch { }

                return resultMsg;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorkspacePlugin] ConfirmFileEdit 异常: {ex.Message}");
                return $"[错误] 确认操作失败: {ex.Message}";
            }
        }

        [KernelFunction("RollbackFileEdit")]
        [Description("取消/回滚预览的文件操作（不落盘）。")]
        public string RollbackFileEdit(
            [Description("预览时返回的 previewId")] string previewId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(previewId))
                    return "[错误] previewId 不能为空";

                var entry = FilePreviewStore.GetPreview(previewId);
                if (entry == null)
                    return "[错误] 预览不存在或已过期";

                FilePreviewStore.Remove(previewId);
                TM.App.Log($"[WorkspacePlugin] RollbackFileEdit: {previewId} → {entry.OperationType} {entry.RelativePath}");
                return $"✅ 已取消操作: {entry.OperationType} {entry.RelativePath}";
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorkspacePlugin] RollbackFileEdit 异常: {ex.Message}");
                return $"[错误] 回滚失败: {ex.Message}";
            }
        }

        [KernelFunction("ExecuteReplaceInFile")]
        [Description("直接执行文件局部替换（预览+确认一步完成，无需用户确认）。Agent/Plan 模式专用。")]
        public async Task<string> ExecuteReplaceInFileAsync(
            [Description("文件的相对路径")] string relativePath,
            [Description("要替换的原始文本（精确匹配）")] string oldText,
            [Description("替换后的新文本")] string newText)
        {
            try
            {
                var previewResult = await ReplaceInFileAsync(relativePath, oldText, newText).ConfigureAwait(false);
                if (previewResult.Contains("[错误]"))
                    return previewResult;

                var previewId = ExtractPreviewId(previewResult);
                if (string.IsNullOrEmpty(previewId))
                    return "[错误] 无法提取 previewId";

                var confirmResult = await ConfirmFileEditAsync(previewId).ConfigureAwait(false);
                TM.App.Log($"[WorkspacePlugin] ExecuteReplaceInFile 完成: {relativePath}");
                return confirmResult;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorkspacePlugin] ExecuteReplaceInFile 异常: {ex.Message}");
                return $"[错误] 执行失败: {ex.Message}";
            }
        }

        [KernelFunction("MultiReplaceInFile")]
        [Description("在同一文件中执行多段局部文本替换（预览模式，不落盘）。所有替换原子应用，任一段未找到则全部失败。返回 previewId 和 diff。")]
        public async Task<string> MultiReplaceInFileAsync(
            [Description("文件的相对路径")] string relativePath,
            [Description("替换操作数组 JSON，格式: [{\"oldText\":\"...\",\"newText\":\"...\"}]")] string replacementsJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(relativePath))
                    return "[错误] 文件路径不能为空";
                if (string.IsNullOrWhiteSpace(replacementsJson))
                    return "[错误] 替换操作不能为空";

                if (!SafePathHelper.TryResolveSafePath(relativePath, out var fullPath, out var error))
                    return $"[错误] {error}";

                if (!File.Exists(fullPath))
                    return $"[错误] 文件不存在: {relativePath}";

                var replacements = JsonSerializer.Deserialize<List<ReplaceSegment>>(replacementsJson, JsonHelper.Default);
                if (replacements == null || replacements.Count == 0)
                    return "[错误] 替换操作数组为空";

                var content = await File.ReadAllTextAsync(fullPath).ConfigureAwait(false);
                var newContent = content;
                var diffLines = new StringBuilder();

                var segments = new List<(int Index, string OldText, string NewText)>();
                foreach (var r in replacements)
                {
                    if (string.IsNullOrEmpty(r.OldText))
                        return "[错误] oldText 不能为空";

                    var idx = newContent.IndexOf(r.OldText, StringComparison.Ordinal);
                    if (idx < 0)
                        return $"[错误] 未找到要替换的文本: \"{(r.OldText.Length > 60 ? r.OldText[..60] + "..." : r.OldText)}\"";

                    var secondIdx = newContent.IndexOf(r.OldText, idx + r.OldText.Length, StringComparison.Ordinal);
                    if (secondIdx >= 0)
                        return $"[错误] 找到多处匹配: \"{(r.OldText.Length > 60 ? r.OldText[..60] + "..." : r.OldText)}\"，请提供更多上下文";

                    segments.Add((idx, r.OldText, r.NewText ?? string.Empty));
                }

                foreach (var (idx, oldText, newText) in segments.OrderByDescending(s => s.Index))
                {
                    newContent = newContent[..idx] + newText + newContent[(idx + oldText.Length)..];
                    var oldPreview = oldText.Length > 80 ? oldText[..80] + "..." : oldText;
                    var newPreview = newText.Length > 80 ? newText[..80] + "..." : newText;
                    diffLines.AppendLine($"  [{idx}] \"{oldPreview}\" → \"{newPreview}\"");
                }

                var hash = FilePreviewStore.ComputeHash(content);
                var diff = $"多段替换: {relativePath}（{segments.Count} 处）\n{diffLines.ToString().TrimEnd()}";
                var previewId = FilePreviewStore.CreateReplacePreview(relativePath, fullPath, content, newContent, diff, hash);

                var result = new
                {
                    previewId,
                    file = relativePath,
                    operation = "multi_replace",
                    replacementCount = segments.Count,
                    summary = diff
                };
                return JsonSerializer.Serialize(result, JsonHelper.Default);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorkspacePlugin] MultiReplaceInFile 异常: {ex.Message}");
                return $"[错误] 多段替换预览失败: {ex.Message}";
            }
        }

        [KernelFunction("ExecuteMultiReplaceInFile")]
        [Description("直接执行同一文件的多段局部替换（预览+确认一步完成）。Agent/Plan 模式专用。")]
        public async Task<string> ExecuteMultiReplaceInFileAsync(
            [Description("文件的相对路径")] string relativePath,
            [Description("替换操作数组 JSON，格式: [{\"oldText\":\"...\",\"newText\":\"...\"}]")] string replacementsJson)
        {
            try
            {
                var previewResult = await MultiReplaceInFileAsync(relativePath, replacementsJson).ConfigureAwait(false);
                if (previewResult.Contains("[错误]"))
                    return previewResult;

                var previewId = ExtractPreviewId(previewResult);
                if (string.IsNullOrEmpty(previewId))
                    return "[错误] 无法提取 previewId";

                var confirmResult = await ConfirmFileEditAsync(previewId).ConfigureAwait(false);
                TM.App.Log($"[WorkspacePlugin] ExecuteMultiReplaceInFile 完成: {relativePath}");
                return confirmResult;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorkspacePlugin] ExecuteMultiReplaceInFile 异常: {ex.Message}");
                return $"[错误] 执行失败: {ex.Message}";
            }
        }

        private sealed class ReplaceSegment
        {
            public string OldText { get; set; } = string.Empty;
            public string? NewText { get; set; }
        }

        #endregion

        #region 全量写入闭环（PreviewWriteFile / ExecuteWriteFile）

        [KernelFunction("PreviewWriteFile")]
        [Description("预览全量写入文件内容（不落盘）。文件不存在则为新建预览，存在则为覆盖预览。返回 previewId 和 diff。")]
        public async Task<string> PreviewWriteFileAsync(
            [Description("文件的相对路径")] string relativePath,
            [Description("要写入的完整文件内容")] string content)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(relativePath))
                    return "[错误] 文件路径不能为空";
                if (content == null)
                    return "[错误] 内容不能为 null";

                if (!SafePathHelper.TryResolveSafePath(relativePath, out var fullPath, out var error))
                    return $"[错误] {error}";

                var isNew = !File.Exists(fullPath);
                var originalContent = isNew ? string.Empty : await File.ReadAllTextAsync(fullPath).ConfigureAwait(false);
                var hash = FilePreviewStore.ComputeHash(originalContent);

                string diff;
                if (isNew)
                {
                    diff = $"新建文件: {relativePath}（{content.Length} 字符）";
                }
                else
                {
                    var oldLines = originalContent.Split('\n').Length;
                    var newLines = content.Split('\n').Length;
                    diff = $"覆盖文件: {relativePath}（{oldLines} 行 → {newLines} 行，{originalContent.Length} → {content.Length} 字符）";
                }

                var previewId = FilePreviewStore.CreateWritePreview(relativePath, fullPath, originalContent, content, diff, hash);

                var result = new
                {
                    previewId,
                    file = relativePath,
                    operation = isNew ? "create" : "overwrite",
                    summary = diff
                };
                return JsonSerializer.Serialize(result, JsonHelper.Default);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorkspacePlugin] PreviewWriteFile 异常: {ex.Message}");
                return $"[错误] 写入预览失败: {ex.Message}";
            }
        }

        [KernelFunction("ExecuteWriteFile")]
        [Description("直接执行全量写入文件（预览+确认一步完成，无需用户确认）。Agent/Plan 模式专用。")]
        public async Task<string> ExecuteWriteFileAsync(
            [Description("文件的相对路径")] string relativePath,
            [Description("要写入的完整文件内容")] string content)
        {
            try
            {
                var previewResult = await PreviewWriteFileAsync(relativePath, content).ConfigureAwait(false);
                if (previewResult.Contains("[错误]"))
                    return previewResult;

                var previewId = ExtractPreviewId(previewResult);
                if (string.IsNullOrEmpty(previewId))
                    return "[错误] 无法提取 previewId";

                var confirmResult = await ConfirmFileEditAsync(previewId).ConfigureAwait(false);
                TM.App.Log($"[WorkspacePlugin] ExecuteWriteFile 完成: {relativePath}");
                return confirmResult;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorkspacePlugin] ExecuteWriteFile 异常: {ex.Message}");
                return $"[错误] 执行失败: {ex.Message}";
            }
        }

        #endregion

        #region 文件管理（CreateFolder / PreviewDeleteFile / ExecuteDeleteFile / PreviewRenameFile / ExecuteRenameFile / CreateFile）

        [KernelFunction("CreateFolder")]
        [Description("创建目录。如果目录已存在则静默成功。")]
        public string CreateFolder(
            [Description("目录的相对路径")] string relativePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(relativePath))
                    return "[错误] 目录路径不能为空";

                if (!SafePathHelper.TryResolveSafePath(relativePath, out var fullPath, out var error))
                    return $"[错误] {error}";

                if (Directory.Exists(fullPath))
                    return $"✅ 目录已存在: {relativePath}";

                Directory.CreateDirectory(fullPath);
                TM.App.Log($"[WorkspacePlugin] CreateFolder: {relativePath}");
                return $"✅ 目录已创建: {relativePath}";
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorkspacePlugin] CreateFolder 异常: {ex.Message}");
                return $"[错误] 创建目录失败: {ex.Message}";
            }
        }

        [KernelFunction("CreateFile")]
        [Description("创建新文件并写入内容。如果文件已存在则报错，请使用 PreviewWriteFile 覆盖。")]
        public async Task<string> CreateFileAsync(
            [Description("文件的相对路径")] string relativePath,
            [Description("文件内容")] string content = "")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(relativePath))
                    return "[错误] 文件路径不能为空";

                if (!SafePathHelper.TryResolveSafePath(relativePath, out var fullPath, out var error))
                    return $"[错误] {error}";

                if (File.Exists(fullPath))
                    return $"[错误] 文件已存在: {relativePath}。如需覆盖，请使用 PreviewWriteFile。";

                EnsureDirectory(fullPath);
                await File.WriteAllTextAsync(fullPath, content ?? string.Empty, Encoding.UTF8).ConfigureAwait(false);

                TM.App.Log($"[WorkspacePlugin] CreateFile: {relativePath}（{content?.Length ?? 0} 字符）");
                return $"✅ 文件已创建: {relativePath}（{content?.Length ?? 0} 字符）";
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorkspacePlugin] CreateFile 异常: {ex.Message}");
                return $"[错误] 创建文件失败: {ex.Message}";
            }
        }

        [KernelFunction("PreviewDeleteFile")]
        [Description("预览删除文件（不执行）。返回 previewId 和文件信息。确认后调用 ConfirmFileEdit(previewId) 执行删除。")]
        public async Task<string> PreviewDeleteFileAsync(
            [Description("文件的相对路径")] string relativePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(relativePath))
                    return "[错误] 文件路径不能为空";

                if (!SafePathHelper.TryResolveSafePath(relativePath, out var fullPath, out var error))
                    return $"[错误] {error}";

                if (!File.Exists(fullPath))
                    return $"[错误] 文件不存在: {relativePath}";

                var content = await File.ReadAllTextAsync(fullPath).ConfigureAwait(false);
                var hash = FilePreviewStore.ComputeHash(content);
                var fileInfo = new FileInfo(fullPath);
                var previewId = FilePreviewStore.CreateDeletePreview(relativePath, fullPath, content, hash);

                var result = new
                {
                    previewId,
                    file = relativePath,
                    operation = "delete",
                    summary = $"删除文件（{FormatFileSize(fileInfo.Length)}，{content.Split('\n').Length} 行）"
                };
                return JsonSerializer.Serialize(result, JsonHelper.Default);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorkspacePlugin] PreviewDeleteFile 异常: {ex.Message}");
                return $"[错误] 删除预览失败: {ex.Message}";
            }
        }

        [KernelFunction("ExecuteDeleteFile")]
        [Description("直接执行删除文件（预览+确认一步完成，无需用户确认）。Agent/Plan 模式专用。")]
        public async Task<string> ExecuteDeleteFileAsync(
            [Description("文件的相对路径")] string relativePath)
        {
            try
            {
                var previewResult = await PreviewDeleteFileAsync(relativePath).ConfigureAwait(false);
                if (previewResult.Contains("[错误]"))
                    return previewResult;

                var previewId = ExtractPreviewId(previewResult);
                if (string.IsNullOrEmpty(previewId))
                    return "[错误] 无法提取 previewId";

                var confirmResult = await ConfirmFileEditAsync(previewId).ConfigureAwait(false);
                TM.App.Log($"[WorkspacePlugin] ExecuteDeleteFile 完成: {relativePath}");
                return confirmResult;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorkspacePlugin] ExecuteDeleteFile 异常: {ex.Message}");
                return $"[错误] 执行失败: {ex.Message}";
            }
        }

        [KernelFunction("PreviewRenameFile")]
        [Description("预览重命名/移动文件（不执行）。返回 previewId。确认后调用 ConfirmFileEdit(previewId) 执行。")]
        public string PreviewRenameFile(
            [Description("当前文件的相对路径")] string relativePath,
            [Description("新的文件相对路径")] string newRelativePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(relativePath))
                    return "[错误] 当前文件路径不能为空";
                if (string.IsNullOrWhiteSpace(newRelativePath))
                    return "[错误] 新文件路径不能为空";

                if (!SafePathHelper.TryResolveSafePath(relativePath, out var fullPath, out var error))
                    return $"[错误] 源路径: {error}";

                if (!SafePathHelper.TryResolveSafePath(newRelativePath, out var newFullPath, out var newError))
                    return $"[错误] 目标路径: {newError}";

                if (!File.Exists(fullPath))
                    return $"[错误] 源文件不存在: {relativePath}";

                if (File.Exists(newFullPath))
                    return $"[错误] 目标文件已存在: {newRelativePath}";

                var hash = FilePreviewStore.ComputeHash(File.ReadAllText(fullPath));
                var previewId = FilePreviewStore.CreateRenamePreview(relativePath, fullPath, newRelativePath, newFullPath, hash);

                var result = new
                {
                    previewId,
                    operation = "rename",
                    from = relativePath,
                    to = newRelativePath,
                    summary = $"重命名: {relativePath} → {newRelativePath}"
                };
                return JsonSerializer.Serialize(result, JsonHelper.Default);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorkspacePlugin] PreviewRenameFile 异常: {ex.Message}");
                return $"[错误] 重命名预览失败: {ex.Message}";
            }
        }

        [KernelFunction("ExecuteRenameFile")]
        [Description("直接执行重命名/移动文件（预览+确认一步完成，无需用户确认）。Agent/Plan 模式专用。")]
        public async Task<string> ExecuteRenameFileAsync(
            [Description("当前文件的相对路径")] string relativePath,
            [Description("新的文件相对路径")] string newRelativePath)
        {
            try
            {
                var previewResult = PreviewRenameFile(relativePath, newRelativePath);
                if (previewResult.Contains("[错误]"))
                    return previewResult;

                var previewId = ExtractPreviewId(previewResult);
                if (string.IsNullOrEmpty(previewId))
                    return "[错误] 无法提取 previewId";

                var confirmResult = await ConfirmFileEditAsync(previewId).ConfigureAwait(false);
                TM.App.Log($"[WorkspacePlugin] ExecuteRenameFile 完成: {relativePath} → {newRelativePath}");
                return confirmResult;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[WorkspacePlugin] ExecuteRenameFile 异常: {ex.Message}");
                return $"[错误] 执行失败: {ex.Message}";
            }
        }

        #endregion

        #region 写侧工具方法

        private static string BuildReplaceDiff(string oldText, string newText, int position, string fullContent)
        {
            var lineNum = 1;
            for (int i = 0; i < position && i < fullContent.Length; i++)
            {
                if (fullContent[i] == '\n') lineNum++;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"位置: 行 {lineNum}");
            var oldPreview = oldText.Length > 200 ? oldText[..200] + "..." : oldText;
            var newPreview = newText.Length > 200 ? newText[..200] + "..." : newText;
            sb.AppendLine($"- {oldPreview}");
            sb.AppendLine($"+ {newPreview}");
            return sb.ToString().TrimEnd();
        }

        private static string? ExtractPreviewId(string jsonResult)
        {
            try
            {
                using var doc = JsonDocument.Parse(jsonResult);
                if (doc.RootElement.TryGetProperty("previewId", out var el))
                    return el.GetString();
            }
            catch { }
            return null;
        }

        private static void EnsureDirectory(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        #endregion
    }
}
