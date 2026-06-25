using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Services.Framework.AI.NovelAgent.Services
{
    public sealed class CharacterLedgerService
    {
        private readonly StoryBibleService _storyBibleService;

        public CharacterLedgerService(StoryBibleService storyBibleService)
        {
            _storyBibleService = storyBibleService;
        }

        public async Task<CharacterMaintenanceResult> ImportProposedEntriesFromReviewAsync(
            NovelAgentRun run,
            CancellationToken ct = default)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));

            var review = run.PostGenerationReview;
            if (review == null || review.ProposedCharacterEntries.Count == 0)
            {
                return new CharacterMaintenanceResult
                {
                    Success = true,
                    Message = "当前 Agent Run 没有待导入的角色状态变化。",
                    Run = run,
                    Document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false)
                };
            }

            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var imported = new List<CharacterLedgerEntry>();
            var updated = new List<CharacterLedgerEntry>();
            var highRisk = new List<CharacterLedgerEntry>();

            foreach (var proposed in review.ProposedCharacterEntries)
            {
                var entry = Clone(proposed);
                entry.SourceRunId = string.IsNullOrWhiteSpace(entry.SourceRunId) ? run.RunId : entry.SourceRunId;
                entry.SourceChapterId = string.IsNullOrWhiteSpace(entry.SourceChapterId) ? run.TargetChapterId : entry.SourceChapterId;

                var existing = FindExisting(document.CharacterLedger, entry);
                if (IsHighRisk(entry.Status))
                {
                    highRisk.Add(entry);
                    if (existing == null)
                    {
                        entry.Status = CharacterLedgerStatus.Proposed;
                        entry.Notes.Add("复盘提出高风险角色状态变化，已先作为 Proposed 导入，后续由 Agent 应用正式状态。");
                        var addResult = await _storyBibleService.AddCharacterEntryAsync(entry, confirmed: false, ct)
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
                    var addResult = await _storyBibleService.AddCharacterEntryAsync(entry, confirmed: false, ct)
                        .ConfigureAwait(false);
                    if (addResult.Success)
                    {
                        imported.Add(entry);
                        document = addResult.Document ?? await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
                    }
                    continue;
                }

                var updateResult = await _storyBibleService.UpdateCharacterEntryStatusAsync(
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

            return new CharacterMaintenanceResult
            {
                Success = true,
                RiskLevel = highRisk.Count > 0 ? NovelToolRiskLevel.High : NovelToolRiskLevel.Medium,
                Message = highRisk.Count > 0
                    ? $"已导入/更新 {imported.Count + updated.Count} 条低风险角色变化；另有 {highRisk.Count} 条高风险角色状态变化进入自动应用队列。"
                    : imported.Count + updated.Count == 0
                        ? "没有新的角色状态变化需要导入。"
                        : $"已导入 {imported.Count} 条新角色状态，更新 {updated.Count} 条既有角色状态。",
                ImportedEntries = imported,
                UpdatedEntries = updated,
                ConflictEntries = highRisk,
                Run = run,
                Document = document
            };
        }

        public async Task<CharacterMaintenanceResult> ConfirmCharacterStatusFromRunAsync(
            NovelAgentRun run,
            string entryIds = "",
            bool confirmed = false,
            CancellationToken ct = default)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));

            var review = run.PostGenerationReview;
            var ids = SplitIds(entryIds);
            var highRisk = (review?.ProposedCharacterEntries ?? new List<CharacterLedgerEntry>())
                .Where(e => IsHighRisk(e.Status))
                .Where(e => ids.Count == 0 || ids.Any(id => string.Equals(id, e.Id, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (highRisk.Count == 0)
            {
                return new CharacterMaintenanceResult
                {
                    Success = true,
                    Message = "没有匹配到可应用的高风险角色状态变化。",
                    Run = run,
                    Document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false)
                };
            }

            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var updated = new List<CharacterLedgerEntry>();
            foreach (var proposed in highRisk)
            {
                var existing = FindExisting(document.CharacterLedger, proposed);
                if (existing == null)
                {
                    var addResult = await _storyBibleService.AddCharacterEntryAsync(proposed, confirmed: true, ct)
                        .ConfigureAwait(false);
                    if (addResult.Success)
                    {
                        updated.AddRange(addResult.ImportedEntries);
                        document = addResult.Document ?? await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
                    }
                    continue;
                }

                var updateResult = await _storyBibleService.UpdateCharacterEntryStatusAsync(
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

            return new CharacterMaintenanceResult
            {
                Success = true,
                Message = $"已应用并更新 {updated.Count} 条高风险角色状态。",
                UpdatedEntries = updated,
                Run = run,
                Document = document
            };
        }

        private static CharacterLedgerEntry? FindExisting(
            IEnumerable<CharacterLedgerEntry> ledger,
            CharacterLedgerEntry proposed)
        {
            return ledger.FirstOrDefault(e =>
                    !string.IsNullOrWhiteSpace(proposed.Id)
                    && string.Equals(e.Id, proposed.Id, StringComparison.OrdinalIgnoreCase))
                ?? ledger.FirstOrDefault(e =>
                    string.Equals(e.CharacterName, proposed.CharacterName, StringComparison.OrdinalIgnoreCase)
                    && e.Type == proposed.Type
                    && (string.IsNullOrWhiteSpace(proposed.Relationship.TargetCharacter)
                        || string.Equals(e.Relationship.TargetCharacter, proposed.Relationship.TargetCharacter, StringComparison.OrdinalIgnoreCase)))
                ?? ledger.FirstOrDefault(e =>
                    string.Equals(e.CharacterName, proposed.CharacterName, StringComparison.OrdinalIgnoreCase)
                    && HasTokenOverlap(proposed.Summary + " " + proposed.CurrentGoal + " " + proposed.CurrentIntent,
                        e.Summary + " " + e.CurrentGoal + " " + e.CurrentIntent));
        }

        private static string BuildUpdateNote(CharacterLedgerEntry entry)
        {
            var evidence = entry.Evidence.FirstOrDefault();
            return string.IsNullOrWhiteSpace(evidence)
                ? $"复盘导入角色状态：{entry.Status}。"
                : $"复盘导入角色状态：{entry.Status}；证据：{evidence}";
        }

        private static bool IsHighRisk(CharacterLedgerStatus status)
        {
            return status is CharacterLedgerStatus.SecretRevealed
                or CharacterLedgerStatus.RelationshipReversed
                or CharacterLedgerStatus.AbilityRuleChanged
                or CharacterLedgerStatus.IdentityRewritten
                or CharacterLedgerStatus.LeftStage
                or CharacterLedgerStatus.Dead
                or CharacterLedgerStatus.Conflict
                or CharacterLedgerStatus.Rejected;
        }

        private static CharacterLedgerEntry Clone(CharacterLedgerEntry entry)
        {
            return new CharacterLedgerEntry
            {
                Id = entry.Id,
                CharacterName = entry.CharacterName,
                Role = entry.Role,
                Type = entry.Type,
                Status = entry.Status,
                Summary = entry.Summary,
                CurrentGoal = entry.CurrentGoal,
                CurrentIntent = entry.CurrentIntent,
                NextPressure = entry.NextPressure,
                Secret = new CharacterSecretState
                {
                    Content = entry.Secret.Content,
                    Status = entry.Secret.Status,
                    KnownBy = entry.Secret.KnownBy.ToList()
                },
                Relationship = new CharacterRelationshipState
                {
                    TargetCharacter = entry.Relationship.TargetCharacter,
                    Status = entry.Relationship.Status,
                    Tension = entry.Relationship.Tension,
                    Change = entry.Relationship.Change
                },
                AbilityCost = new CharacterAbilityCostState
                {
                    Ability = entry.AbilityCost.Ability,
                    LevelOrBoundary = entry.AbilityCost.LevelOrBoundary,
                    Cost = entry.AbilityCost.Cost,
                    Debt = entry.AbilityCost.Debt,
                    Limitation = entry.AbilityCost.Limitation
                },
                Psychology = new CharacterPsychologyState
                {
                    Emotion = entry.Psychology.Emotion,
                    Wound = entry.Psychology.Wound,
                    CopingStrategy = entry.Psychology.CopingStrategy,
                    StressLevel = entry.Psychology.StressLevel
                },
                BeliefShift = entry.BeliefShift,
                IdentityState = entry.IdentityState,
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
                .Split(new[] { ' ', '\t', '\r', '\n', '，', '。', '、', '；', ';', ',', '.', '：', ':', '！', '？', '(', ')', '（', '）', '/', '\\' },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => t.Length >= 2)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(24)
                .ToList();
        }

        private static List<string> SplitIds(string values)
        {
            if (string.IsNullOrWhiteSpace(values))
                return new List<string>();

            return values
                .Split(new[] { ',', '，', ';', '；', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
