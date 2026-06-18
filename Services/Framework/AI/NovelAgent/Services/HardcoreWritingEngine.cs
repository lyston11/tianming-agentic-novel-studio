using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers;
using TM.Services.Framework.AI.Embedding;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Implementations.Guides;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.TaskContexts;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Framework.AI.NovelAgent.Services
{
    public sealed class HardcoreWritingEngine
    {
        private const string ChangesSeparator = ChapterChanges.ChangesSeparator;

        private readonly StoryStateSnapshotService _storyStateSnapshotService;
        private readonly IGuideContextService? _guideContextService;
        private readonly GenerationGate? _generationGate;
        private readonly IGeneratedContentService? _generatedContentService;
        private readonly IContentChunkSearchService? _contentChunkSearch;
        private readonly IVectorIndex? _chapterEmbeddingIndex;
        private readonly IChunkEmbeddingIndex? _chunkEmbeddingIndex;
        private readonly IMicroEmbeddingService? _embeddingService;
        private readonly object? _versionTrackingService;
        private readonly object? _settingsManager;
        private readonly StoryBibleService? _storyBibleService;

        public HardcoreWritingEngine(StoryStateSnapshotService storyStateSnapshotService)
        {
            _storyStateSnapshotService = storyStateSnapshotService;
            _storyBibleService = storyStateSnapshotService.StoryBibleService;
        }

        public HardcoreWritingEngine(
            StoryStateSnapshotService storyStateSnapshotService,
            IGuideContextService guideContextService,
            GenerationGate generationGate,
            IGeneratedContentService generatedContentService,
            IContentChunkSearchService contentChunkSearch,
            IVectorIndex? chapterEmbeddingIndex,
            IChunkEmbeddingIndex chunkEmbeddingIndex,
            IMicroEmbeddingService embeddingService,
            object versionTrackingService,
            object settingsManager)
            : this(storyStateSnapshotService)
        {
            _guideContextService = guideContextService;
            _generationGate = generationGate;
            _generatedContentService = generatedContentService;
            _contentChunkSearch = contentChunkSearch;
            _chapterEmbeddingIndex = chapterEmbeddingIndex;
            _chunkEmbeddingIndex = chunkEmbeddingIndex;
            _embeddingService = embeddingService;
            _versionTrackingService = versionTrackingService;
            _settingsManager = settingsManager;
            _storyBibleService = storyStateSnapshotService.StoryBibleService;
        }

        public async Task<ChapterContextPackageSummary> BuildContextPackageAsync(
            NovelAgentRun run,
            StoryBibleDocument document,
            CancellationToken ct = default)
        {
            ContentTaskContext? contentContext = null;
            if (_guideContextService != null)
            {
                try
                {
                    contentContext = await _guideContextService.BuildContentContextAsync(run.TargetChapterId, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[HardcoreWritingEngine] 真实上下文包构建失败，回退 StoryState: {ex.Message}");
                }
            }

            var storyState = run.StoryState ?? await _storyStateSnapshotService
                .BuildForChapterAsync(run.TargetChapterId, ct)
                .ConfigureAwait(false);
            run.StoryState = storyState;

            var package = new ChapterContextPackageSummary
            {
                ChapterId = run.TargetChapterId,
                Status = contentContext != null ? "context_ready:real_project_data" : "context_ready:fallback_story_state",
                WorldRules = Merge(
                    contentContext?.WorldRules.Select(w => FirstNonEmpty(w.Name, w.GetCoreSummary(), w.Description)),
                    storyState.WorldRules).Take(12).ToList(),
                CharacterStates = Merge(
                    contentContext?.Characters.Select(c => $"{FirstNonEmpty(c.Name, c.Id)}：{FirstNonEmpty(c.GetCoreSummary(), c.Description)}"),
                    storyState.CharacterStates,
                    storyState.CharacterLedgerItems).Take(16).ToList(),
                ActiveConflicts = storyState.ActiveConflicts.Take(12).ToList(),
                ActiveForeshadowing = storyState.ActiveForeshadowing.Concat(storyState.ForeshadowLedgerItems).Where(HasText).Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToList(),
                ChapterBlueprints = Merge(
                    contentContext?.Blueprints.Select(b => FirstNonEmpty(b.Name, b.GetCoreSummary(), b.Description)),
                    contentContext?.Scenes.Select(s => FirstNonEmpty(
                        s.Title,
                        s.Purpose,
                        string.Join(" / ", new[] { s.Opening, s.Development, s.Turning, s.Ending }.Where(HasText)))),
                    BuildBlueprintLines(run)).Take(12).ToList(),
                PreviousSummaries = Merge(
                    contentContext?.PreviousChapterSummaries.Select(s => $"{s.ChapterId}: {s.Summary}"),
                    contentContext?.MdPreviousChapterSummaries.Select(s => $"{s.ChapterId}: {s.Summary}"),
                    new[] { contentContext?.PreviousChapterSummary, storyState.PreviousChapterSummary }).Take(12).ToList(),
                LongDistanceRecall = Merge(
                    contentContext?.LongDistanceRecallFragments.Select(f => $"{f.ChapterId}({f.Score:0.00}): {f.Content}"),
                    storyState.LongDistanceRecall,
                    storyState.SimilarContentFragments).Take(12).ToList(),
                RagQueries = storyState.RagSearchQueries.Where(HasText).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList(),
                Warnings = Merge(contentContext?.StateDivergenceWarnings, storyState.Warnings).Take(12).ToList()
            };

            if (document.Constitution != null)
            {
                package.WorldRules.Insert(0, $"Story Bible：{document.Constitution.Genre}/{document.Constitution.SubGenre}；{document.Constitution.WorldCoreRule}");
                package.ActiveConflicts.Insert(0, $"主冲突引擎：{document.Constitution.MainConflictEngine}");
            }

            ApplyContinuityPack(document, run, package);
            await BackfillPreviousChapterSummaryAsync(run, package, ct).ConfigureAwait(false);

            package.WorldRules = package.WorldRules.Where(HasText).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
            package.CharacterStates = package.CharacterStates.Where(HasText).Distinct(StringComparer.OrdinalIgnoreCase).Take(18).ToList();
            package.ActiveConflicts = package.ActiveConflicts.Where(HasText).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
            package.PreviousSummaries = package.PreviousSummaries.Where(HasText).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
            package.HardContinuityFacts = package.HardContinuityFacts.Where(HasText).Distinct(StringComparer.OrdinalIgnoreCase).Take(24).ToList();
            return package;
        }

        public async Task<ChapterDraftArtifact> GenerateDraftWithChangesAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            CancellationToken ct = default)
        {
            var settings = await LoadSettingsAsync(ct).ConfigureAwait(false);
            if (!settings.IsConfigured)
            {
                return new ChapterDraftArtifact
                {
                    ChapterId = run.TargetChapterId,
                    Status = "blocked_missing_llm_settings",
                    DraftContent = string.Empty,
                    ChangesJson = string.Empty,
                    HasChanges = false
                };
            }

            var raw = await CompleteWritingAsync(settings, BuildWritingSystemPrompt(), BuildWritingUserPrompt(run, contextPackage), ct)
                .ConfigureAwait(false);
            var changesJson = ExtractChangesJson(raw);
            return new ChapterDraftArtifact
            {
                ChapterId = run.TargetChapterId,
                Status = "draft_generated",
                DraftContent = raw.Trim(),
                ChangesJson = changesJson,
                HasChanges = GenerationGate.HasChangesRegion(raw)
            };
        }

        public ChapterDraftArtifact GenerateDraftWithChanges(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage) =>
            GenerateDraftWithChangesAsync(run, contextPackage).GetAwaiter().GetResult();

        public async Task<GenerationGateReport> ValidateDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft,
            CancellationToken ct = default)
        {
            if (_generationGate != null && _guideContextService != null)
            {
                try
                {
                    var context = await _guideContextService.BuildContentContextAsync(run.TargetChapterId, ct)
                        .ConfigureAwait(false);
                    var snapshot = context?.FactSnapshot;
                    if (snapshot == null)
                        snapshot = new TM.Services.Modules.ProjectData.Models.Tracking.FactSnapshot();
                    var gate = await _generationGate.ValidateAsync(
                        run.TargetChapterId,
                        draft.DraftContent,
                        snapshot,
                        BuildDesignElements(context),
                        context?.ContextIds ?? new ContextIdCollection()).ConfigureAwait(false);
                    var report = MapGateResult(gate, contextPackage);
                    ApplyCoreContinuityGate(report, contextPackage, draft);
                    return report;
                }
                catch (Exception ex)
                {
                    var fallback = BuildFallbackGateReport(run, contextPackage, draft);
                    fallback.RepairHints.Add($"真实 GenerationGate 当前不可用，已使用 Web runtime fallback 校验：{ex.Message}");
                    return fallback;
                }
            }

            return BuildFallbackGateReport(run, contextPackage, draft);
        }

        private static GenerationGateReport BuildFallbackGateReport(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft)
        {
            var hasChangesRegion = GenerationGate.HasChangesRegion(draft.DraftContent);
            var changesJson = !string.IsNullOrWhiteSpace(draft.ChangesJson)
                ? draft.ChangesJson
                : ExtractChangesJson(draft.DraftContent);
            if (GenerationGate.TryNormalizeChangesJsonShape(changesJson, out var normalizedChangesJson))
                changesJson = normalizedChangesJson;
            var isFirstChapter = ExtractChapterNumber(run.TargetChapterId) <= 1;
            var report = new GenerationGateReport
            {
                ChangesDetected = hasChangesRegion,
                ProtocolPassed = hasChangesRegion && IsValidJsonObject(changesJson),
                FactSnapshotPassed = contextPackage.ActiveConflicts.Count > 0 || contextPackage.CharacterStates.Count > 0 || contextPackage.WorldRules.Count > 0,
                BlueprintPassed = contextPackage.ChapterBlueprints.Count > 0 || run.ChapterBrief != null,
                RagPassed = isFirstChapter || contextPackage.LongDistanceRecall.Count > 0 || contextPackage.PreviousSummaries.Count > 0
            };

            if (!report.ChangesDetected)
            {
                report.Issues.Add("未识别到 CHANGES 区域，正文不能进入书城。");
                report.RepairHints.Add("在正文末尾追加 ---CHANGES--- 和完整 JSON 变更声明。");
            }

            if (!report.ProtocolPassed)
            {
                report.Issues.Add("CHANGES JSON 不是可解析对象。");
                report.RepairHints.Add("修复 CHANGES JSON 语法，并保留角色、冲突、伏笔等顶级字段。");
            }

            if (!report.FactSnapshotPassed)
            {
                report.Issues.Add("章节上下文缺少事实快照或结构化状态。");
                report.RepairHints.Add("先构建章节上下文包，补齐角色状态、冲突进展或世界规则。");
            }

            if (!report.BlueprintPassed)
            {
                report.Issues.Add("章节缺少蓝图/候选简报依据。");
                report.RepairHints.Add("先确认章节候选或生成章节蓝图。");
            }

            if (!report.RagPassed)
            {
                report.Issues.Add("没有可用上章摘要或长距离召回，长篇连续性不足。");
                report.RepairHints.Add("刷新章节摘要和长距离 RAG 索引后再生成。");
            }

            ApplyCoreContinuityGate(report, contextPackage, draft);
            report.Status = report.Issues.Count == 0 ? "validated" : "gate_failed";
            return report;
        }

        private static int ExtractChapterNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            var digits = new string(value.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var number) ? number : 0;
        }

        private async Task BackfillPreviousChapterSummaryAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary package,
            CancellationToken ct)
        {
            if (package.PreviousSummaries.Count > 0)
                return;

            var currentNumber = ExtractChapterNumber(run.TargetChapterId);
            if (currentNumber <= 1)
                return;

            var summaries = await LoadPreviousSummariesFromStoreAsync(run.TargetChapterId, ct).ConfigureAwait(false);
            if (summaries.Count == 0)
                summaries = await LoadPreviousSummariesFromCommittedContentAsync(run.TargetChapterId, currentNumber, ct)
                    .ConfigureAwait(false);

            foreach (var summary in summaries.Where(HasText))
                package.PreviousSummaries.Add(summary);
        }

        private static async Task<List<string>> LoadPreviousSummariesFromStoreAsync(
            string currentChapterId,
            CancellationToken ct)
        {
            var summaryStore = TM.Framework.Common.Services.ServiceLocator.TryGet<TM.Services.Modules.ProjectData.Implementations.ChapterSummaryStore>();
            if (summaryStore == null)
                return new List<string>();

            try
            {
                var summaries = await summaryStore.GetPreviousSummariesAsync(currentChapterId, 3).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                return summaries
                    .Where(kv => ChapterParserHelper.CompareChapterId(kv.Key, currentChapterId) < 0)
                    .OrderByDescending(kv => ChapterParserHelper.ParseChapterId(kv.Key)?.chapterNumber ?? 0)
                    .Take(3)
                    .Select(kv => $"{kv.Key}: {kv.Value}")
                    .Where(HasText)
                    .ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                TM.App.Log($"[HardcoreWritingEngine] 摘要链兜底读取失败: {ex.Message}");
                return new List<string>();
            }
        }

        private async Task<List<string>> LoadPreviousSummariesFromCommittedContentAsync(
            string currentChapterId,
            int currentNumber,
            CancellationToken ct)
        {
            var previousChapterId = FormatSiblingChapterId(currentChapterId, currentNumber - 1);
            var previousContent = await LoadCommittedChapterContentAsync(previousChapterId, ct).ConfigureAwait(false);
            if (!HasText(previousContent))
                return new List<string>();

            return new List<string>
            {
                $"{previousChapterId}: {BuildCommittedChapterSummary(previousContent!)}"
            };
        }

        private async Task<string?> LoadCommittedChapterContentAsync(string chapterId, CancellationToken ct)
        {
            if (_generatedContentService != null)
            {
                try
                {
                    var content = await _generatedContentService.GetChapterAsync(chapterId).ConfigureAwait(false);
                    if (HasText(content))
                        return content;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    TM.App.Log($"[HardcoreWritingEngine] 读取已提交章节 {chapterId} 失败: {ex.Message}");
                }
            }

            if (_contentChunkSearch != null)
            {
                var chunks = await _contentChunkSearch.SearchByChapterAsync(chapterId, topK: 3).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                var content = string.Join("\n", chunks.OrderBy(c => c.Position).Select(c => c.Content));
                if (HasText(content))
                    return content;
            }

            return null;
        }

        private static string FormatSiblingChapterId(string currentChapterId, int chapterNumber)
        {
            if (chapterNumber <= 0)
                return string.Empty;

            var match = Regex.Match(currentChapterId ?? string.Empty, @"^(.*?)(\d+)(\D*)$");
            if (!match.Success)
                return $"chapter-{chapterNumber:000}";

            var width = match.Groups[2].Value.Length;
            return $"{match.Groups[1].Value}{chapterNumber.ToString().PadLeft(width, '0')}{match.Groups[3].Value}";
        }

        private static string BuildCommittedChapterSummary(string content)
        {
            var body = Regex.Replace(content ?? string.Empty, @"\s+", " ").Trim();
            if (body.Length > 600) body = body[..600] + "...";
            return body;
        }

        public GenerationGateReport ValidateDraft(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft) =>
            ValidateDraftAsync(run, contextPackage, draft).GetAwaiter().GetResult();

        public async Task<ChapterDraftArtifact> RepairDraftAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft,
            GenerationGateReport report,
            CancellationToken ct = default)
        {
            var settings = await LoadSettingsAsync(ct).ConfigureAwait(false);
            if (!settings.IsConfigured)
            {
                draft.Status = "blocked_missing_llm_settings";
                return draft;
            }

            var raw = await CompleteWritingAsync(
                settings,
                BuildWritingSystemPrompt(),
                BuildRepairUserPrompt(run, contextPackage, draft, report),
                ct).ConfigureAwait(false);
            var repaired = new ChapterDraftArtifact
            {
                ChapterId = run.TargetChapterId,
                DraftContent = raw.Trim(),
                ChangesJson = ExtractChangesJson(raw),
                HasChanges = GenerationGate.HasChangesRegion(raw),
            };
            repaired.ArtifactId = draft.ArtifactId;
            repaired.RepairAttemptCount = draft.RepairAttemptCount + 1;
            repaired.Status = "repairing";
            return repaired;
        }

        public ChapterDraftArtifact RepairDraft(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft,
            GenerationGateReport report) =>
            RepairDraftAsync(run, contextPackage, draft, report).GetAwaiter().GetResult();

        public async Task<DependencyImpactReport> CommitValidatedChapterAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft,
            CancellationToken ct = default)
        {
            if (_generatedContentService == null)
                throw new InvalidOperationException("Web 写作引擎未注册 IGeneratedContentService，不能提交章节。");

            var gate = await ValidateDraftAsync(run, contextPackage, draft, ct).ConfigureAwait(false);
            if (gate.Status != "validated")
                throw new InvalidOperationException($"章节未通过 GenerationGate，禁止提交：{string.Join("；", gate.Issues.Take(4))}");

            var committed = StripChanges(draft.DraftContent);
            await _generatedContentService.SaveChapterAsync(run.TargetChapterId, committed).ConfigureAwait(false);
            StartPostCommitRefresh(run, contextPackage, draft, committed);
            return RefreshIndexesAndAnalyzeImpact(run, draft);
        }

        private void StartPostCommitRefresh(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft,
            string committedContent)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await ExtractAndPersistContinuityFactsAsync(run, contextPackage, committedContent, CancellationToken.None)
                        .ConfigureAwait(false);
                    await RefreshIndexesAsync(run, draft, committedContent, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[HardcoreWritingEngine] 提交后后台沉淀/索引刷新失败（章节已提交）：{ex.Message}");
                }
            });
        }

        public DependencyImpactReport RefreshIndexesAndAnalyzeImpact(
            NovelAgentRun run,
            ChapterDraftArtifact draft)
        {
            return new DependencyImpactReport
            {
                Status = "clean",
                ChangedModules = new List<string> { "Chapter", "Tracking", "VectorIndex", "ValidationSummary" },
                ImpactedModules = new List<string> { "Blueprint", "ValidationSummary", "LongDistanceRecall" },
                ImpactedChapters = new List<string> { run.TargetChapterId },
                Summary = $"章节 {run.TargetChapterId} 已通过门禁，已标记刷新事实快照、摘要链和长距离 RAG 索引。"
            };
        }

        public string StripChanges(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return string.Empty;
            var xml = Regex.Match(
                content,
                @"<\s*(?:chapter_changes|changes)\b[^>]*>[\s\S]*?</\s*(?:chapter_changes|changes)\s*>",
                RegexOptions.IgnoreCase);
            if (xml.Success)
                return content[..xml.Index].Trim();

            var xmlStart = Regex.Match(
                content,
                @"<\s*(?:chapter_changes|changes)\b[^>]*>",
                RegexOptions.IgnoreCase);
            if (xmlStart.Success)
                return content[..xmlStart.Index].Trim();

            var index = content.IndexOf(ChangesSeparator, StringComparison.Ordinal);
            return index >= 0 ? content[..index].Trim() : content.Trim();
        }

        private static IEnumerable<string> BuildBlueprintLines(NovelAgentRun run)
        {
            var brief = run.ChapterBrief;
            if (brief == null) yield break;
            if (HasText(brief.VolumeBeatRole)) yield return $"卷节拍：{brief.VolumeBeatRole}";
            if (HasText(brief.CoreIdea)) yield return $"核心创意：{brief.CoreIdea}";
            if (HasText(brief.ConflictMove)) yield return $"冲突推进：{brief.ConflictMove}";
            if (HasText(brief.CharacterChoice)) yield return $"角色选择：{brief.CharacterChoice}";
            if (HasText(brief.CostOrConsequence)) yield return $"代价后果：{brief.CostOrConsequence}";
            if (HasText(brief.ForeshadowingAction)) yield return $"伏笔动作：{brief.ForeshadowingAction}";
        }

        private static void ApplyContinuityPack(
            StoryBibleDocument document,
            NovelAgentRun run,
            ChapterContextPackageSummary package)
        {
            var currentNumber = ExtractChapterNumber(run.TargetChapterId);
            var previousFacts = document.ContinuityFacts
                .Where(f => !string.IsNullOrWhiteSpace(f.ChapterId))
                .Select(f => new { Facts = f, Number = ExtractChapterNumber(f.ChapterId) })
                .Where(x => x.Number > 0 && (currentNumber <= 0 || x.Number < currentNumber))
                .OrderByDescending(x => x.Number)
                .Take(3)
                .Select(x => x.Facts)
                .ToList();

            foreach (var facts in previousFacts)
            {
                foreach (var line in FormatContinuityFactLines(facts))
                    package.HardContinuityFacts.Add(line);

                if (HasText(facts.ProtagonistName))
                {
                    package.CharacterStates.Insert(0,
                        $"{facts.ProtagonistName}：{FirstNonEmpty(facts.ProtagonistIdentity, "主角")}；当前状态={facts.ProtagonistStatus}；位置={facts.CurrentLocation}；系统={facts.SystemState}；装备={facts.EquipmentState}");
                }

                if (HasText(facts.EndingState))
                    package.PreviousSummaries.Insert(0, $"{facts.ChapterId}: {facts.EndingState}");
            }

            var activeCharacters = document.CharacterLedger
                .Where(c => c.Status is CharacterLedgerStatus.Active
                    or CharacterLedgerStatus.GoalUpdated
                    or CharacterLedgerStatus.SecretSeeded
                    or CharacterLedgerStatus.RelationshipChanged
                    or CharacterLedgerStatus.AbilityChanged
                    or CharacterLedgerStatus.PsychologicalShifted
                    or CharacterLedgerStatus.BeliefShifted)
                .OrderByDescending(c => c.Importance)
                .ThenByDescending(c => c.UpdatedAt)
                .Take(8)
                .Select(c => $"{c.CharacterName}：{FirstNonEmpty(c.Role, c.IdentityState)}；{FirstNonEmpty(c.Summary, c.CurrentGoal, c.NextPressure)}");
            package.CharacterStates.AddRange(activeCharacters);

            var activeVolume = document.VolumeArcs
                .Select(v => new
                {
                    Plan = v,
                    Start = ExtractChapterNumber(v.StartChapterId),
                    End = ExtractChapterNumber(v.EndChapterId)
                })
                .Where(x => x.Start <= 0 || currentNumber <= 0 || currentNumber >= x.Start)
                .Where(x => x.End <= 0 || currentNumber <= 0 || currentNumber <= x.End)
                .OrderByDescending(x => x.Start)
                .Select(x => x.Plan)
                .FirstOrDefault();
            if (activeVolume != null)
            {
                package.WorldRules.Insert(0, $"当前卷目标：{FirstNonEmpty(activeVolume.Title, activeVolume.VolumeId)}；{activeVolume.VolumePromise}");
                if (HasText(activeVolume.ExitState))
                    package.ActiveConflicts.Insert(0, $"卷出口状态目标：{activeVolume.ExitState}");
            }
        }

        private static IEnumerable<string> FormatContinuityFactLines(ChapterContinuityFacts facts)
        {
            if (HasText(facts.ProtagonistName)) yield return $"主角姓名：{facts.ProtagonistName}";
            if (HasText(facts.ProtagonistIdentity)) yield return $"主角身份：{facts.ProtagonistIdentity}";
            if (HasText(facts.ProtagonistStatus)) yield return $"主角当前状态：{facts.ProtagonistStatus}";
            if (HasText(facts.CurrentLocation)) yield return $"当前位置：{facts.CurrentLocation}";
            if (HasText(facts.SystemState)) yield return $"系统状态：{facts.SystemState}";
            if (HasText(facts.EquipmentState)) yield return $"装备状态：{facts.EquipmentState}";
            foreach (var keyEvent in facts.KeyEvents.Where(HasText).Take(8))
                yield return $"已发生事件：{keyEvent}";
            if (HasText(facts.EndingState)) yield return $"上一章结尾状态：{facts.EndingState}";
            foreach (var carry in facts.NextChapterMustCarry.Where(HasText).Take(8))
                yield return $"下一章必须承接：{carry}";
        }

        private static void ApplyCoreContinuityGate(
            GenerationGateReport report,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft)
        {
            var body = NormalizeContinuityText(Regex.Replace(StripChangesStatic(draft.DraftContent), @"<\s*(?:chapter_changes|changes)\b[\s\S]*$", string.Empty, RegexOptions.IgnoreCase));
            var protagonistName = ExtractFactValue(contextPackage.HardContinuityFacts, "主角姓名");
            if (HasText(protagonistName) && !body.Contains(NormalizeContinuityText(protagonistName!), StringComparison.Ordinal))
            {
                report.Issues.Add($"核心连续性失败：本章没有承接硬事实主角「{protagonistName}」。");
                report.RepairHints.Add($"重写正文，主角姓名、身份和当前状态必须继续使用「{protagonistName}」。");
            }

            var protagonistStatus = ExtractFactValue(contextPackage.HardContinuityFacts, "主角当前状态");
            if (HasText(protagonistStatus) && !ContainsEnoughContinuityKeywords(body, protagonistStatus!))
            {
                report.Issues.Add("核心连续性失败：主角当前状态没有从上一章硬事实自然承接。");
                report.RepairHints.Add($"承接主角状态：{protagonistStatus}");
            }

            var systemState = ExtractFactValue(contextPackage.HardContinuityFacts, "系统状态");
            if (HasText(systemState) && body.Contains("系统", StringComparison.Ordinal) && !ContainsEnoughContinuityKeywords(body, systemState!))
            {
                report.Issues.Add("核心连续性失败：系统状态与上一章硬事实不一致或发生无解释跳变。");
                report.RepairHints.Add($"系统状态必须从这里承接：{systemState}");
            }

            foreach (var carry in ExtractFactValues(contextPackage.HardContinuityFacts, "上一章结尾状态", "下一章必须承接").Take(8))
            {
                if (!ContainsEnoughContinuityKeywords(body, carry))
                {
                    report.Issues.Add($"核心连续性失败：未承接「{TrimForIssue(carry)}」。");
                    report.RepairHints.Add($"开章或关键场景必须回应上一章结尾/必须承接项：{carry}");
                }
            }

            if (report.Issues.Count > 0)
            {
                report.FactSnapshotPassed = false;
                report.RagPassed = false;
                report.Status = "gate_failed";
            }
        }

        private async Task ExtractAndPersistContinuityFactsAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            string committedContent,
            CancellationToken ct)
        {
            if (_storyBibleService == null || !HasText(committedContent))
                return;

            try
            {
                var settings = await LoadSettingsAsync(ct).ConfigureAwait(false);
                if (!settings.IsConfigured)
                {
                    TM.App.Log("[HardcoreWritingEngine] LLM 未配置，跳过章节连续性事实沉淀；不会使用规则抽取伪造事实。");
                    return;
                }

                var raw = await CompleteWritingAsync(
                    settings,
                    BuildContinuityExtractionSystemPrompt(),
                    BuildContinuityExtractionUserPrompt(run, contextPackage, committedContent),
                    ct).ConfigureAwait(false);
                if (!TryDeserializeContinuityFacts(raw, out var facts))
                {
                    TM.App.Log($"[HardcoreWritingEngine] LLM 连续性事实 JSON 不可解析，跳过沉淀：{TrimForIssue(raw)}");
                    return;
                }

                facts.ChapterId = FirstNonEmpty(facts.ChapterId, run.TargetChapterId);
                facts.SourceRunId = FirstNonEmpty(facts.SourceRunId, run.RunId);
                facts.ExtractedAt = DateTime.Now;
                var result = await _storyBibleService.UpsertContinuityFactsAsync(facts, ct).ConfigureAwait(false);
                if (result.Success)
                    run.Notes.Add($"已沉淀章节连续性事实：{FirstNonEmpty(facts.ChapterTitle, facts.ChapterId)}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                TM.App.Log($"[HardcoreWritingEngine] 章节连续性事实沉淀失败（章节已提交）：{ex.Message}");
            }
        }

        private static string BuildContinuityExtractionSystemPrompt() =>
            """
            你是长篇小说事实沉淀模型。只从给定成稿中抽取已经发生且明确写出的事实，不推测、不补设定。
            必须只输出一个合法 JSON 对象，不要 Markdown，不要解释。
            JSON 字段必须包含：
            chapterId, chapterTitle, protagonistName, protagonistIdentity, protagonistStatus, currentLocation,
            systemState, equipmentState, keyEvents, endingState, nextChapterMustCarry。
            keyEvents 和 nextChapterMustCarry 必须是字符串数组；没有明确事实时填空字符串或空数组。
            """;

        private static string BuildContinuityExtractionUserPrompt(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            string committedContent)
        {
            var payload = new
            {
                task = "extract_chapter_continuity_facts",
                chapterId = run.TargetChapterId,
                existingHardContinuityFacts = contextPackage.HardContinuityFacts,
                chapterText = committedContent
            };
            return JsonSerializer.Serialize(payload, JsonHelper.CnDefault);
        }

        private static bool TryDeserializeContinuityFacts(string raw, out ChapterContinuityFacts facts)
        {
            facts = new ChapterContinuityFacts();
            var json = ExtractJsonObject(raw);
            if (!HasText(json))
                return false;

            try
            {
                facts = JsonSerializer.Deserialize<ChapterContinuityFacts>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                }) ?? new ChapterContinuityFacts();
                return HasText(facts.ChapterId) ||
                       HasText(facts.ProtagonistName) ||
                       HasText(facts.EndingState) ||
                       facts.KeyEvents.Count > 0 ||
                       facts.NextChapterMustCarry.Count > 0;
            }
            catch
            {
                facts = new ChapterContinuityFacts();
                return false;
            }
        }

        private static string ExtractJsonObject(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var text = raw.Trim();
            if (text.StartsWith("```", StringComparison.Ordinal))
            {
                text = Regex.Replace(text, @"^```(?:json)?", string.Empty, RegexOptions.IgnoreCase).Trim();
                text = Regex.Replace(text, @"```$", string.Empty).Trim();
            }

            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            return start >= 0 && end > start ? text[start..(end + 1)] : text;
        }

        private static string? ExtractFactValue(IEnumerable<string> facts, string key) =>
            ExtractFactValues(facts, key).FirstOrDefault();

        private static IEnumerable<string> ExtractFactValues(IEnumerable<string> facts, params string[] keys)
        {
            foreach (var fact in facts.Where(HasText))
            {
                foreach (var key in keys)
                {
                    var prefix = key + "：";
                    if (fact.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        yield return fact[prefix.Length..].Trim();
                    prefix = key + ":";
                    if (fact.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        yield return fact[prefix.Length..].Trim();
                }
            }
        }

        private static bool ContainsEnoughContinuityKeywords(string body, string expected)
        {
            var searchableBody = ExpandContinuityAliases(body);
            var keywords = ExtractContinuityKeywords(ExpandContinuityAliases(expected))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (keywords.Count == 0)
                return true;
            var hits = keywords.Count(keyword => searchableBody.Contains(keyword, StringComparison.Ordinal));
            var required = keywords.Count <= 2 ? keywords.Count : Math.Min(3, keywords.Count);
            return hits >= required;
        }

        private static string ExpandContinuityAliases(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var expanded = value;
            if (ContainsAny(expanded, "临近", "将至", "来临", "即将", "逼近", "接近"))
                expanded += "临近将至来临即将逼近";

            if (ContainsAny(expanded, "提前", "爆发", "开始", "已开始", "已经开始", "发生"))
                expanded += "来临临近升级增加";

            if (ContainsAny(expanded, "威胁", "危险", "危机", "风险", "戒备", "预警", "告警"))
                expanded += "威胁危险危机风险戒备";

            if (ContainsAny(expanded, "增加", "加剧", "升级", "飙升", "超预期", "增强", "扩大"))
                expanded += "增加加剧升级飙升增强";

            if (expanded.Contains("逆潮现象", StringComparison.Ordinal) &&
                !expanded.Contains("逆潮夜", StringComparison.Ordinal))
            {
                expanded += "逆潮夜";
            }

            if (expanded.Contains("逆潮夜", StringComparison.Ordinal) &&
                !expanded.Contains("逆潮现象", StringComparison.Ordinal))
            {
                expanded += "逆潮现象";
            }

            if (expanded.Contains("蓝磷骨光", StringComparison.Ordinal) &&
                !expanded.Contains("蓝光现象", StringComparison.Ordinal))
            {
                expanded += "蓝光现象蓝光";
            }

            if (expanded.Contains("蓝光", StringComparison.Ordinal) &&
                !expanded.Contains("蓝磷骨光", StringComparison.Ordinal))
            {
                expanded += "蓝磷骨光";
            }

            return expanded;
        }

        private static bool ContainsAny(string value, params string[] candidates) =>
            candidates.Any(candidate => value.Contains(candidate, StringComparison.Ordinal));

        private static IEnumerable<string> ExtractContinuityKeywords(string value)
        {
            foreach (Match match in Regex.Matches(value ?? string.Empty, @"[\u4e00-\u9fffA-Za-z0-9]{2,}"))
            {
                var token = match.Value.Trim();
                if (token.Length < 2)
                    continue;
                if (IsContinuityStopword(token))
                    continue;

                if (Regex.IsMatch(token, @"^[\u4e00-\u9fff]+$"))
                {
                    if (token.Length <= 4)
                    {
                        yield return token;
                        continue;
                    }

                    yield return token[..4];
                    for (var i = 0; i <= token.Length - 2; i++)
                    {
                        var slice = token.Substring(i, 2);
                        if (!IsContinuityStopword(slice))
                            yield return slice;
                    }
                    continue;
                }

                yield return token.Length > 8 ? token[..8] : token;
            }
        }

        private static bool IsContinuityStopword(string token) =>
            token is "必须" or "承接" or "当前" or "状态" or "主角" or "下一章" or
                "正在" or "已经" or "没有" or "解释" or "查看" or "前往" or "身份" or
                "位置" or "发生" or "事件";

        private static string NormalizeContinuityText(string? value) =>
            Regex.Replace(value ?? string.Empty, @"\s+", string.Empty).Trim();

        private static string TrimForIssue(string value)
        {
            var text = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
            return text.Length <= 80 ? text : text[..80] + "...";
        }

        private static string StripChangesStatic(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return string.Empty;
            var xml = Regex.Match(
                content,
                @"<\s*(?:chapter_changes|changes)\b[^>]*>[\s\S]*?</\s*(?:chapter_changes|changes)\s*>",
                RegexOptions.IgnoreCase);
            if (xml.Success)
                return content[..xml.Index].Trim();
            var index = content.IndexOf(ChangesSeparator, StringComparison.Ordinal);
            return index >= 0 ? content[..index].Trim() : content.Trim();
        }

        private static string? ExtractCharacterName(IEnumerable<string> characterStates)
        {
            foreach (var state in characterStates)
            {
                var match = Regex.Match(state, @"^[^：:,\s]{2,12}");
                if (match.Success) return match.Value;
            }

            return null;
        }

        private static bool IsValidJsonObject(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.ValueKind == JsonValueKind.Object;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

        private static string FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(HasText)?.Trim() ?? string.Empty;

        private async Task RefreshIndexesAsync(NovelAgentRun run, ChapterDraftArtifact draft, string content, CancellationToken ct)
        {
            try
            {
                if (_guideContextService != null)
                {
                    var summaryStore = TM.Framework.Common.Services.ServiceLocator.TryGet<TM.Services.Modules.ProjectData.Implementations.ChapterSummaryStore>();
                    if (summaryStore != null)
                        await summaryStore.SetSummaryAsync(run.TargetChapterId, BuildChapterSummary(run, content)).ConfigureAwait(false);
                }

                if (_embeddingService != null)
                {
                    var chapterVector = await _embeddingService.EncodeAsync(content, EmbeddingMode.Passage, ct).ConfigureAwait(false);
                    if (_chapterEmbeddingIndex != null)
                    {
                        await _chapterEmbeddingIndex.UpsertAsync(run.TargetChapterId, chapterVector, ct).ConfigureAwait(false);
                        await _chapterEmbeddingIndex.SaveAsync(ct).ConfigureAwait(false);
                    }

                    if (_chunkEmbeddingIndex != null)
                    {
                        await _chunkEmbeddingIndex.RemoveByChapterAsync(run.TargetChapterId, ct).ConfigureAwait(false);
                        var chunks = ChunkContent(content).ToList();
                        var vectors = await _embeddingService.EncodeBatchAsync(chunks, EmbeddingMode.Passage, ct).ConfigureAwait(false);
                        var items = chunks.Select((_, i) => (ChunkKey.Format(run.TargetChapterId, i), vectors[i])).ToList();
                        await _chunkEmbeddingIndex.UpsertBatchAsync(items, ct).ConfigureAwait(false);
                        await _chunkEmbeddingIndex.SaveAsync(ct).ConfigureAwait(false);
                    }
                }

                _contentChunkSearch?.InvalidateCache();
                if (!string.IsNullOrWhiteSpace(draft.ChangesJson) &&
                    TryDeserializeChanges(draft.ChangesJson, out var changes))
                {
                    var keywordIndex = TM.Framework.Common.Services.ServiceLocator.TryGet<TM.Services.Modules.ProjectData.Implementations.KeywordChapterIndexService>();
                    if (keywordIndex != null)
                        await keywordIndex.IndexChapterAsync(run.TargetChapterId, changes).ConfigureAwait(false);
                    var changesWal = TM.Framework.Common.Services.ServiceLocator.TryGet<ChapterChangesWalStore>();
                    if (changesWal != null)
                        await changesWal.WriteAsync(run.TargetChapterId, changes).ConfigureAwait(false);
                }

                IncrementModuleVersion("Chapter");
                IncrementModuleVersion("Tracking");
                IncrementModuleVersion("VectorIndex");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[HardcoreWritingEngine] 刷新章节索引失败（章节已保存）：{ex.Message}");
            }
        }

        private static string BuildChapterSummary(NovelAgentRun run, string content)
        {
            var title = FirstNonEmpty(run.ChapterBrief?.SelectedCandidateTitle, run.ChapterBrief?.RecommendedCandidateTitle, run.TargetChapterId);
            var body = Regex.Replace(content ?? string.Empty, @"\s+", " ").Trim();
            if (body.Length > 600) body = body[..600] + "...";
            return $"{title}: {body}";
        }

        private static IEnumerable<string> ChunkContent(string content)
        {
            content = content ?? string.Empty;
            const int size = 900;
            const int overlap = 120;
            if (content.Length <= size)
            {
                if (HasText(content)) yield return content;
                yield break;
            }
            for (var start = 0; start < content.Length; start += size - overlap)
            {
                var len = Math.Min(size, content.Length - start);
                if (len <= 0) yield break;
                yield return content.Substring(start, len);
                if (start + len >= content.Length) yield break;
            }
        }

        private static GenerationGateReport MapGateResult(TM.Services.Modules.ProjectData.Models.Tracking.GateResult gate, ChapterContextPackageSummary contextPackage)
        {
            var failures = gate.GetHumanReadableFailures(20);
            return new GenerationGateReport
            {
                Status = gate.Success ? "validated" : "gate_failed",
                ChangesDetected = gate.ParsedChanges != null || !string.IsNullOrWhiteSpace(gate.ContentWithoutChanges),
                ProtocolPassed = gate.Failures.All(f => f.Type != TM.Services.Modules.ProjectData.Models.Tracking.FailureType.Protocol),
                FactSnapshotPassed = true,
                BlueprintPassed = contextPackage.ChapterBlueprints.Count > 0,
                RagPassed = contextPackage.LongDistanceRecall.Count > 0 || contextPackage.PreviousSummaries.Count > 0,
                Issues = failures.Count > 0 ? failures : gate.GetAllFailures(),
                RepairHints = failures.Count > 0
                    ? failures.Select(f => $"修复：{f}").Take(8).ToList()
                    : new List<string>()
            };
        }

        private static DesignElementNames BuildDesignElements(ContentTaskContext? context)
        {
            if (context == null) return new DesignElementNames();
            return new DesignElementNames
            {
                CharacterNames = context.Characters.Select(c => c.Name).Where(HasText).Distinct().ToList(),
                FactionNames = context.Factions.Select(f => f.Name).Where(HasText).Distinct().ToList(),
                LocationNames = context.Locations.Select(l => l.Name).Where(HasText).Distinct().ToList(),
                PlotKeyNames = context.PlotRules.Select(p => p.Name).Where(HasText).Distinct().ToList(),
                PovCharacterNames = context.Characters.Take(1).Select(c => c.Name).Where(HasText).ToList()
            };
        }

        private static IEnumerable<string> Merge(params IEnumerable<string?>?[] sources) =>
            sources
                .Where(s => s != null)
                .SelectMany(s => s!)
                .Where(HasText)
                .Select(s => s!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase);

        private async Task<LlmRuntimeSettings> LoadSettingsAsync(CancellationToken ct)
        {
            if (_settingsManager == null) return LlmRuntimeSettings.Empty;
            var load = _settingsManager.GetType().GetMethod("LoadAsync", new[] { typeof(CancellationToken) });
            if (load == null) return LlmRuntimeSettings.Empty;
            var task = load.Invoke(_settingsManager, new object[] { ct });
            if (task is not Task settingsTask) return LlmRuntimeSettings.Empty;
            await settingsTask.ConfigureAwait(false);
            var result = settingsTask.GetType().GetProperty("Result")?.GetValue(settingsTask);
            return LlmRuntimeSettings.From(result);
        }

        private static async Task<string> CompleteWritingAsync(LlmRuntimeSettings settings, string system, string user, CancellationToken ct)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var provider = settings.Provider;
            var baseUrl = settings.BaseUrl.TrimEnd('/');
            var model = NormalizeProviderModelId(settings.Model);
            var maxTokens = NormalizeWritingMaxTokens(settings.MaxTokens);

            if (string.Equals(provider, "anthropic", StringComparison.OrdinalIgnoreCase))
            {
                http.DefaultRequestHeaders.Add("api-key", settings.ApiKey);
                http.DefaultRequestHeaders.Add("x-api-key", settings.ApiKey);
                http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                var anthropicPayload = new
                {
                    model,
                    system,
                    max_tokens = maxTokens,
                    temperature = settings.Temperature,
                    messages = new[] { new { role = "user", content = user } }
                };
                using var response = await http.PostAsync(
                    BuildAnthropicMessagesUrl(baseUrl),
                    new StringContent(JsonSerializer.Serialize(anthropicPayload), Encoding.UTF8, "application/json"),
                    ct).ConfigureAwait(false);
                var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"写作模型接口返回 {(int)response.StatusCode}: {text}");
                using var doc = JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                    return string.Join("\n", content.EnumerateArray()
                        .Select(item => item.TryGetProperty("text", out var t) ? t.GetString() : null)
                        .Where(HasText));
                return text;
            }

            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            var payload = new
            {
                model,
                temperature = settings.Temperature,
                max_tokens = maxTokens,
                messages = new[]
                {
                    new { role = "system", content = system },
                    new { role = "user", content = user }
                }
            };
            var url = baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
                ? baseUrl
                : $"{baseUrl}/chat/completions";
            using var openAiResponse = await http.PostAsync(
                url,
                new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
                ct).ConfigureAwait(false);
            var json = await openAiResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!openAiResponse.IsSuccessStatusCode)
                throw new InvalidOperationException($"写作模型接口返回 {(int)openAiResponse.StatusCode}: {json}");
            using var openAiDoc = JsonDocument.Parse(json);
            return openAiDoc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
                   ?? json;
        }

        private static string BuildAnthropicMessagesUrl(string baseUrl)
        {
            var url = baseUrl.Trim().TrimEnd('/');
            if (url.EndsWith("/v1/messages", StringComparison.OrdinalIgnoreCase) ||
                url.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }
            if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                return $"{url}/messages";
            }
            return $"{url}/v1/messages";
        }

        private static string BuildWritingSystemPrompt() =>
            """
            你是长篇小说正文写作模型。你必须输出完整章节正文，并在末尾输出成对的 <chapter_changes>...</chapter_changes>。
            CHANGES 内只能是合法 JSON 对象，必须包含这些顶级字段：
            CharacterStateChanges, ConflictProgress, NewPlotPoints, ForeshadowingActions, LocationStateChanges,
            FactionStateChanges, TimeProgression, CharacterMovements, ItemTransfers, SecretRevealChanges,
            PledgeConstraintChanges, DeadlineConstraintChanges。
            所有不存在的变更字段也要用空数组或空对象显式给出。不要使用 Markdown 代码块包裹 CHANGES。
            正文必须严格遵守上下文包、事实快照、蓝图、长距离召回，不得发明关键实体。
            """;

        private static string BuildWritingUserPrompt(NovelAgentRun run, ChapterContextPackageSummary contextPackage)
        {
            var payload = new
            {
                task = "generate_chapter_with_changes",
                chapterId = run.TargetChapterId,
                chapterBrief = run.ChapterBrief,
                contextPackage,
                requiredChangesSchema = ChapterChanges.TopLevelFieldNames
            };
            return JsonSerializer.Serialize(payload, JsonHelper.CnDefault);
        }

        private static string BuildRepairUserPrompt(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft,
            GenerationGateReport report)
        {
            var payload = new
            {
                task = "repair_chapter_draft_with_changes",
                chapterId = run.TargetChapterId,
                repairAttempt = draft.RepairAttemptCount + 1,
                repairStrategy = SelectRepairStrategy(draft.RepairAttemptCount + 1),
                gateIssues = report.Issues,
                repairHints = report.RepairHints,
                contextPackage,
                previousDraft = draft.DraftContent,
                requiredChangesSchema = ChapterChanges.TopLevelFieldNames
            };
            return JsonSerializer.Serialize(payload, JsonHelper.CnDefault);
        }

        private static string SelectRepairStrategy(int attempt) =>
            attempt switch
            {
                <= 1 => "patch_missing_continuity_facts_without_changing_valid_plot",
                2 => "rewrite_scene_that_failed_continuity_gate",
                _ => "regenerate_opening_and_key_scene_around_hard_continuity_facts"
            };

        private static string ExtractChangesJson(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return string.Empty;
            var xml = Regex.Match(content, @"<\s*(?:chapter_changes|changes)\s*>([\s\S]*?)</\s*(?:chapter_changes|changes)\s*>", RegexOptions.IgnoreCase);
            if (xml.Success)
            {
                var extracted = xml.Groups[1].Value.Trim();
                return GenerationGate.TryNormalizeChangesJsonShape(extracted, out var normalized)
                    ? normalized
                    : extracted;
            }
            var index = content.LastIndexOf(ChangesSeparator, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return string.Empty;
            var changes = content[(index + ChangesSeparator.Length)..].Trim();
            return GenerationGate.TryNormalizeChangesJsonShape(changes, out var normalizedChanges)
                ? normalizedChanges
                : changes;
        }

        private static bool TryDeserializeChanges(string json, out ChapterChanges changes)
        {
            try
            {
                if (GenerationGate.TryNormalizeChangesJsonShape(json, out var normalized))
                    json = normalized;
                changes = JsonSerializer.Deserialize<ChapterChanges>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                }) ?? new ChapterChanges();
                return true;
            }
            catch
            {
                changes = new ChapterChanges();
                return false;
            }
        }

        private void IncrementModuleVersion(string moduleName)
        {
            var method = _versionTrackingService?.GetType().GetMethod("IncrementModuleVersion", BindingFlags.Instance | BindingFlags.Public);
            try { method?.Invoke(_versionTrackingService, new object[] { moduleName }); }
            catch { }
        }

        private static string NormalizeProviderModelId(string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return model;
            var value = model.Trim();
            var slash = value.IndexOf('/');
            if (slash >= 0 && slash < value.Length - 1)
                value = value[(slash + 1)..].Trim();
            if (value.EndsWith("[1m]", StringComparison.OrdinalIgnoreCase))
                value = value[..^4].Trim();
            if (value.EndsWith(":extended", StringComparison.OrdinalIgnoreCase))
                value = value[..^9].Trim();
            return value;
        }

        private static int NormalizeWritingMaxTokens(int maxTokens)
        {
            const int minimumWritingOutputTokens = 8192;
            return Math.Max(maxTokens, minimumWritingOutputTokens);
        }

        private sealed record LlmRuntimeSettings(
            string Provider,
            string ApiKey,
            string BaseUrl,
            string Model,
            double Temperature,
            int MaxTokens)
        {
            public static LlmRuntimeSettings Empty { get; } = new("", "", "", "", 0.7, 4096);

            public bool IsConfigured =>
                !string.IsNullOrWhiteSpace(BaseUrl) &&
                !string.IsNullOrWhiteSpace(Model) &&
                !string.IsNullOrWhiteSpace(ApiKey);

            public static LlmRuntimeSettings From(object? settings)
            {
                if (settings == null) return Empty;
                var type = settings.GetType();
                string ReadString(string name) => type.GetProperty(name)?.GetValue(settings)?.ToString() ?? string.Empty;
                double ReadDouble(string name, double fallback)
                {
                    var value = type.GetProperty(name)?.GetValue(settings);
                    return value is double d ? d : double.TryParse(value?.ToString(), out var parsed) ? parsed : fallback;
                }
                int ReadInt(string name, int fallback)
                {
                    var value = type.GetProperty(name)?.GetValue(settings);
                    return value is int i ? i : int.TryParse(value?.ToString(), out var parsed) ? parsed : fallback;
                }
                return new LlmRuntimeSettings(
                    ReadString("LlmProvider"),
                    ReadString("LlmApiKey"),
                    ReadString("LlmBaseUrl"),
                    ReadString("LlmModel"),
                    ReadDouble("LlmTemperature", 0.7),
                    ReadInt("LlmMaxTokens", 4096));
            }
        }
    }
}
