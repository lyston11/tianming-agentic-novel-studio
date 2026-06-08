using System;
using System.IO;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class ContentGenerationCallback
    {
        internal const int SummaryMaxChars = 1500;

        private string BuildStructuredSummary(string content, ChapterChanges changes,
            System.Collections.Generic.IReadOnlyDictionary<string, string>? nameMap = null)
        {
            string R(string id) => (!string.IsNullOrWhiteSpace(id) && nameMap != null && nameMap.TryGetValue(id, out var n)) ? n : id;

            var sb = new System.Text.StringBuilder();

            sb.AppendLine(ExtractSummary(content, 200));

            foreach (var c in changes.CharacterStateChanges ?? new())
            {
                if (!string.IsNullOrWhiteSpace(c.KeyEvent))
                    sb.AppendLine($"[角色]{R(c.CharacterId)}: {c.KeyEvent}");
            }
            foreach (var c in changes.ConflictProgress ?? new())
            {
                if (!string.IsNullOrWhiteSpace(c.Event))
                    sb.AppendLine($"[冲突]{R(c.ConflictId)}: {c.Event}→{c.NewStatus}");
            }
            foreach (var p in changes.NewPlotPoints ?? new())
            {
                if (!string.IsNullOrWhiteSpace(p.Context))
                    sb.AppendLine($"[情节]{p.Context}");
            }
            foreach (var f in changes.ForeshadowingActions ?? new())
            {
                if (!string.IsNullOrWhiteSpace(f.ForeshadowId))
                    sb.AppendLine($"[伏笔]{R(f.ForeshadowId)}: {f.Action}");
            }
            foreach (var l in changes.LocationStateChanges ?? new())
            {
                if (!string.IsNullOrWhiteSpace(l.Event))
                    sb.AppendLine($"[地点]{R(l.LocationId)}: {l.Event}→{l.NewStatus}");
            }
            foreach (var fa in changes.FactionStateChanges ?? new())
            {
                if (!string.IsNullOrWhiteSpace(fa.Event))
                    sb.AppendLine($"[势力]{R(fa.FactionId)}: {fa.Event}→{fa.NewStatus}");
            }
            if (changes.TimeProgression != null && !string.IsNullOrWhiteSpace(changes.TimeProgression.TimePeriod))
            {
                sb.AppendLine($"[时间]{changes.TimeProgression.TimePeriod} 经过{changes.TimeProgression.ElapsedTime}");
            }
            foreach (var m in changes.CharacterMovements ?? new())
            {
                if (!string.IsNullOrWhiteSpace(m.ToLocation))
                    sb.AppendLine($"[移动]{R(m.CharacterId)}: {R(m.FromLocation)}→{R(m.ToLocation)}");
            }
            foreach (var item in changes.ItemTransfers ?? new())
            {
                if (!string.IsNullOrWhiteSpace(item.Event))
                    sb.AppendLine($"[物品]{item.ItemName}: {item.Event} ({R(item.FromHolder)}→{R(item.ToHolder)})");
            }
            foreach (var sr in changes.SecretRevealChanges ?? new())
            {
                if (!string.IsNullOrWhiteSpace(sr.KeyEvent))
                    sb.AppendLine($"[秘密]{R(sr.SecretId)}: {sr.Method} {sr.KeyEvent}");
            }
            foreach (var pc in changes.PledgeConstraintChanges ?? new())
            {
                if (!string.IsNullOrWhiteSpace(pc.KeyEvent))
                    sb.AppendLine($"[承诺]{R(pc.PledgeId)}: {pc.Action} {pc.KeyEvent}");
            }
            foreach (var dc in changes.DeadlineConstraintChanges ?? new())
            {
                if (!string.IsNullOrWhiteSpace(dc.KeyEvent))
                    sb.AppendLine($"[时限]{R(dc.DeadlineId)}: {dc.Action} {dc.KeyEvent}");
            }

            var result = sb.ToString().Trim();

            if (result.Length > SummaryMaxChars)
            {
                var lastNewline = result.LastIndexOf('\n', SummaryMaxChars);
                result = lastNewline > SummaryMaxChars / 3
                    ? result.Substring(0, lastNewline).TrimEnd() + "\n[...摘要已截断]"
                    : result.Substring(0, SummaryMaxChars) + "...";
                TM.App.Log($"[ContentCallback] 摘要超长截断: {sb.Length} → {result.Length} chars");
            }

            return result;
        }

        private string ExtractSummary(string content, int maxLength = 500)
        {
            if (string.IsNullOrEmpty(content)) return string.Empty;

            var cleaned = content.Replace("\r\n", " ").Replace("\n", " ").Trim();
            if (cleaned.Length <= maxLength) return cleaned;

            var cutRegion = cleaned.Substring(0, maxLength);
            var lastSentenceEnd = cutRegion.LastIndexOfAny(new[] { '。', '！', '？', '…', '"' });
            if (lastSentenceEnd > maxLength / 3)
            {
                return cutRegion.Substring(0, lastSentenceEnd + 1) + "……";
            }

            return cutRegion + "……";
        }

        private async Task UpdateChapterSummaryAsync(string chapterId, string summary)
        {
            const int maxRetries = 2;
            for (int _attempt = 1; _attempt <= maxRetries; _attempt++)
            {
                try
                {
                    await _summaryStore.SetSummaryAsync(chapterId, summary).ConfigureAwait(false);
                    TM.App.Log($"[ContentCallback] 已更新章节摘要: {chapterId}");
                    return;
                }
                catch (Exception ex)
                {
                    if (_attempt < maxRetries)
                    {
                        TM.App.Log($"[ContentCallback] 摘要写入第{_attempt}次失败，重试中: {ex.Message}");
                        await Task.Delay(100).ConfigureAwait(false);
                    }
                    else
                    {
                        TM.App.Log($"[ContentCallback] [!] 摘要写入失败，将由启动对账修复: {chapterId}: {ex.Message}");
                    }
                }
            }
        }

        private static bool VerifyCommitSync(string chapterId)
        {
            try
            {
                var projectName = StoragePathHelper.CurrentProjectName;
                var guidesDir = Path.Combine(StoragePathHelper.GetStorageRoot(), "Projects", projectName, "Config", "guides");

                var stagingDir = Path.Combine(guidesDir, ".flush_staging");
                if (Directory.Exists(stagingDir) && Directory.GetFiles(stagingDir, "*.json").Length > 0)
                {
                    TM.App.Log($"[ContentCallback] VerifyCommit FAIL: staging 目录仍有未提交文件");
                    return false;
                }

                var (vol, _) = ChapterParserHelper.ParseChapterIdOrDefault(chapterId);
                var charGuideFile = Path.Combine(guidesDir, GuideManager.GetVolumeFileName("character_state_guide.json", vol));
                if (File.Exists(charGuideFile))
                {
                    var fi = new FileInfo(charGuideFile);
                    if (fi.Length == 0)
                    {
                        TM.App.Log($"[ContentCallback] VerifyCommit FAIL: {Path.GetFileName(charGuideFile)} 文件为空");
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentCallback] VerifyCommit err: {ex.Message}");
                return false;
            }
        }
    }
}
