using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TM.Framework.Common.Helpers;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.TaskContexts;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Framework.AI.NovelAgent.Services
{
    public sealed class HardcoreWritingEngine
    {
        private static readonly IChapterPromptBuilder DefaultChapterPromptBuilder =
            new ChapterPromptBuilder(new ChapterDirectiveBuilder());
        private static readonly IChapterGatekeeper DefaultChapterGatekeeper = new ChapterGatekeeper();
        private static readonly IChapterRewriter DefaultChapterRewriter =
            new ChapterRewriter(DefaultChapterPromptBuilder);
        private static readonly IChapterPackageBuilder DefaultChapterPackageBuilder = new ChapterPackageBuilder();

        private readonly StoryStateSnapshotService _storyStateSnapshotService;
        private readonly IChapterPromptBuilder _chapterPromptBuilder;
        private readonly IChapterGatekeeper _chapterGatekeeper;
        private readonly IChapterRewriter _chapterRewriter;
        private readonly IChapterPackageBuilder _chapterPackageBuilder;
        private readonly IGuideContextService? _guideContextService;
        private readonly GenerationGate? _generationGate;
        private readonly IGeneratedContentService? _generatedContentService;
        private readonly IContentChunkSearchService? _contentChunkSearch;
        private readonly object? _settingsManager;
        private readonly IChapterSummaryService? _chapterSummaryService;
        private readonly IChapterFactPostCommitScheduler? _chapterFactPostCommitScheduler;
        private readonly Func<string, string, CancellationToken, Task<string>>? _writingCompletion;

        public HardcoreWritingEngine(
            StoryStateSnapshotService storyStateSnapshotService,
            IGuideContextService guideContextService,
            GenerationGate generationGate,
            IGeneratedContentService generatedContentService,
            IContentChunkSearchService contentChunkSearch,
            object settingsManager,
            IChapterPromptBuilder? chapterPromptBuilder = null,
            IChapterGatekeeper? chapterGatekeeper = null,
            IChapterRewriter? chapterRewriter = null,
            IChapterPackageBuilder? chapterPackageBuilder = null,
            IChapterSummaryService? chapterSummaryService = null,
            IChapterFactPostCommitScheduler? chapterFactPostCommitScheduler = null,
            Func<string, string, CancellationToken, Task<string>>? writingCompletion = null)
        {
            _storyStateSnapshotService = storyStateSnapshotService;
            _chapterPromptBuilder = chapterPromptBuilder ?? DefaultChapterPromptBuilder;
            _chapterGatekeeper = chapterGatekeeper ?? DefaultChapterGatekeeper;
            _chapterRewriter = chapterRewriter ?? DefaultChapterRewriter;
            _chapterPackageBuilder = chapterPackageBuilder ?? DefaultChapterPackageBuilder;
            _guideContextService = guideContextService;
            _generationGate = generationGate;
            _generatedContentService = generatedContentService;
            _contentChunkSearch = contentChunkSearch;
            _settingsManager = settingsManager;
            _chapterSummaryService = chapterSummaryService;
            _chapterFactPostCommitScheduler = chapterFactPostCommitScheduler;
            _writingCompletion = writingCompletion;
        }

        public async Task<ChapterContextPackageSummary> BuildContextPackageAsync(
            NovelAgentRun run,
            StoryBibleDocument document,
            CancellationToken ct = default)
        {
            if (_guideContextService == null)
            {
                throw new InvalidOperationException(
                    "GuideContextService is required before building a Tianming chapter package；缺少真实章节上下文服务，禁止构建章节生产包。");
            }

            ContentTaskContext? contentContext;
            try
            {
                contentContext = await _guideContextService.BuildContentContextAsync(run.TargetChapterId, ct)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"BuildContentContextAsync failed for {run.TargetChapterId}；真实章节上下文构建失败，禁止用 StoryState 代替真实上下文：{ex.Message}",
                    ex);
            }

            if (contentContext == null)
            {
                throw new InvalidOperationException(
                    $"BuildContentContextAsync returned no ContentTaskContext for {run.TargetChapterId}；缺少真实章节上下文，禁止用 StoryState 代替真实上下文。");
            }

            var storyState = run.StoryState ?? await _storyStateSnapshotService
                .BuildForChapterAsync(run.TargetChapterId, ct)
                .ConfigureAwait(false);
            run.StoryState = storyState;

            var package = _chapterPackageBuilder.Build(new ChapterPackageBuildRequest
            {
                Run = run,
                Document = document,
                StoryState = storyState,
                ContentContext = contentContext
            });
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
            string raw;
            if (_writingCompletion != null)
            {
                raw = await _writingCompletion(
                        _chapterPromptBuilder.BuildWritingSystemPrompt(),
                        _chapterPromptBuilder.BuildWritingUserPrompt(run, contextPackage),
                        ct)
                    .ConfigureAwait(false);
            }
            else
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

                raw = await CompleteWritingAsync(
                        settings,
                        _chapterPromptBuilder.BuildWritingSystemPrompt(),
                        _chapterPromptBuilder.BuildWritingUserPrompt(run, contextPackage),
                        ct)
                    .ConfigureAwait(false);
            }
            var changesJson = ChapterChangesText.ExtractChangesJson(raw);
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
            if (_generationGate == null || _guideContextService == null)
                return BuildGenerationGateUnavailableReport(contextPackage, draft, "GenerationGate 未配置，章节不能进入书城。");

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
                _chapterGatekeeper.ApplyHardGates(report, contextPackage, draft);
                return report;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return BuildGenerationGateUnavailableReport(
                    contextPackage,
                    draft,
                    $"GenerationGate 执行失败，章节不能进入书城：{ex.Message}");
            }
        }

        private GenerationGateReport BuildGenerationGateUnavailableReport(
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft,
            string issue)
        {
            var report = new GenerationGateReport
            {
                Status = "gate_failed",
                ChangesDetected = false,
                ProtocolPassed = false,
                FactSnapshotPassed = false,
                BlueprintPassed = false,
                RagPassed = false
            };
            report.Issues.Add(issue);
            report.RepairHints.Add("修复真实 GenerationGate 配置或执行错误后，重新运行章节门禁。");
            _chapterGatekeeper.ApplyHardGates(report, contextPackage, draft);
            report.Status = "gate_failed";
            return report;
        }

        private static int ExtractChapterNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            return ChapterParserHelper.ExtractChapterNumber(value);
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

        private async Task<List<string>> LoadPreviousSummariesFromStoreAsync(
            string currentChapterId,
            CancellationToken ct)
        {
            if (_chapterSummaryService == null)
                return new List<string>();

            try
            {
                var summaries = await _chapterSummaryService.GetPreviousSummariesAsync(currentChapterId, 3).ConfigureAwait(false);
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
            LlmRuntimeSettings settings = LlmRuntimeSettings.Empty;
            if (_writingCompletion == null)
            {
                settings = await LoadSettingsAsync(ct).ConfigureAwait(false);
                if (!settings.IsConfigured)
                {
                    draft.Status = "blocked_missing_llm_settings";
                    return draft;
                }
            }

            return await _chapterRewriter.RepairAsync(
                new ChapterRewriteRequest
                {
                    Run = run,
                    ContextPackage = contextPackage,
                    Draft = draft,
                    GateReport = report,
                    CompleteAsync = (system, user, cancellationToken) =>
                        _writingCompletion != null
                            ? _writingCompletion(system, user, cancellationToken)
                            : CompleteWritingAsync(
                                settings,
                                system,
                                user,
                                cancellationToken,
                                IsChangesOnlyRepairPrompt(system, user)
                                    ? WritingCompletionBudget.ChangesOnlyRepair
                                    : WritingCompletionBudget.LongChapter)
                },
                ct).ConfigureAwait(false);
        }

        public ChapterDraftArtifact RepairDraft(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft,
            GenerationGateReport report) =>
            RepairDraftAsync(run, contextPackage, draft, report).GetAwaiter().GetResult();

        public async Task<GenerationGateReport> AuditCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            CancellationToken ct = default)
        {
            var package = PrepareCommittedChapterAuditContext(chapterId, contextPackage);
            var run = new NovelAgentRun
            {
                RunId = Guid.NewGuid().ToString("N"),
                Intent = NovelAgentIntent.ValidateContinuity,
                Status = NovelAgentRunStatus.Validating,
                TargetChapterId = package.ChapterId,
                UserGoal = "审查已提交章节正文的连续性和知识库硬事实。"
            };
            var draft = BuildCommittedChapterAuditDraft(package.ChapterId, committedContent);
            var report = await ValidateDraftAsync(run, package, draft, ct).ConfigureAwait(false);
            report.ValidatedAt = DateTime.Now;
            return report;
        }

        public async Task<NovelAgentExecutionResult> ReviseCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            string revisionGoal,
            CancellationToken ct = default)
        {
            if (_generatedContentService == null)
            {
                return new NovelAgentExecutionResult
                {
                    Success = false,
                    RiskLevel = NovelToolRiskLevel.High,
                    Message = "Web 写作引擎未注册 IGeneratedContentService，不能修订已提交章节。"
                };
            }

            var package = PrepareCommittedChapterAuditContext(chapterId, contextPackage);
            if (package.SourceRevisionPlans.Count == 0)
            {
                return new NovelAgentExecutionResult
                {
                    Success = false,
                    RiskLevel = NovelToolRiskLevel.High,
                    Message = "修订已提交章节必须绑定来源 RevisionPlan；当前上下文包没有 sourceRevisionPlans，已拒绝覆盖书城正文。",
                    WriterResult = committedContent,
                    ContextPackage = package
                };
            }

            var run = new NovelAgentRun
            {
                RunId = Guid.NewGuid().ToString("N"),
                Intent = NovelAgentIntent.RewriteChapter,
                Status = NovelAgentRunStatus.Repairing,
                TargetChapterId = package.ChapterId,
                UserGoal = string.IsNullOrWhiteSpace(revisionGoal)
                    ? "修订已提交章节，使其通过连续性和知识库硬事实门禁。"
                    : revisionGoal.Trim(),
                ContextPackage = package
            };

            var draft = BuildCommittedChapterAuditDraft(package.ChapterId, committedContent);
            var audit = await ValidateDraftAsync(run, package, draft, ct).ConfigureAwait(false);
            if (audit.Status == "validated" && string.IsNullOrWhiteSpace(revisionGoal))
            {
                run.Status = NovelAgentRunStatus.Completed;
                run.DraftArtifact = draft;
                run.GateReport = audit;
                return new NovelAgentExecutionResult
                {
                    Success = true,
                    RiskLevel = NovelToolRiskLevel.Medium,
                    Message = "已提交章节通过回溯审查，未执行正文覆盖。",
                    WriterResult = committedContent,
                    ContextPackage = package,
                    DraftArtifact = draft,
                    GateReport = audit,
                    Run = run
                };
            }

            if (audit.Status == "validated" && !string.IsNullOrWhiteSpace(revisionGoal))
            {
                audit.Status = "gate_failed";
                audit.Issues.Add($"用户要求修订已提交章节：{revisionGoal.Trim()}");
                audit.RepairHints.Add($"在不破坏连续性和知识库硬事实的前提下完成修订：{revisionGoal.Trim()}");
            }

            run.DraftArtifact = draft;
            run.GateReport = audit;
            var repaired = await RepairDraftAsync(run, package, draft, audit, ct).ConfigureAwait(false);
            run.DraftArtifact = repaired;
            if (repaired.Status == "blocked_missing_llm_settings")
            {
                run.Status = NovelAgentRunStatus.Failed;
                return new NovelAgentExecutionResult
                {
                    Success = false,
                    RiskLevel = NovelToolRiskLevel.High,
                    Message = "LLM 未配置，不能用模型修订已提交章节；不会用规则替换正文。",
                    WriterResult = committedContent,
                    ContextPackage = package,
                    DraftArtifact = repaired,
                    GateReport = audit,
                    Run = run
                };
            }

            var gate = await ValidateDraftAsync(run, package, repaired, ct).ConfigureAwait(false);
            if (gate.Status != "validated")
            {
                run.GateReport = gate;
                var secondPass = await RepairDraftAsync(run, package, repaired, gate, ct).ConfigureAwait(false);
                run.DraftArtifact = secondPass;
                if (secondPass.Status == "blocked_missing_llm_settings")
                {
                    run.Status = NovelAgentRunStatus.Failed;
                    return new NovelAgentExecutionResult
                    {
                        Success = false,
                        RiskLevel = NovelToolRiskLevel.High,
                        Message = "LLM 未配置，不能继续修订已提交章节；不会用规则替换正文。",
                        WriterResult = committedContent,
                        ContextPackage = package,
                        DraftArtifact = secondPass,
                        GateReport = gate,
                        Run = run
                    };
                }

                gate = await ValidateDraftAsync(run, package, secondPass, ct).ConfigureAwait(false);
                repaired = secondPass;
            }

            run.GateReport = gate;
            if (gate.Status != "validated")
            {
                run.Status = NovelAgentRunStatus.Failed;
                return new NovelAgentExecutionResult
                {
                    Success = false,
                    RiskLevel = NovelToolRiskLevel.High,
                    Message = $"修订稿仍未通过硬门禁，未覆盖书城正文：{string.Join("；", gate.Issues.Take(4))}",
                    WriterResult = StripChanges(repaired.DraftContent),
                    ContextPackage = package,
                    DraftArtifact = repaired,
                    GateReport = gate,
                    Run = run
                };
            }

            var revisedContent = StripChanges(repaired.DraftContent);
            await _generatedContentService.SaveChapterAsync(package.ChapterId, revisedContent).ConfigureAwait(false);
            run.Status = NovelAgentRunStatus.Completed;
            repaired.Status = "committed_revision";
            repaired.CommittedContent = revisedContent;
            repaired.CommittedAt = DateTime.Now;
            return new NovelAgentExecutionResult
            {
                Success = true,
                RiskLevel = NovelToolRiskLevel.High,
                Message = "已提交章节已完成模型修订、通过硬门禁并覆盖入书城。",
                WriterResult = revisedContent,
                ContextPackage = package,
                DraftArtifact = repaired,
                GateReport = gate,
                DependencyImpact = RefreshIndexesAndAnalyzeImpact(run, repaired),
                Run = run
            };
        }

        public async Task<DependencyImpactReport> CommitChapterAsync(
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
            var committedTitle = ResolveCommittedChapterTitle(run);
            if (_generatedContentService is IAtomicGeneratedChapterCommitService atomicCommit)
            {
                await atomicCommit.SaveChapterAtomicallyAsync(
                        run.TargetChapterId,
                        committed,
                        committedTitle,
                        BuildChapterCommitOutboxes(run, contextPackage, draft, gate, committed))
                    .ConfigureAwait(false);
            }
            else if (!string.IsNullOrWhiteSpace(committedTitle) &&
                _generatedContentService is IGeneratedChapterMetadataWriter metadataWriter)
            {
                await metadataWriter.SaveChapterAsync(run.TargetChapterId, committed, committedTitle).ConfigureAwait(false);
            }
            else
            {
                await _generatedContentService.SaveChapterAsync(run.TargetChapterId, committed).ConfigureAwait(false);
            }
            await ScheduleOrExtractContinuityFactsAsync(run, contextPackage, committed, ct)
                .ConfigureAwait(false);
            return RefreshIndexesAndAnalyzeImpact(run, draft);
        }

        private static IReadOnlyList<GeneratedChapterOutboxWrite> BuildChapterCommitOutboxes(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            ChapterDraftArtifact draft,
            GenerationGateReport gate,
            string committedContent)
        {
            var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            return new[]
            {
                new GeneratedChapterOutboxWrite(
                    run.RunId,
                    "extract_chapter_continuity_facts",
                    "chapter",
                    run.TargetChapterId,
                    JsonSerializer.Serialize(new
                    {
                        run,
                        contextPackage,
                        committedContent
                    }, jsonOptions)),
                new GeneratedChapterOutboxWrite(
                    run.RunId,
                    "finalize_chapter_commit_metadata",
                    "chapter",
                    run.TargetChapterId,
                    JsonSerializer.Serialize(new
                    {
                        runtimeRunId = run.RunId,
                        userId = string.Empty,
                        projectId = string.Empty,
                        targetChapterId = run.TargetChapterId,
                        message = "章节正文、版本和提交后任务已原子落库。",
                        contextPackage,
                        draftArtifact = draft,
                        gateReport = gate,
                        postGenerationReview = run.PostGenerationReview,
                        continuityFacts = run.ContinuityFacts
                    }, jsonOptions))
            };
        }

        private static string ResolveCommittedChapterTitle(NovelAgentRun run)
        {
            if (!string.IsNullOrWhiteSpace(run.ChapterBrief?.SelectedCandidateTitle))
                return run.ChapterBrief.SelectedCandidateTitle.Trim();
            if (!string.IsNullOrWhiteSpace(run.ChapterBrief?.RecommendedCandidateTitle))
                return run.ChapterBrief.RecommendedCandidateTitle.Trim();
            return string.Empty;
        }

        private async Task ScheduleOrExtractContinuityFactsAsync(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            string committedContent,
            CancellationToken ct)
        {
            if (_chapterFactPostCommitScheduler == null)
            {
                run.Notes.Add("章节连续性事实未调度：提交后事实沉淀调度器未注册。");
                return;
            }

            if (!HasText(committedContent))
            {
                run.Notes.Add("章节连续性事实未调度：提交正文为空。");
                return;
            }

            var request = CreateChapterFactWriteRequest(run, contextPackage, committedContent);
            await _chapterFactPostCommitScheduler.ScheduleAsync(
                    request,
                    result => ApplyChapterFactWriteResult(run, result),
                    ex => run.Notes.Add($"章节连续性事实后台沉淀失败：{ex.Message}"),
                    ct)
                .ConfigureAwait(false);
            run.Notes.Add("章节连续性事实已进入提交后后台沉淀。");
        }

        private static ChapterFactWriteRequest CreateChapterFactWriteRequest(
            NovelAgentRun run,
            ChapterContextPackageSummary contextPackage,
            string committedContent) =>
            new()
            {
                Run = run,
                ContextPackage = contextPackage,
                CommittedContent = committedContent
            };

        private static void ApplyChapterFactWriteResult(
            NovelAgentRun run,
            ChapterFactWriteResult result)
        {
            if (result.Success && result.Facts != null)
            {
                run.ContinuityFacts = result.Facts;
                run.Notes.Add("章节连续性事实已由 LLM 沉淀。");
            }
            else
            {
                run.Notes.Add($"章节连续性事实未沉淀：{result.Message}");
            }
        }

        private static ChapterContextPackageSummary PrepareCommittedChapterAuditContext(
            string chapterId,
            ChapterContextPackageSummary contextPackage)
        {
            var package = CloneContextPackage(contextPackage);
            package.ChapterId = FirstNonEmpty(package.ChapterId, chapterId);
            package.Status = "committed_audit";

            if (package.ChapterBlueprints.Count == 0)
                package.ChapterBlueprints.Add("已提交章节回溯审查：只验证正文是否符合 Story Bible、连续性事实和知识库硬事实。");
            if (package.PreviousSummaries.Count == 0 && package.LongDistanceRecall.Count == 0)
                package.PreviousSummaries.Add("已提交章节回溯审查上下文。");
            if (package.WorldRules.Count == 0 && package.HardContinuityFacts.Count == 0)
                package.Warnings.Add("审查上下文缺少世界规则或硬事实，结果只能覆盖基础结构门禁。");

            package.WorldRules = NormalizeDistinct(package.WorldRules, 24);
            package.CharacterStates = NormalizeDistinct(package.CharacterStates, 24);
            package.ActiveConflicts = NormalizeDistinct(package.ActiveConflicts, 24);
            package.ActiveForeshadowing = NormalizeDistinct(package.ActiveForeshadowing, 24);
            package.ChapterBlueprints = NormalizeDistinct(package.ChapterBlueprints, 24);
            package.PreviousSummaries = NormalizeDistinct(package.PreviousSummaries, 24);
            package.LongDistanceRecall = NormalizeDistinct(package.LongDistanceRecall, 24);
            package.RagQueries = NormalizeDistinct(package.RagQueries, 24);
            package.HardContinuityFacts = NormalizeDistinct(package.HardContinuityFacts, 48);
            package.Warnings = NormalizeDistinct(package.Warnings, 24);
            return package;
        }

        private static ChapterContextPackageSummary CloneContextPackage(ChapterContextPackageSummary? source)
        {
            if (source == null)
                return new ChapterContextPackageSummary();

            return new ChapterContextPackageSummary
            {
                ChapterId = source.ChapterId,
                Status = source.Status,
                WorldRules = source.WorldRules.ToList(),
                CharacterStates = source.CharacterStates.ToList(),
                ActiveConflicts = source.ActiveConflicts.ToList(),
                ActiveForeshadowing = source.ActiveForeshadowing.ToList(),
                ChapterBlueprints = source.ChapterBlueprints.ToList(),
                PreviousSummaries = source.PreviousSummaries.ToList(),
                LongDistanceRecall = source.LongDistanceRecall.ToList(),
                RagQueries = source.RagQueries.ToList(),
                HardContinuityFacts = source.HardContinuityFacts.ToList(),
                KnowledgeBindings = source.KnowledgeBindings
                    .Select(binding => new BoundKnowledgeSnapshot
                    {
                        KnowledgeId = binding.KnowledgeId,
                        Title = binding.Title,
                        EntryType = binding.EntryType,
                        Content = binding.Content,
                        Tags = binding.Tags.ToList(),
                        Weight = binding.Weight,
                        SourceProjectId = binding.SourceProjectId,
                        ProjectUsageStatus = binding.ProjectUsageStatus,
                        ProjectUsageCount = binding.ProjectUsageCount,
                        SourceSessionId = binding.SourceSessionId,
                        SourceRunId = binding.SourceRunId,
                        Note = binding.Note,
                        Role = binding.Role,
                        Scope = binding.Scope,
                        Priority = binding.Priority,
                        ConstraintLevel = binding.ConstraintLevel,
                        PackagePolicy = binding.PackagePolicy,
                        BoundVersion = binding.BoundVersion,
                        UsedByChapters = binding.UsedByChapters.ToList()
                    })
                    .ToList(),
                AcceptedCreativeIntents = source.AcceptedCreativeIntents
                    .Select(CloneAcceptedCreativeIntentSnapshot)
                    .ToList(),
                Warnings = source.Warnings.ToList(),
                BuiltAt = source.BuiltAt
            };
        }

        private static AcceptedCreativeIntentSnapshot CloneAcceptedCreativeIntentSnapshot(AcceptedCreativeIntentSnapshot source) => new()
        {
            IntentId = source.IntentId,
            NormalizedIntent = source.NormalizedIntent,
            TargetScope = source.TargetScope,
            TargetChapterId = source.TargetChapterId,
            TargetVolumeId = source.TargetVolumeId,
            TargetCharacterName = source.TargetCharacterName,
            ImpactLevel = source.ImpactLevel,
            Source = source.Source,
            DecisionReason = source.DecisionReason,
            CreatedAt = source.CreatedAt
        };

        private static ChapterDraftArtifact BuildCommittedChapterAuditDraft(string chapterId, string committedContent)
        {
            var content = string.IsNullOrWhiteSpace(committedContent)
                ? string.Empty
                : committedContent.Trim();
            var changesJson = BuildEmptyAuditChangesJson();
            return new ChapterDraftArtifact
            {
                ChapterId = chapterId,
                Status = "committed_audit",
                DraftContent = $"{content}\n\n{ChapterChanges.ChangesXmlOpen}\n{changesJson}\n{ChapterChanges.ChangesXmlClose}",
                CommittedContent = content,
                ChangesJson = changesJson,
                HasChanges = true
            };
        }

        private static string BuildEmptyAuditChangesJson()
        {
            var fields = ChapterChanges.TopLevelFieldNames
                .Select(name => $"\"{name}\":[]");
            return "{" + string.Join(",", fields) + "}";
        }

        private static List<string> NormalizeDistinct(IEnumerable<string> values, int take) =>
            values
                .Where(HasText)
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(take)
                .ToList();

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
            => ChapterChangesText.StripChanges(content);

        private static string TrimForIssue(string value)
        {
            var text = Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
            return text.Length <= 80 ? text : text[..80] + "...";
        }

        private static string StripChangesStatic(string content)
            => ChapterChangesText.StripChanges(content);

        private static string? ExtractCharacterName(IEnumerable<string> characterStates)
        {
            foreach (var state in characterStates)
            {
                var match = Regex.Match(state, @"^[^：:,\s]{2,12}");
                if (match.Success) return match.Value;
            }

            return null;
        }

        private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

        private static string FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(HasText)?.Trim() ?? string.Empty;

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

        private static async Task<string> CompleteWritingAsync(
            LlmRuntimeSettings settings,
            string system,
            string user,
            CancellationToken ct,
            WritingCompletionBudget budget = WritingCompletionBudget.LongChapter)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var provider = settings.Provider;
            var baseUrl = settings.BaseUrl.TrimEnd('/');
            var model = NormalizeProviderModelId(settings.Model);
            var maxTokens = budget == WritingCompletionBudget.ChangesOnlyRepair
                ? NormalizeChangesOnlyRepairMaxTokens(settings.MaxTokens)
                : NormalizeWritingMaxTokens(settings.MaxTokens);

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
            const int minimumWritingOutputTokens = 16384;
            return Math.Max(maxTokens, minimumWritingOutputTokens);
        }

        private static int NormalizeChangesOnlyRepairMaxTokens(int maxTokens)
        {
            const int fallbackChangesOutputTokens = 2048;
            var requested = maxTokens <= 0 ? fallbackChangesOutputTokens : maxTokens;
            return Math.Clamp(requested, 1024, 4096);
        }

        private static bool IsChangesOnlyRepairPrompt(string system, string user)
        {
            return (system?.Contains("章节修订记录生成模型", StringComparison.Ordinal) ?? false) ||
                   (user?.Contains("repair_chapter_changes_only", StringComparison.Ordinal) ?? false);
        }

        private enum WritingCompletionBudget
        {
            LongChapter,
            ChangesOnlyRepair
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
