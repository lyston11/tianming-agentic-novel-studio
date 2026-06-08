using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class ContentEditPlugin
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        #region KernelFunction

        [KernelFunction("ReadChapterContent")]
        [Description("读取指定章节的完整正文内容。返回章节正文文本，不存在时返回错误提示。")]
        public async Task<string> ReadChapterContentAsync(
            [Description("章节ID，如 vol1_ch3")] string chapterId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(chapterId))
                    return "[错误] 章节ID不能为空";

                var contentService = ServiceLocator.Get<GeneratedContentService>();
                if (!contentService.ChapterExists(chapterId))
                    return $"[错误] 章节 {chapterId} 不存在";

                var content = await contentService.GetChapterAsync(chapterId).ConfigureAwait(false);
                if (string.IsNullOrEmpty(content))
                    return $"[错误] 章节 {chapterId} 内容为空";

                var wordCount = WordCountHelper.CountRaw(content);
                TM.App.Log($"[ContentEditPlugin] ReadChapterContent: {chapterId}, {wordCount}字");
                return content;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentEditPlugin] ReadChapterContent 异常: {ex.Message}");
                return $"[错误] 读取失败: {ex.Message}";
            }
        }

        [KernelFunction("GetChapterEditContext")]
        [Description("获取章节的编辑约束上下文：涉及的实体列表、前章摘要、世界观约束、CHANGES协议说明。编辑正文前必须先调用此方法了解约束。")]
        public async Task<string> GetChapterEditContextAsync(
            [Description("章节ID，如 vol1_ch3")] string chapterId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(chapterId))
                    return "[错误] 章节ID不能为空";

                var contentService = ServiceLocator.Get<GeneratedContentService>();
                if (!contentService.ChapterExists(chapterId))
                    return $"[错误] 章节 {chapterId} 不存在";

                var guideService = ServiceLocator.Get<GuideContextService>();
                var contentGuide = await guideService.GetContentGuideAsync().ConfigureAwait(false);

                var result = new StringBuilder();
                result.AppendLine($"=== 章节 {chapterId} 编辑约束 ===");

                var title = await guideService.GetChapterTitleAsync(chapterId).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(title))
                    result.AppendLine($"标题: {title}");

                if (contentGuide?.Chapters != null
                    && contentGuide.Chapters.TryGetValue(chapterId, out var entry)
                    && entry?.ContextIds != null)
                {
                    var ctx = entry.ContextIds;
                    result.AppendLine("\n涉及的实体（编辑时不能引入此列表之外的新实体）:");

                    if (ctx.Characters?.Count > 0)
                        result.AppendLine($"  角色: {string.Join(", ", ctx.Characters)}");
                    if (ctx.Locations?.Count > 0)
                        result.AppendLine($"  地点: {string.Join(", ", ctx.Locations)}");
                    if (ctx.Factions?.Count > 0)
                        result.AppendLine($"  势力: {string.Join(", ", ctx.Factions)}");
                    if (ctx.PlotRules?.Count > 0)
                        result.AppendLine($"  剧情规则: {string.Join(", ", ctx.PlotRules)}");
                    if (ctx.WorldRuleIds?.Count > 0)
                        result.AppendLine($"  世界观规则: {string.Join(", ", ctx.WorldRuleIds)}");
                }
                else
                {
                    result.AppendLine("\n[注意] 未找到该章节的 ContextIds（可能未打包），仅支持文本编辑（修改文笔/对话/错字）。");
                }

                var prevSummary = await GetPreviousChapterSummaryAsync(chapterId, guideService).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(prevSummary))
                    result.AppendLine($"\n前章摘要（不得与之矛盾）:\n  {prevSummary}");

                result.AppendLine("\n编辑规则:");
                result.AppendLine("  1. 纯文笔/对话/错字修改：直接提交修改后的正文，无需附带 CHANGES 块");
                result.AppendLine($"  2. 涉及剧情/角色状态/事件变更：必须在正文末尾用成对的 {ChapterChanges.ChangesXmlOpen} 与 {ChapterChanges.ChangesXmlClose} 标签包裹完整 JSON 变更摘要");
                result.AppendLine("  3. 禁止引入 ContextIds 之外的新实体");
                result.AppendLine("  4. 修改必须与前后章节情节保持一致");

                TM.App.Log($"[ContentEditPlugin] GetChapterEditContext: {chapterId}");
                return result.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentEditPlugin] GetChapterEditContext 异常: {ex.Message}");
                return $"[错误] 获取编辑上下文失败: {ex.Message}";
            }
        }

        [KernelFunction("PreviewContentEdit")]
        [Description("预校验章节编辑结果（不落盘）。检查内容合法性，返回校验结果和变更摘要。确认无误后调用 ConfirmContentEdit 落盘。")]
        public async Task<string> PreviewContentEditAsync(
            [Description("章节ID，如 vol1_ch3")] string chapterId,
            [Description("编辑后的完整章节正文")] string newContent)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(chapterId))
                    return "[错误] 章节ID不能为空";
                if (string.IsNullOrWhiteSpace(newContent))
                    return "[错误] 新内容不能为空";

                var contentService = ServiceLocator.Get<GeneratedContentService>();
                if (!contentService.ChapterExists(chapterId))
                    return $"[错误] 章节 {chapterId} 不存在，无法编辑";

                var oldContent = await contentService.GetChapterAsync(chapterId).ConfigureAwait(false);

                var oldWordCount = WordCountHelper.CountRaw(oldContent);
                var newWordCount = WordCountHelper.CountRaw(newContent);
                var result = new StringBuilder();
                result.AppendLine($"=== 预览: {chapterId} ===");
                result.AppendLine($"字数变化: {oldWordCount} → {newWordCount} ({(newWordCount >= oldWordCount ? "+" : "")}{newWordCount - oldWordCount})");

                var hasChanges = GenerationGate.FindChangesStartIndex(newContent) >= 0;

                if (hasChanges)
                {
                    result.AppendLine("检测到 CHANGES 块，执行全量校验...");

                    var guideService = ServiceLocator.Get<GuideContextService>();
                    var contentGuide = await guideService.GetContentGuideAsync().ConfigureAwait(false);

                    if (contentGuide?.Chapters == null
                        || !contentGuide.Chapters.TryGetValue(chapterId, out var entry)
                        || entry?.ContextIds == null)
                    {
                        result.AppendLine("[警告] 未找到 ContextIds，无法执行一致性校验。如果仅修改文笔，请去掉 CHANGES 块。");
                    }
                    else
                    {
                        var ctxValid = await guideService.ValidateContextIdsAsync(entry.ContextIds).ConfigureAwait(false);
                        if (!ctxValid.IsValid)
                        {
                            result.AppendLine($"[校验失败] ContextIds 不一致: {ctxValid.GetErrorSummary()}");
                            return result.ToString().TrimEnd();
                        }

                        var factSnapshot = await guideService.ExtractFactSnapshotForChapterAsync(chapterId, entry.ContextIds).ConfigureAwait(false);
                        var gate = ServiceLocator.Get<GenerationGate>();
                        var gateResult = await gate.ValidateAsync(chapterId, newContent, factSnapshot, contextIds: entry.ContextIds).ConfigureAwait(false);

                        if (!gateResult.Success)
                        {
                            var failures = gateResult.GetHumanReadableFailures(5);
                            result.AppendLine("[校验失败] 以下问题需要修正:");
                            foreach (var f in failures)
                                result.AppendLine($"  - {f}");
                            return result.ToString().TrimEnd();
                        }

                        result.AppendLine("[校验通过] CHANGES 协议和一致性校验均通过");
                    }
                }
                else
                {
                    result.AppendLine("无 CHANGES 块，将以文本编辑模式保存（保留原追踪数据）");
                }

                if (!string.IsNullOrEmpty(oldContent))
                {
                    var diffSummary = BuildSimpleDiffSummary(oldContent, newContent);
                    if (!string.IsNullOrEmpty(diffSummary))
                        result.AppendLine($"\n变更摘要:\n{diffSummary}");
                }

                result.AppendLine($"\n确认无误后，调用 ConfirmContentEdit(\"{chapterId}\") 落盘。");

                TM.App.Log($"[ContentEditPlugin] PreviewContentEdit: {chapterId}, hasChanges={hasChanges}");
                return result.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentEditPlugin] PreviewContentEdit 异常: {ex.Message}");
                return $"[错误] 预校验失败: {ex.Message}";
            }
        }

        [KernelFunction("ConfirmContentEdit")]
        [Description("确认落盘章节编辑（必须先调用 PreviewContentEdit 校验通过）。通过 OnExternalContentSavedAsync 原子写入，支持 CHANGES 追踪、WAL 崩溃恢复。")]
        public async Task<string> ConfirmContentEditAsync(
            [Description("章节ID，如 vol1_ch3")] string chapterId,
            [Description("编辑后的完整章节正文（与 PreviewContentEdit 传入的内容一致）")] string newContent)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(chapterId))
                    return "[错误] 章节ID不能为空";
                if (string.IsNullOrWhiteSpace(newContent))
                    return "[错误] 新内容不能为空";

                var contentService = ServiceLocator.Get<GeneratedContentService>();
                if (!contentService.ChapterExists(chapterId))
                    return $"[错误] 章节 {chapterId} 不存在";

                await contentService.SaveChapterAsync(chapterId, newContent).ConfigureAwait(false);

                var wordCount = WordCountHelper.CountRaw(newContent);
                var hasChanges = GenerationGate.FindChangesStartIndex(newContent) >= 0;

                TM.App.Log($"[ContentEditPlugin] ConfirmContentEdit 完成: {chapterId}, {wordCount}字, hasChanges={hasChanges}");

                try
                {
                    GlobalToast.Success("章节已更新", $"「{chapterId}」约 {wordCount} 字已落盘");
                }
                catch { }

                try
                {
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        ServiceLocator.Get<TM.Framework.UI.Workspace.Services.PanelCommunicationService>()
                            .PublishRefreshChapterList();
                    });
                }
                catch { }

                return $"✅ 章节 {chapterId} 已更新落盘（{wordCount}字）" +
                       (hasChanges ? "，CHANGES 追踪已同步更新。" : "，文本编辑模式，原追踪数据保留。");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentEditPlugin] ConfirmContentEdit 异常: {ex.Message}");
                return $"[错误] 落盘失败: {ex.Message}";
            }
        }

        [KernelFunction("ExecuteContentEdit")]
        [Description("直接执行章节正文编辑（预校验+落盘一步完成，无需用户确认）。Agent/Plan 模式专用。")]
        public async Task<string> ExecuteContentEditAsync(
            [Description("章节ID，如 vol1_ch3")] string chapterId,
            [Description("编辑后的完整章节正文")] string newContent)
        {
            try
            {
                var previewResult = await PreviewContentEditAsync(chapterId, newContent).ConfigureAwait(false);
                if (previewResult.Contains("[错误]") || previewResult.Contains("[校验失败]"))
                    return previewResult;

                var confirmResult = await ConfirmContentEditAsync(chapterId, newContent).ConfigureAwait(false);

                TM.App.Log($"[ContentEditPlugin] ExecuteContentEdit 完成: {chapterId}");
                return confirmResult;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentEditPlugin] ExecuteContentEdit 异常: {ex.Message}");
                return $"[错误] 执行失败: {ex.Message}";
            }
        }

        [KernelFunction("RollbackContentEdit")]
        [Description("取消章节编辑（丢弃 PreviewContentEdit 的预览结果，不做任何写入）。仅用于用户主动放弃编辑。")]
        public string RollbackContentEdit(
            [Description("章节ID，如 vol1_ch3")] string chapterId)
        {
            if (string.IsNullOrWhiteSpace(chapterId))
                return "[错误] 章节ID不能为空";

            TM.App.Log($"[ContentEditPlugin] RollbackContentEdit: {chapterId}");
            return $"✅ 已取消章节 {chapterId} 的编辑，未做任何写入。";
        }

        #endregion

        #region 内部方法

        private static async Task<string?> GetPreviousChapterSummaryAsync(string chapterId, IGuideContextService guideService)
        {
            try
            {
                var parsed = ChapterParserHelper.ParseChapterId(chapterId);
                if (!parsed.HasValue) return null;

                var vol = parsed.Value.volumeNumber;
                var ch = parsed.Value.chapterNumber;

                string prevChapterId;
                if (ch > 1)
                {
                    prevChapterId = ChapterParserHelper.BuildChapterId(vol, ch - 1);
                }
                else if (vol > 1)
                {
                    var prevVolMaxCh = await guideService.GetVolumeMaxChapterAsync(vol - 1).ConfigureAwait(false);
                    if (prevVolMaxCh <= 0) return null;
                    prevChapterId = ChapterParserHelper.BuildChapterId(vol - 1, prevVolMaxCh);
                }
                else
                {
                    return null;
                }

                return await guideService.GetChapterSummaryAsync(prevChapterId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentEditPlugin] 获取前章摘要失败: {ex.Message}");
                return null;
            }
        }

        private static string BuildSimpleDiffSummary(string oldContent, string newContent)
        {
            var oldLines = oldContent.Split('\n');
            var newLines = newContent.Split('\n');

            var added = 0;
            var removed = 0;
            var oldSet = new HashSet<string>(oldLines.Select(l => l.TrimEnd()));
            var newSet = new HashSet<string>(newLines.Select(l => l.TrimEnd()));

            foreach (var line in newLines)
            {
                if (!oldSet.Contains(line.TrimEnd()))
                    added++;
            }
            foreach (var line in oldLines)
            {
                if (!newSet.Contains(line.TrimEnd()))
                    removed++;
            }

            if (added == 0 && removed == 0)
                return "  无实质变更";

            var sb = new StringBuilder();
            sb.AppendLine($"  +{added} 行新增, -{removed} 行删除（共 {oldLines.Length} → {newLines.Length} 行）");
            return sb.ToString().TrimEnd();
        }

        #endregion
    }
}
