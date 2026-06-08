using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Services.Framework.AI.NovelAgent.Services
{
    public sealed class ForeshadowLedgerService
    {
        private readonly StoryBibleService _storyBibleService;

        public ForeshadowLedgerService(StoryBibleService storyBibleService)
        {
            _storyBibleService = storyBibleService;
        }

        public async Task<ForeshadowMaintenanceResult> ImportProposedEntriesFromReviewAsync(
            NovelAgentRun run,
            CancellationToken ct = default)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));

            var review = run.PostGenerationReview;
            if (review == null || review.ProposedForeshadowEntries.Count == 0)
            {
                return new ForeshadowMaintenanceResult
                {
                    Success = true,
                    Message = "当前 Agent Run 没有待导入的伏笔变化。",
                    Run = run,
                    Document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false)
                };
            }

            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var imported = new List<ForeshadowLedgerEntry>();
            var updated = new List<ForeshadowLedgerEntry>();
            var highRisk = new List<ForeshadowLedgerEntry>();

            foreach (var proposed in review.ProposedForeshadowEntries)
            {
                var entry = Clone(proposed);
                entry.SourceRunId = string.IsNullOrWhiteSpace(entry.SourceRunId) ? run.RunId : entry.SourceRunId;
                entry.SourceChapterId = string.IsNullOrWhiteSpace(entry.SourceChapterId) ? run.TargetChapterId : entry.SourceChapterId;

                var existing = FindExisting(document.ForeshadowLedger, entry);
                if (IsHighRisk(entry.Status))
                {
                    highRisk.Add(entry);
                    if (existing == null)
                    {
                        entry.Status = ForeshadowLedgerStatus.Proposed;
                        entry.Notes.Add("复盘提出高风险伏笔状态变化，已先作为 Proposed 导入，后续由 Autopilot 应用正式状态。");
                        var addResult = await _storyBibleService.AddForeshadowEntryAsync(entry, confirmed: false, ct)
                            .ConfigureAwait(false);
                        if (addResult.Success)
                        {
                            imported.Add(entry);
                            document = addResult.Document ?? await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
                        }
                    }

                    continue;
                }

                if (existing == null)
                {
                    var addResult = await _storyBibleService.AddForeshadowEntryAsync(entry, confirmed: false, ct)
                        .ConfigureAwait(false);
                    if (addResult.Success)
                    {
                        imported.Add(entry);
                        document = addResult.Document ?? await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
                    }
                    continue;
                }

                var updateResult = await _storyBibleService.UpdateForeshadowEntryStatusAsync(
                    existing.Id,
                    entry.Status,
                    run.TargetChapterId,
                    BuildUpdateNote(entry),
                    confirmed: false,
                    ct).ConfigureAwait(false);
                if (updateResult.Success)
                {
                    updated.AddRange(updateResult.UpdatedEntries);
                    document = updateResult.Document ?? await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
                }
            }

            return new ForeshadowMaintenanceResult
            {
                Success = true,
                RiskLevel = highRisk.Count > 0 ? NovelToolRiskLevel.High : NovelToolRiskLevel.Medium,
                Message = highRisk.Count > 0
                    ? $"已导入/更新 {imported.Count + updated.Count} 条低风险伏笔变化；另有 {highRisk.Count} 条高风险回收/废弃动作进入自动应用队列。"
                    : imported.Count + updated.Count == 0
                        ? "没有新的伏笔变化需要导入。"
                        : $"已导入 {imported.Count} 条新伏笔，更新 {updated.Count} 条既有伏笔。",
                ImportedEntries = imported,
                UpdatedEntries = updated,
                ConflictEntries = highRisk,
                Run = run,
                Document = document
            };
        }

        public async Task<ForeshadowMaintenanceResult> ConfirmForeshadowStatusFromRunAsync(
            NovelAgentRun run,
            string entryIds = "",
            bool confirmed = false,
            CancellationToken ct = default)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));

            var review = run.PostGenerationReview;
            var ids = SplitIds(entryIds);
            var highRisk = (review?.ProposedForeshadowEntries ?? new List<ForeshadowLedgerEntry>())
                .Where(e => IsHighRisk(e.Status))
                .Where(e => ids.Count == 0 || ids.Any(id => string.Equals(id, e.Id, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (highRisk.Count == 0)
            {
                return new ForeshadowMaintenanceResult
                {
                    Success = true,
                    Message = "没有匹配到可应用的高风险伏笔状态变化。",
                    Run = run,
                    Document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false)
                };
            }

            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var updated = new List<ForeshadowLedgerEntry>();
            foreach (var proposed in highRisk)
            {
                var existing = FindExisting(document.ForeshadowLedger, proposed);
                if (existing == null)
                {
                    var addResult = await _storyBibleService.AddForeshadowEntryAsync(proposed, confirmed: true, ct)
                        .ConfigureAwait(false);
                    if (addResult.Success)
                    {
                        updated.AddRange(addResult.ImportedEntries);
                        document = addResult.Document ?? await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
                    }
                    continue;
                }

                var updateResult = await _storyBibleService.UpdateForeshadowEntryStatusAsync(
                    existing.Id,
                    proposed.Status,
                    run.TargetChapterId,
                    BuildUpdateNote(proposed),
                    confirmed: true,
                    ct).ConfigureAwait(false);
                if (updateResult.Success)
                {
                    updated.AddRange(updateResult.UpdatedEntries);
                    document = updateResult.Document ?? await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
                }
            }

            return new ForeshadowMaintenanceResult
            {
                Success = true,
                Message = $"已应用并更新 {updated.Count} 条高风险伏笔状态。",
                UpdatedEntries = updated,
                Run = run,
                Document = document
            };
        }

        private static ForeshadowLedgerEntry? FindExisting(
            IEnumerable<ForeshadowLedgerEntry> ledger,
            ForeshadowLedgerEntry proposed)
        {
            return ledger.FirstOrDefault(e =>
                    !string.IsNullOrWhiteSpace(proposed.Id)
                    && string.Equals(e.Id, proposed.Id, StringComparison.OrdinalIgnoreCase))
                ?? ledger.FirstOrDefault(e =>
                    string.Equals(e.Name, proposed.Name, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrWhiteSpace(proposed.SourceVolumeId)
                        || string.Equals(e.SourceVolumeId, proposed.SourceVolumeId, StringComparison.OrdinalIgnoreCase)))
                ?? ledger.FirstOrDefault(e =>
                    HasTokenOverlap(proposed.Name + " " + proposed.Setup, e.Name + " " + e.Setup));
        }

        private static string BuildUpdateNote(ForeshadowLedgerEntry entry)
        {
            var evidence = entry.Evidence.FirstOrDefault();
            return string.IsNullOrWhiteSpace(evidence)
                ? $"复盘导入伏笔状态：{entry.Status}。"
                : $"复盘导入伏笔状态：{entry.Status}；证据：{evidence}";
        }

        private static bool IsHighRisk(ForeshadowLedgerStatus status)
        {
            return status is ForeshadowLedgerStatus.PaidOff
                or ForeshadowLedgerStatus.Abandoned
                or ForeshadowLedgerStatus.Conflict;
        }

        private static ForeshadowLedgerEntry Clone(ForeshadowLedgerEntry entry)
        {
            return new ForeshadowLedgerEntry
            {
                Id = entry.Id,
                Name = entry.Name,
                Type = entry.Type,
                Status = entry.Status,
                Setup = entry.Setup,
                Payoff = entry.Payoff,
                SourceVolumeId = entry.SourceVolumeId,
                PlannedSetupChapterId = entry.PlannedSetupChapterId,
                PlannedPayoffChapterId = entry.PlannedPayoffChapterId,
                ActualSetupChapterIds = entry.ActualSetupChapterIds.ToList(),
                ActualReinforceChapterIds = entry.ActualReinforceChapterIds.ToList(),
                ActualPayoffChapterId = entry.ActualPayoffChapterId,
                Importance = entry.Importance,
                Evidence = entry.Evidence.ToList(),
                Notes = entry.Notes.ToList(),
                SourceRunId = entry.SourceRunId,
                SourceChapterId = entry.SourceChapterId,
                CreatedAt = entry.CreatedAt,
                UpdatedAt = entry.UpdatedAt
            };
        }

        private static bool HasTokenOverlap(string left, string right)
        {
            var leftTokens = Tokenize(left);
            var rightTokens = Tokenize(right);
            if (leftTokens.Count == 0 || rightTokens.Count == 0) return false;
            return leftTokens.Intersect(rightTokens, StringComparer.OrdinalIgnoreCase).Count() >= 2;
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
            return ids.Split(new[] { ',', '，', ';', '；', '|', '\n', '\r' },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
