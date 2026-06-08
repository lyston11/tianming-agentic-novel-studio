using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Services.Framework.AI.NovelAgent.Services
{
    public sealed class CanonMaintenanceService
    {
        private readonly StoryBibleService _storyBibleService;

        public CanonMaintenanceService(StoryBibleService storyBibleService)
        {
            _storyBibleService = storyBibleService;
        }

        public async Task<CanonMaintenanceResult> ImportProposedEntriesFromReviewAsync(
            NovelAgentRun run,
            CancellationToken ct = default)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));

            var review = run.PostGenerationReview;
            if (review == null || review.ProposedCanonEntries.Count == 0)
            {
                return new CanonMaintenanceResult
                {
                    Success = true,
                    Message = "当前 Agent Run 没有待导入的 Proposed 设定。",
                    Run = run,
                    Document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false)
                };
            }

            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var imported = new List<CanonLedgerEntry>();
            var conflicts = new List<CanonLedgerEntry>();

            foreach (var proposed in review.ProposedCanonEntries)
            {
                var entry = CloneEntry(proposed);
                entry.Status = CanonLedgerEntryStatus.Proposed;
                entry.SourceRunId = string.IsNullOrWhiteSpace(entry.SourceRunId) ? run.RunId : entry.SourceRunId;
                entry.SourceChapterId = string.IsNullOrWhiteSpace(entry.SourceChapterId) ? run.TargetChapterId : entry.SourceChapterId;

                var conflictReason = BuildConflictCheck(entry, document.CanonLedger);
                entry.ConflictCheck = conflictReason;
                if (conflictReason.Contains("可能冲突", StringComparison.OrdinalIgnoreCase))
                    conflicts.Add(entry);

                if (document.CanonLedger.Any(e =>
                        string.Equals(e.Title, entry.Title, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(e.SourceChapterId, entry.SourceChapterId, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var result = await _storyBibleService.AddLedgerEntryAsync(entry, confirmed: false, ct)
                    .ConfigureAwait(false);
                if (result.Success)
                {
                    imported.Add(entry);
                    document = result.Document ?? await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
                }
            }

            return new CanonMaintenanceResult
            {
                Success = true,
                Message = imported.Count == 0
                    ? "没有新的 Proposed 设定需要导入。"
                    : $"已导入 {imported.Count} 条 Proposed 设定，其中 {conflicts.Count} 条存在可能冲突。",
                ImportedEntries = imported,
                ConflictEntries = conflicts,
                Run = run,
                Document = document
            };
        }

        public async Task<CanonMaintenanceResult> PromoteProposedEntriesAsync(
            NovelAgentRun run,
            string entryIds = "",
            bool confirmed = false,
            CancellationToken ct = default)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));

            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var ids = SplitIds(entryIds);
            var proposed = document.CanonLedger
                .Where(e => e.Status == CanonLedgerEntryStatus.Proposed)
                .Where(e => ids.Count > 0
                    ? ids.Any(id => string.Equals(id, e.Id, StringComparison.OrdinalIgnoreCase))
                    : string.Equals(e.SourceRunId, run.RunId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (proposed.Count == 0)
            {
                return new CanonMaintenanceResult
                {
                    Success = true,
                    Message = "没有匹配到可升级的 Proposed 设定。",
                    Run = run,
                    Document = document
                };
            }

            var promoted = new List<CanonLedgerEntry>();
            var conflicts = proposed
                .Where(e => e.ConflictCheck.Contains("可能冲突", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var entry in proposed)
            {
                var result = await _storyBibleService.UpdateLedgerEntryStatusAsync(
                    entry.Id,
                    CanonLedgerEntryStatus.Canon,
                    string.IsNullOrWhiteSpace(entry.ConflictCheck)
                        ? "Autopilot 升级为 Canon。"
                        : entry.ConflictCheck + "；Autopilot 升级为 Canon。",
                    confirmed: true,
                    ct).ConfigureAwait(false);

                if (result.Success)
                {
                    entry.Status = CanonLedgerEntryStatus.Canon;
                    promoted.Add(entry);
                    document = result.Document ?? await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
                }
            }

            return new CanonMaintenanceResult
            {
                Success = true,
                Message = $"已将 {promoted.Count} 条 Proposed 设定升级为 Canon。可能冲突条目：{conflicts.Count}。",
                PromotedEntries = promoted,
                ConflictEntries = conflicts,
                Run = run,
                Document = document
            };
        }

        public async Task<CanonMaintenanceResult> RejectProposedEntriesAsync(
            NovelAgentRun run,
            string entryIds = "",
            string reason = "",
            bool confirmed = false,
            CancellationToken ct = default)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));

            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var ids = SplitIds(entryIds);
            var proposed = document.CanonLedger
                .Where(e => e.Status == CanonLedgerEntryStatus.Proposed)
                .Where(e => ids.Count > 0
                    ? ids.Any(id => string.Equals(id, e.Id, StringComparison.OrdinalIgnoreCase))
                    : string.Equals(e.SourceRunId, run.RunId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (proposed.Count == 0)
            {
                return new CanonMaintenanceResult
                {
                    Success = true,
                    Message = "没有匹配到可拒绝的 Proposed 设定。",
                    Run = run,
                    Document = document
                };
            }

            var rejected = new List<CanonLedgerEntry>();
            var normalizedReason = string.IsNullOrWhiteSpace(reason)
                ? "Autopilot 标记该 Proposed 设定不进入 Canon。"
                : reason.Trim();

            foreach (var entry in proposed)
            {
                var result = await _storyBibleService.UpdateLedgerEntryStatusAsync(
                    entry.Id,
                    CanonLedgerEntryStatus.Rejected,
                    normalizedReason,
                    confirmed: false,
                    ct).ConfigureAwait(false);

                if (result.Success)
                {
                    entry.Status = CanonLedgerEntryStatus.Rejected;
                    entry.ConflictCheck = normalizedReason;
                    rejected.Add(entry);
                    document = result.Document ?? await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
                }
            }

            return new CanonMaintenanceResult
            {
                Success = true,
                Message = $"已将 {rejected.Count} 条 Proposed 设定标记为 Rejected。",
                RejectedEntries = rejected,
                Run = run,
                Document = document
            };
        }

        private static CanonLedgerEntry CloneEntry(CanonLedgerEntry entry)
        {
            return new CanonLedgerEntry
            {
                Type = entry.Type,
                Status = entry.Status,
                Title = entry.Title,
                Content = entry.Content,
                Rationale = entry.Rationale,
                ImpactScope = entry.ImpactScope,
                ConflictCheck = entry.ConflictCheck,
                SourceRunId = entry.SourceRunId,
                SourceChapterId = entry.SourceChapterId
            };
        }

        private static string BuildConflictCheck(CanonLedgerEntry entry, IReadOnlyList<CanonLedgerEntry> ledger)
        {
            var canon = ledger
                .Where(e => e.Status == CanonLedgerEntryStatus.Canon)
                .ToList();
            var titleHit = canon.FirstOrDefault(e =>
                string.Equals(e.Title, entry.Title, StringComparison.OrdinalIgnoreCase));
            if (titleHit != null)
                return $"可能冲突：与既有 Canon「{titleHit.Title}」标题相同，请人工确认语义是否重复或矛盾。";

            var contentTokens = Tokenize(entry.Content + " " + entry.Title);
            var similar = canon.FirstOrDefault(e =>
            {
                var existingTokens = Tokenize(e.Content + " " + e.Title);
                var overlap = existingTokens
                    .Intersect(contentTokens, StringComparer.OrdinalIgnoreCase)
                    .Count();
                return overlap >= 4;
            });
            if (similar != null)
                return $"可能冲突：与既有 Canon「{similar.Title}」存在较多关键词重合，请人工确认边界。";

            return "自动冲突检查：未发现明显 Canon 冲突。";
        }

        private static List<string> Tokenize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            return text
                .Split(new[] { ' ', '\t', '\r', '\n', '，', '。', '、', '；', ';', ',', '.', '：', ':', '！', '？', '(', ')', '（', '）' },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => t.Length >= 2)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToList();
        }

        private static List<string> SplitIds(string ids)
        {
            if (string.IsNullOrWhiteSpace(ids)) return new List<string>();
            return ids
                .Split(new[] { ',', '，', ';', '；', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
