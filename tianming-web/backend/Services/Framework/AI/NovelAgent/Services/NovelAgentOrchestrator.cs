using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;

namespace TM.Services.Framework.AI.NovelAgent.Services
{
    public sealed class NovelAgentOrchestrator
    {
        private static string ProductionStep(string stageId) => NovelAgentProductionStages.StepToolName(stageId);

        private readonly BookConceptDesigner _bookConceptDesigner;
        private readonly VolumeArcPlanner _volumeArcPlanner;
        private readonly ChapterNoveltyPlanner _chapterNoveltyPlanner;
        private readonly StoryBibleService _storyBibleService;
        private readonly StoryStateSnapshotService _storyStateSnapshotService;
        private readonly ChapterPostGenerationReviewer _postGenerationReviewer;
        private readonly CanonMaintenanceService _canonMaintenanceService;
        private readonly ForeshadowLedgerService _foreshadowLedgerService;
        private readonly CharacterLedgerService _characterLedgerService;
        private readonly CreativeKnowledgeBaseService _creativeKnowledgeBaseService;
        private readonly ITianmingProductionKernel _productionKernel;

        public NovelAgentOrchestrator(
            BookConceptDesigner bookConceptDesigner,
            VolumeArcPlanner volumeArcPlanner,
            ChapterNoveltyPlanner chapterNoveltyPlanner,
            StoryBibleService storyBibleService,
            StoryStateSnapshotService storyStateSnapshotService,
            ChapterPostGenerationReviewer postGenerationReviewer,
            CanonMaintenanceService canonMaintenanceService,
            ForeshadowLedgerService foreshadowLedgerService,
            CharacterLedgerService characterLedgerService,
            CreativeKnowledgeBaseService creativeKnowledgeBaseService,
            ITianmingProductionKernel productionKernel)
        {
            _bookConceptDesigner = bookConceptDesigner;
            _volumeArcPlanner = volumeArcPlanner;
            _chapterNoveltyPlanner = chapterNoveltyPlanner;
            _storyBibleService = storyBibleService;
            _storyStateSnapshotService = storyStateSnapshotService;
            _postGenerationReviewer = postGenerationReviewer;
            _canonMaintenanceService = canonMaintenanceService;
            _foreshadowLedgerService = foreshadowLedgerService;
            _characterLedgerService = characterLedgerService;
            _creativeKnowledgeBaseService = creativeKnowledgeBaseService;
            _productionKernel = productionKernel;
        }

        public async Task<NovelAgentRun> PlanStoryFoundationAsync(
            StoryFoundationRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var constitution = _bookConceptDesigner.BuildConstitution(request);
            var run = CreateRun(request.UserSeed, NovelAgentIntent.CreateStoryFoundation);
            run.Status = NovelAgentRunStatus.Planning;
            run.StoryConstitution = constitution;
            run.MacroCandidates.AddRange(_bookConceptDesigner.GenerateMacroCandidates(request));
            run.Notes.Add("已生成故事创意宪法草案。可按候选序号选择，也可由 Agent 继续推荐路径。");
            run.Steps.Add(new NovelAgentPlanStep
            {
                Name = "生成宏观创意候选",
                Purpose = "围绕题材、读者承诺、世界核心规则和主线冲突生成 3 个整书方案。",
                ToolName = "BookConceptDesigner.GenerateMacroCandidates",
                Status = NovelAgentStepStatus.Completed,
                RiskLevel = NovelToolRiskLevel.Low
            });
            run.Steps.Add(new NovelAgentPlanStep
            {
                Name = "固化 Story Bible",
                Purpose = "将作品宪法、类型风向和禁止方向写入项目设定。",
                ToolName = "StoryBible.CommitConstitution",
                Status = NovelAgentStepStatus.Pending,
                RiskLevel = NovelToolRiskLevel.High
            });
            Touch(run);
            await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);
            return run;
        }

        public async Task<NovelAgentRun> PlanChapterAsync(
            ChapterCreativeRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var storyBible = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            request.Constitution ??= storyBible.Constitution;
            request.VolumeArc ??= FindCurrentVolumeArc(storyBible.VolumeArcs, request.ChapterId);
            request.VolumeBeat ??= FindVolumeBeat(request.VolumeArc, request.ChapterId);
            var storyState = await _storyStateSnapshotService.BuildForChapterAsync(request.ChapterId, ct)
                .ConfigureAwait(false);

            request.ActiveConflicts.AddRange(storyState.ActiveConflicts);
            request.ActiveForeshadowing.AddRange(storyState.ActiveForeshadowing);
            request.CharacterStates.AddRange(storyState.CharacterStates);
            request.UsedPlotPatterns.AddRange(storyState.UsedPlotPatterns);
            request.SimilarContentFragments.AddRange(storyState.SimilarContentFragments);
            if (string.IsNullOrWhiteSpace(request.UserGoal))
                request.UserGoal = storyState.ChapterGoal;
            request.CreativeKnowledge = await _creativeKnowledgeBaseService.RetrieveAsync(
                    request.UserGoal,
                    request.Constitution,
                    request.UsedPlotPatterns.Concat(request.SimilarContentFragments),
                    topK: 10,
                    ct)
                .ConfigureAwait(false);

            var run = CreateRun(request.UserGoal, NovelAgentIntent.PlanChapter);
            run.TargetChapterId = request.ChapterId;
            run.Status = NovelAgentRunStatus.Planning;
            run.StoryState = storyState;
            run.ChapterBrief = _chapterNoveltyPlanner.BuildBrief(request);
            run.Steps.Add(new NovelAgentPlanStep
            {
                Name = "读取故事状态",
                Purpose = "读取 Story Bible、事实快照、伏笔账本、历史章节摘要。",
                ToolName = "NovelLookupTools.GetStoryState",
                Status = NovelAgentStepStatus.Completed,
                RiskLevel = NovelToolRiskLevel.Low
            });
            run.Steps.Add(new NovelAgentPlanStep
            {
                Name = "检索相似桥段",
                Purpose = "从章节蓝图和长距离召回中提取已用剧情模式，避免重复桥段。",
                ToolName = "GuideContextService.BuildContentContext",
                Status = NovelAgentStepStatus.Completed,
                RiskLevel = NovelToolRiskLevel.Low
            });
            run.Steps.Add(new NovelAgentPlanStep
            {
                Name = "检索创意知识库",
                Purpose = "检索类型知识、套路风险、反套路策略和项目已用桥段，作为章节候选评分依据。",
                ToolName = "NovelAgent.RetrieveCreativeKnowledge",
                Status = NovelAgentStepStatus.Completed,
                RiskLevel = NovelToolRiskLevel.Low
            });
            run.Steps.Add(new NovelAgentPlanStep
            {
                Name = "选择章节候选",
                Purpose = "在正式写作前选定本章采用哪个剧情候选，或混合多个候选形成最终简报。",
                ToolName = "NovelAgent.SelectChapterCandidate",
                Status = NovelAgentStepStatus.Pending,
                RiskLevel = NovelToolRiskLevel.Medium
            });
            run.Steps.Add(new NovelAgentPlanStep
            {
                Name = "构建章节上下文包",
                Purpose = "汇总 Story Bible、事实快照、章节蓝图、摘要链和长距离 RAG，形成正式写作上下文。",
                ToolName = ProductionStep(NovelAgentProductionStages.ContextPackage),
                Status = NovelAgentStepStatus.Pending,
                RiskLevel = NovelToolRiskLevel.Low
            });
            run.Steps.Add(new NovelAgentPlanStep
            {
                Name = "生成章节",
                Purpose = "构建章节上下文包，生成正文和 CHANGES，硬门禁通过后才进入书城。",
                ToolName = ProductionStep(NovelAgentProductionStages.DraftGeneration),
                Status = NovelAgentStepStatus.Pending,
                RiskLevel = NovelToolRiskLevel.High
            });
            run.Steps.Add(new NovelAgentPlanStep
            {
                Name = "CHANGES 门禁校验",
                Purpose = "校验 CHANGES 协议、事实快照、蓝图依据和长距离召回连续性。",
                ToolName = ProductionStep(NovelAgentProductionStages.GateValidation),
                Status = NovelAgentStepStatus.Pending,
                RiskLevel = NovelToolRiskLevel.Medium
            });
            run.Steps.Add(new NovelAgentPlanStep
            {
                Name = "质量改写闭环",
                Purpose = "门禁失败时，按失败项构造修复任务，生成正文和 CHANGES 后重跑门禁。",
                ToolName = ProductionStep(NovelAgentProductionStages.DraftRepair),
                Status = NovelAgentStepStatus.Pending,
                RiskLevel = NovelToolRiskLevel.High
            });
            run.Steps.Add(new NovelAgentPlanStep
            {
                Name = "提交成稿",
                Purpose = "门禁通过后提交章节成稿，刷新事实快照、摘要链、长距离 RAG 和依赖影响。",
                ToolName = ProductionStep(NovelAgentProductionStages.ChapterCommit),
                Status = NovelAgentStepStatus.Pending,
                RiskLevel = NovelToolRiskLevel.High
            });
            run.Steps.Add(new NovelAgentPlanStep
            {
                Name = "世界观增量维护",
                Purpose = "将生成后复盘发现的新设定导入 Proposed，后续可升级为 Canon。",
                ToolName = "NovelAgent.ImportProposedCanonFromReview",
                Status = NovelAgentStepStatus.Pending,
                RiskLevel = NovelToolRiskLevel.Medium
            });
            run.Steps.Add(new NovelAgentPlanStep
            {
                Name = "伏笔账本维护",
                Purpose = "将生成后复盘发现的伏笔投放、强化或回收动作写入伏笔账本。",
                ToolName = "NovelAgent.ImportForeshadowFromReview",
                Status = NovelAgentStepStatus.Pending,
                RiskLevel = NovelToolRiskLevel.Medium
            });
            run.Steps.Add(new NovelAgentPlanStep
            {
                Name = "角色状态账本维护",
                Purpose = "将生成后复盘发现的角色目标、秘密、关系、能力代价和心理变化写入角色状态账本。",
                ToolName = "NovelAgent.ImportCharacterStateFromReview",
                Status = NovelAgentStepStatus.Pending,
                RiskLevel = NovelToolRiskLevel.Medium
            });
            Touch(run);
            await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);
            return run;
        }

        public async Task<NovelAgentRun> PlanVolumeArcAsync(
            VolumeArcPlanningRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            request ??= new VolumeArcPlanningRequest();

            var storyBible = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var query = string.Join("；", new[]
                {
                    request.UserGoal,
                    request.VolumeTitle,
                    storyBible.Constitution?.ReaderPromise,
                    storyBible.Constitution?.MainConflictEngine
                }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            var knowledge = await _creativeKnowledgeBaseService.RetrieveAsync(
                    query,
                    storyBible.Constitution,
                    storyBible.VolumeArcs.Select(v => $"{v.Title} {v.VolumePromise} {v.MidpointReversal} {v.Climax}"),
                    topK: 10,
                    ct)
                .ConfigureAwait(false);

            var plan = _volumeArcPlanner.BuildPlan(request, storyBible.Constitution, knowledge);
            var run = CreateRun(request.UserGoal, NovelAgentIntent.PlanVolumeArc);
            run.Status = NovelAgentRunStatus.Planning;
            run.VolumeArcPlan = plan;
            run.Notes.Add("已生成卷级大框架草案。可继续提交到 Story Bible。");
            run.Steps.Add(new NovelAgentPlanStep
            {
                Name = "检索卷级创意知识",
                Purpose = "检索类型原则、套路风险、反套路策略和既有卷规划，避免整卷结构重复。",
                ToolName = "NovelAgent.RetrieveCreativeKnowledge",
                Status = NovelAgentStepStatus.Completed,
                RiskLevel = NovelToolRiskLevel.Low
            });
            run.Steps.Add(new NovelAgentPlanStep
            {
                Name = "生成卷级大框架",
                Purpose = "规划卷目标、阶段反转、高潮、伏笔投放回收、角色弧和世界观增量。",
                ToolName = "VolumeArcPlanner.BuildPlan",
                Status = NovelAgentStepStatus.Completed,
                RiskLevel = NovelToolRiskLevel.Low
            });
            run.Steps.Add(new NovelAgentPlanStep
            {
                Name = "提交卷级规划",
                Purpose = "将卷级规划写入 Story Bible，作为后续章节规划的上层依据。",
                ToolName = "StoryBible.CommitVolumeArc",
                Status = NovelAgentStepStatus.Pending,
                RiskLevel = NovelToolRiskLevel.High
            });
            Touch(run);
            await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);
            return run;
        }

        public Task<StoryStateSnapshot> GetChapterStoryStateAsync(
            string chapterId,
            CancellationToken ct = default)
        {
            return _storyStateSnapshotService.BuildForChapterAsync(chapterId, ct);
        }

        public Task<StoryBibleDocument> GetStoryBibleAsync(CancellationToken ct = default)
        {
            return _storyBibleService.LoadAsync(ct);
        }

        public Task<NovelAgentRunOperationResult> ListRunsAsync(
            int take = 20,
            CancellationToken ct = default)
        {
            return _storyBibleService.ListRunsAsync(take, ct);
        }

        public Task<NovelAgentRunOperationResult> ResumeRunAsync(
            string runId,
            CancellationToken ct = default)
        {
            return _storyBibleService.ResumeRunAsync(runId, ct);
        }

        public Task<NovelAgentRunOperationResult> CancelRunAsync(
            string runId,
            string reason = "",
            CancellationToken ct = default)
        {
            return _storyBibleService.CancelRunAsync(runId, reason, ct);
        }

        public async Task<ChapterCandidateSelectionResult> SelectChapterCandidateAsync(
            string runId,
            string candidateTitles = "",
            string selectionMode = "Recommended",
            string selectionRationale = "",
            bool confirmed = false,
            CancellationToken ct = default,
            int candidateIndex = 0)
        {
            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var run = document.AgentRuns.FirstOrDefault(r =>
                string.Equals(r.RunId, runId?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (run == null)
            {
                return new ChapterCandidateSelectionResult
                {
                    Success = false,
                    Message = "未找到指定 Agent Run。"
                };
            }

            if (run.ChapterBrief == null)
            {
                return new ChapterCandidateSelectionResult
                {
                    Success = false,
                    Message = "该 Agent Run 缺少章节创意简报，无法选择候选。",
                    Run = run
                };
            }

            var requestedTitles = SplitCandidateTitles(candidateTitles);
            var candidates = ResolveChapterCandidateSelection(
                run.ChapterBrief,
                requestedTitles,
                candidateIndex,
                out var selectionError);

            if (candidates.Count == 0)
            {
                return new ChapterCandidateSelectionResult
                {
                    Success = false,
                    Message = string.IsNullOrWhiteSpace(selectionError)
                        ? "没有匹配到候选标题或候选序号。请从 ChapterCreativeBrief.Candidates 中选择，或传 candidateIndex。"
                        : selectionError,
                    Run = run
                };
            }

            var mode = string.IsNullOrWhiteSpace(selectionMode)
                ? (candidates.Count > 1 ? "Mixed" : "Single")
                : selectionMode.Trim();

            ApplyCandidateSelection(run.ChapterBrief, candidates, mode, selectionRationale);
            run.Status = NovelAgentRunStatus.Planning;
            EnsureCandidateSelectionStep(run);
            SetStepStatus(run, "NovelAgent.SelectChapterCandidate", NovelAgentStepStatus.Completed);
            SetStepStatus(run, ProductionStep(NovelAgentProductionStages.DraftGeneration), NovelAgentStepStatus.Pending);
            run.Notes.Add($"章节候选已选定：{run.ChapterBrief.SelectedCandidateTitle}（{run.ChapterBrief.SelectionMode}）。");
            Touch(run);
            await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);

            return new ChapterCandidateSelectionResult
            {
                Success = true,
                Message = "章节候选已选定，后续可执行章节生成。",
                Run = run
            };
        }

        internal async Task<NovelAgentExecutionResult> BuildContextPackageStageAsync(
            string runId,
            CancellationToken ct = default)
        {
            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var run = FindRun(document, runId);
            var invalid = ValidateChapterWritingRun(run, "构建章节上下文包");
            if (invalid != null) return invalid;

            run!.Status = NovelAgentRunStatus.Retrieving;
            SetStepStatus(run, ProductionStep(NovelAgentProductionStages.ContextPackage), NovelAgentStepStatus.Running);
            run.ContextPackage = await _productionKernel.BuildContextPackageAsync(run, document, ct)
                .ConfigureAwait(false);
            SetStepStatus(run, ProductionStep(NovelAgentProductionStages.ContextPackage), NovelAgentStepStatus.Completed);
            run.Notes.Add($"章节上下文包已构建：世界规则 {run.ContextPackage.WorldRules.Count} 条，角色状态 {run.ContextPackage.CharacterStates.Count} 条，长距离召回 {run.ContextPackage.LongDistanceRecall.Count} 条。");
            run.Status = NovelAgentRunStatus.Planning;
            Touch(run);
            await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);

            return new NovelAgentExecutionResult
            {
                Success = true,
                Message = "章节上下文包已构建。",
                ContextPackage = run.ContextPackage,
                Run = run
            };
        }

        internal async Task<NovelAgentExecutionResult> GenerateChapterDraftStageAsync(
            string runId,
            bool confirmed = false,
            CancellationToken ct = default)
        {
            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var run = FindRun(document, runId);
            var invalid = ValidateChapterWritingRun(run, "生成章节草稿");
            if (invalid != null) return invalid;
            var currentRun = run!;

            if (!EnsureChapterCandidateSelected(currentRun))
            {
                return new NovelAgentExecutionResult
                {
                    Success = false,
                    RiskLevel = NovelToolRiskLevel.Medium,
                    Message = "章节创意候选为空，无法自动选定候选继续生成。",
                    Run = currentRun
                };
            }

            currentRun.Status = NovelAgentRunStatus.Executing;
            if (currentRun.ContextPackage == null)
            {
                SetStepStatus(currentRun, ProductionStep(NovelAgentProductionStages.ContextPackage), NovelAgentStepStatus.Running);
                currentRun.ContextPackage = await _productionKernel.BuildContextPackageAsync(currentRun, document, ct)
                    .ConfigureAwait(false);
                SetStepStatus(currentRun, ProductionStep(NovelAgentProductionStages.ContextPackage), NovelAgentStepStatus.Completed);
            }

            SetStepStatus(currentRun, ProductionStep(NovelAgentProductionStages.DraftGeneration), NovelAgentStepStatus.Running);
            currentRun.DraftArtifact = await _productionKernel.GenerateDraftWithChangesAsync(currentRun, currentRun.ContextPackage, ct)
                .ConfigureAwait(false);
            currentRun.GateReport = null;
            currentRun.PostGenerationReview = null;
            SetStepStatus(currentRun, ProductionStep(NovelAgentProductionStages.DraftGeneration), NovelAgentStepStatus.Completed);
            SetStepStatus(currentRun, ProductionStep(NovelAgentProductionStages.GateValidation), NovelAgentStepStatus.Pending);
            var draftHasChanges = currentRun.DraftArtifact.HasChanges;
            var draftReadyMessage = draftHasChanges
                ? "正文草稿已生成，并检测到 CHANGES，等待硬门禁校验。"
                : "正文草稿已生成，但缺少 CHANGES，等待硬门禁校验与自动修复。";
            currentRun.Notes.Add(draftReadyMessage);
            currentRun.Status = NovelAgentRunStatus.Validating;
            Touch(currentRun);
            await _storyBibleService.SaveRunAsync(currentRun, ct).ConfigureAwait(false);

            return new NovelAgentExecutionResult
            {
                Success = true,
                Message = draftHasChanges
                    ? "章节草稿已生成并检测到 CHANGES，下一步执行硬门禁校验。"
                    : "章节草稿已生成但缺少 CHANGES，下一步执行硬门禁校验并进入自动修复。",
                WriterResult = currentRun.DraftArtifact.DraftContent,
                ContextPackage = currentRun.ContextPackage,
                DraftArtifact = currentRun.DraftArtifact,
                Run = currentRun
            };
        }

        internal async Task<NovelAgentExecutionResult> ValidateDraftGateStageAsync(
            string runId,
            CancellationToken ct = default)
        {
            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var run = FindRun(document, runId);
            var invalid = ValidateChapterWritingRun(run, "校验章节草稿");
            if (invalid != null) return invalid;

            if (run!.ContextPackage == null || run.DraftArtifact == null)
            {
                return new NovelAgentExecutionResult
                {
                    Success = false,
                    Message = "当前 Run 缺少上下文包或章节草稿，无法执行硬门禁校验。",
                    ContextPackage = run.ContextPackage,
                    DraftArtifact = run.DraftArtifact,
                    Run = run
                };
            }

            run.Status = NovelAgentRunStatus.Validating;
            SetStepStatus(run, ProductionStep(NovelAgentProductionStages.GateValidation), NovelAgentStepStatus.Running);
            run.GateReport = await _productionKernel.ValidateDraftAsync(run, run.ContextPackage, run.DraftArtifact, ct)
                .ConfigureAwait(false);
            SetStepStatus(run, ProductionStep(NovelAgentProductionStages.GateValidation), run.GateReport.Status == "validated"
                ? NovelAgentStepStatus.Completed
                : NovelAgentStepStatus.Failed);

            if (run.GateReport.Status == "validated")
            {
                run.Status = NovelAgentRunStatus.Planning;
                EnsureReviewStep(run);
                SetStepStatus(run, ProductionStep(NovelAgentProductionStages.QualityReview), NovelAgentStepStatus.Pending);
                SetStepStatus(run, ProductionStep(NovelAgentProductionStages.ChapterCommit), NovelAgentStepStatus.Pending);
                run.Notes.Add("硬门禁已通过，可继续提交成稿。");
            }
            else
            {
                run.Status = NovelAgentRunStatus.Repairing;
                run.DraftArtifact.Status = "gate_failed";
                SetStepStatus(run, ProductionStep(NovelAgentProductionStages.DraftRepair), NovelAgentStepStatus.Pending);
                run.Notes.Add($"硬门禁失败：{string.Join("；", run.GateReport.Issues.Take(4))}");
            }

            Touch(run);
            await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);

            return new NovelAgentExecutionResult
            {
                Success = run.GateReport.Status == "validated",
                RiskLevel = run.GateReport.Status == "validated" ? NovelToolRiskLevel.Medium : NovelToolRiskLevel.High,
                Message = run.GateReport.Status == "validated"
                    ? "章节草稿已通过 CHANGES 硬门禁，下一步可提交成稿。"
                    : $"章节草稿未通过硬门禁：{string.Join("；", run.GateReport.Issues.Take(4))}",
                WriterResult = run.DraftArtifact.DraftContent,
                ContextPackage = run.ContextPackage,
                DraftArtifact = run.DraftArtifact,
                GateReport = run.GateReport,
                Run = run
            };
        }

        internal async Task<NovelAgentExecutionResult> RepairDraftStageAsync(
            string runId,
            bool confirmed = false,
            CancellationToken ct = default)
        {
            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var run = FindRun(document, runId);
            var invalid = ValidateChapterWritingRun(run, "修复章节草稿");
            if (invalid != null) return invalid;

            if (run!.ContextPackage == null || run.DraftArtifact == null || run.GateReport == null)
            {
                return new NovelAgentExecutionResult
                {
                    Success = false,
                    Message = "当前 Run 缺少上下文包、草稿或门禁报告，无法进入修复闭环。",
                    ContextPackage = run.ContextPackage,
                    DraftArtifact = run.DraftArtifact,
                    GateReport = run.GateReport,
                    Run = run
                };
            }

            run.Status = NovelAgentRunStatus.Repairing;
            SetStepStatus(run, ProductionStep(NovelAgentProductionStages.DraftRepair), NovelAgentStepStatus.Running);
            run.DraftArtifact = await _productionKernel.RepairDraftAsync(run, run.ContextPackage, run.DraftArtifact, run.GateReport, ct)
                .ConfigureAwait(false);
            run.PostGenerationReview = null;
            run.GateReport = await _productionKernel.ValidateDraftAsync(run, run.ContextPackage, run.DraftArtifact, ct)
                .ConfigureAwait(false);
            SetStepStatus(run, ProductionStep(NovelAgentProductionStages.GateValidation), run.GateReport.Status == "validated"
                ? NovelAgentStepStatus.Completed
                : NovelAgentStepStatus.Failed);
            SetStepStatus(run, ProductionStep(NovelAgentProductionStages.DraftRepair), run.GateReport.Status == "validated"
                ? NovelAgentStepStatus.Completed
                : NovelAgentStepStatus.Failed);

            if (run.GateReport.Status == "validated")
            {
                run.Status = NovelAgentRunStatus.Planning;
                run.DraftArtifact.Status = "draft_generated";
                EnsureReviewStep(run);
                SetStepStatus(run, ProductionStep(NovelAgentProductionStages.QualityReview), NovelAgentStepStatus.Pending);
                SetStepStatus(run, ProductionStep(NovelAgentProductionStages.ChapterCommit), NovelAgentStepStatus.Pending);
                run.Notes.Add("章节草稿修复后已通过硬门禁，可继续提交成稿。");
            }
            else
            {
                run.Status = NovelAgentRunStatus.Repairing;
                run.DraftArtifact.Status = "gate_failed";
                run.Notes.Add($"章节草稿修复后仍未通过硬门禁：{string.Join("；", run.GateReport.Issues.Take(4))}");
            }

            Touch(run);
            await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);

            return new NovelAgentExecutionResult
            {
                Success = run.GateReport.Status == "validated",
                RiskLevel = NovelToolRiskLevel.High,
                Message = run.GateReport.Status == "validated"
                    ? "章节草稿已修复并通过硬门禁，下一步可提交成稿。"
                    : $"章节草稿修复后仍未通过硬门禁：{string.Join("；", run.GateReport.Issues.Take(4))}",
                WriterResult = run.DraftArtifact.DraftContent,
                ContextPackage = run.ContextPackage,
                DraftArtifact = run.DraftArtifact,
                GateReport = run.GateReport,
                Run = run
            };
        }

        internal async Task<NovelAgentExecutionResult> CommitChapterStageAsync(
            string runId,
            bool confirmed = false,
            CancellationToken ct = default)
        {
            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var run = FindRun(document, runId);
            var invalid = ValidateChapterWritingRun(run, "提交章节成稿");
            if (invalid != null) return invalid;

            if (run!.ContextPackage == null || run.DraftArtifact == null || run.GateReport?.Status != "validated")
            {
                return new NovelAgentExecutionResult
                {
                    Success = false,
                    RiskLevel = NovelToolRiskLevel.High,
                    Message = "当前章节未通过硬门禁，不能提交到书城。",
                    ContextPackage = run.ContextPackage,
                    DraftArtifact = run.DraftArtifact,
                    GateReport = run.GateReport,
                    Run = run
                };
            }

            if (run.PostGenerationReview == null)
            {
                return new NovelAgentExecutionResult
                {
                    Success = false,
                    RiskLevel = NovelToolRiskLevel.Medium,
                    Message = "当前章节已通过结构门禁，但还没有执行质量评审，不能直接提交到书城。",
                    ContextPackage = run.ContextPackage,
                    DraftArtifact = run.DraftArtifact,
                    GateReport = run.GateReport,
                    Run = run
                };
            }

            if (run.PostGenerationReview.RequiresRewrite ||
                run.PostGenerationReview.OverallResult is "Fail" or "Failed")
            {
                return new NovelAgentExecutionResult
                {
                    Success = false,
                    RiskLevel = NovelToolRiskLevel.High,
                    Message = $"当前章节质量评审未通过，需先修复：{run.PostGenerationReview.Summary}",
                    ContextPackage = run.ContextPackage,
                    DraftArtifact = run.DraftArtifact,
                    GateReport = run.GateReport,
                    Run = run
                };
            }

            run.DraftArtifact.Status = "committed";
            run.DraftArtifact.CommittedContent = _productionKernel.StripChanges(run.DraftArtifact.DraftContent);
            run.DraftArtifact.CommittedAt = DateTime.Now;
            run.DependencyImpact = await _productionKernel.CommitChapterAsync(run, run.ContextPackage, run.DraftArtifact, ct)
                .ConfigureAwait(false);
            run.Status = NovelAgentRunStatus.Completed;
            SetStepStatus(run, ProductionStep(NovelAgentProductionStages.ChapterCommit), NovelAgentStepStatus.Completed);
            run.Notes.Add($"章节已提交成稿：{run.DependencyImpact.Summary}");
            Touch(run);
            await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);

            return new NovelAgentExecutionResult
            {
                Success = true,
                Message = "章节已通过硬门禁并提交成稿。",
                WriterResult = run.DraftArtifact.CommittedContent,
                ContextPackage = run.ContextPackage,
                DraftArtifact = run.DraftArtifact,
                GateReport = run.GateReport,
                DependencyImpact = run.DependencyImpact,
                Run = run
            };
        }

        public async Task<NovelAgentExecutionResult> AuditCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            CancellationToken ct = default)
        {
            var report = await _productionKernel.AuditCommittedChapterAsync(
                    chapterId,
                    committedContent,
                    contextPackage,
                    ct)
                .ConfigureAwait(false);
            var run = new NovelAgentRun
            {
                Intent = NovelAgentIntent.ValidateContinuity,
                Status = report.Status == "validated" ? NovelAgentRunStatus.Completed : NovelAgentRunStatus.Failed,
                TargetChapterId = chapterId,
                UserGoal = "审查已提交章节正文的连续性和知识库硬事实。",
                ContextPackage = contextPackage,
                DraftArtifact = new ChapterDraftArtifact
                {
                    ChapterId = chapterId,
                    Status = "committed_audit",
                    CommittedContent = _productionKernel.StripChanges(committedContent)
                },
                GateReport = report
            };
            Touch(run);
            try
            {
                await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[NovelAgentOrchestrator] 已提交章节审查 Run 记录失败（不影响审查结果）：{ex.Message}");
            }

            return new NovelAgentExecutionResult
            {
                Success = true,
                RiskLevel = NovelToolRiskLevel.Medium,
                Message = report.Status == "validated"
                    ? "已提交章节通过回溯审查。"
                    : $"已提交章节回溯审查发现问题：{string.Join("；", report.Issues.Take(4))}",
                WriterResult = committedContent,
                ContextPackage = contextPackage,
                DraftArtifact = run.DraftArtifact,
                GateReport = report,
                Run = run
            };
        }

        public async Task<NovelAgentExecutionResult> ReviseCommittedChapterAsync(
            string chapterId,
            string committedContent,
            ChapterContextPackageSummary contextPackage,
            string revisionGoal,
            CancellationToken ct = default)
        {
            var result = await _productionKernel.ReviseCommittedChapterAsync(
                    chapterId,
                    committedContent,
                    contextPackage,
                    revisionGoal,
                    ct)
                .ConfigureAwait(false);
            if (result.Run != null)
            {
                Touch(result.Run);
                try
                {
                    await _storyBibleService.SaveRunAsync(result.Run, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[NovelAgentOrchestrator] 已提交章节修订 Run 记录失败（不影响章节保存结果）：{ex.Message}");
                }
            }

            return result;
        }

        public async Task<NovelAgentExecutionResult> RefreshProjectIndexesAsync(
            string runId,
            CancellationToken ct = default)
        {
            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var run = FindRun(document, runId);
            var invalid = ValidateChapterWritingRun(run, "刷新项目索引");
            if (invalid != null) return invalid;

            if (run!.DraftArtifact == null)
            {
                return new NovelAgentExecutionResult
                {
                    Success = false,
                    Message = "当前 Run 还没有章节草稿，无法刷新章节索引。",
                    Run = run
                };
            }

            run.DependencyImpact = _productionKernel.RefreshIndexesAndAnalyzeImpact(run, run.DraftArtifact);
            run.Notes.Add(run.DependencyImpact.Summary);
            Touch(run);
            await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);

            return new NovelAgentExecutionResult
            {
                Success = true,
                Message = run.DependencyImpact.Summary,
                ContextPackage = run.ContextPackage,
                DraftArtifact = run.DraftArtifact,
                GateReport = run.GateReport,
                DependencyImpact = run.DependencyImpact,
                Run = run
            };
        }

        public async Task<NovelAgentExecutionResult> ReviewGeneratedChapterAsync(
            string runId,
            CancellationToken ct = default)
        {
            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var run = document.AgentRuns.FirstOrDefault(r =>
                string.Equals(r.RunId, runId?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (run == null)
            {
                return new NovelAgentExecutionResult
                {
                    Success = false,
                    Message = "未找到指定 Agent Run。"
                };
            }

            if (string.IsNullOrWhiteSpace(run.TargetChapterId))
            {
                return new NovelAgentExecutionResult
                {
                    Success = false,
                    Message = "该 Agent Run 缺少目标章节ID，无法执行生成后复盘。",
                    Run = run
                };
            }

            try
            {
                run.Status = NovelAgentRunStatus.Validating;
                EnsureReviewStep(run);
                SetStepStatus(run, ProductionStep(NovelAgentProductionStages.QualityReview), NovelAgentStepStatus.Running);
                run.Notes.Add("开始重跑 Agent 生成后复盘。");
                Touch(run);
                await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);

                run.PostGenerationReview = await _postGenerationReviewer.ReviewAsync(run, ct)
                    .ConfigureAwait(false);

                run.Status = run.PostGenerationReview.RequiresRewrite
                    ? NovelAgentRunStatus.Repairing
                    : NovelAgentRunStatus.Planning;
                SetStepStatus(run, ProductionStep(NovelAgentProductionStages.QualityReview), NovelAgentStepStatus.Completed);
                run.Notes.Add($"生成后复盘完成：{run.PostGenerationReview.Summary}");
                Touch(run);
                await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);

                return new NovelAgentExecutionResult
                {
                    Success = true,
                    Message = "生成后复盘已完成。",
                    Run = run
                };
            }
            catch (Exception ex)
            {
                run.Status = NovelAgentRunStatus.Failed;
                SetStepStatus(run, ProductionStep(NovelAgentProductionStages.QualityReview), NovelAgentStepStatus.Failed);
                run.Notes.Add($"生成后复盘失败：{ex.Message}");
                Touch(run);
                await _storyBibleService.SaveRunAsync(run, CancellationToken.None).ConfigureAwait(false);

                return new NovelAgentExecutionResult
                {
                    Success = false,
                    Message = $"生成后复盘失败：{ex.Message}",
                    Run = run
                };
            }
        }

        public async Task<CanonMaintenanceResult> ImportProposedCanonFromReviewAsync(
            string runId,
            CancellationToken ct = default)
        {
            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var run = document.AgentRuns.FirstOrDefault(r =>
                string.Equals(r.RunId, runId?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (run == null)
            {
                return new CanonMaintenanceResult
                {
                    Success = false,
                    Message = "未找到指定 Agent Run。"
                };
            }

            run.PostGenerationReview ??= await _postGenerationReviewer.ReviewAsync(run, ct)
                .ConfigureAwait(false);

            EnsureCanonMaintenanceStep(run);
            SetStepStatus(run, "NovelAgent.ImportProposedCanonFromReview", NovelAgentStepStatus.Running);
            Touch(run);
            await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);

            var result = await _canonMaintenanceService.ImportProposedEntriesFromReviewAsync(run, ct)
                .ConfigureAwait(false);

            SetStepStatus(run, "NovelAgent.ImportProposedCanonFromReview", NovelAgentStepStatus.Completed);
            if (result.ImportedEntries.Count == 0)
            {
                EnsureCanonMaintenanceStep(run);
                SetStepStatus(run, "NovelAgent.PromoteProposedCanonFromRun", NovelAgentStepStatus.Skipped);
                SetStepStatus(run, "NovelAgent.RejectProposedCanonFromRun", NovelAgentStepStatus.Skipped);
                run.Status = ResolveStatusAfterStep(run);
            }
            else
            {
                SetStepStatus(run, "NovelAgent.PromoteProposedCanonFromRun", NovelAgentStepStatus.Pending);
                run.Status = ResolveStatusAfterStep(run);
            }
            run.Notes.Add(result.Message);
            Touch(run);
            await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);
            result.Run = run;
            return result;
        }

        public async Task<ForeshadowMaintenanceResult> ImportForeshadowFromReviewAsync(
            string runId,
            CancellationToken ct = default)
        {
            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var run = document.AgentRuns.FirstOrDefault(r =>
                string.Equals(r.RunId, runId?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (run == null)
            {
                return new ForeshadowMaintenanceResult
                {
                    Success = false,
                    Message = "未找到指定 Agent Run。"
                };
            }

            run.PostGenerationReview ??= await _postGenerationReviewer.ReviewAsync(run, ct)
                .ConfigureAwait(false);

            EnsureForeshadowLedgerStep(run);
            SetStepStatus(run, "NovelAgent.ImportForeshadowFromReview", NovelAgentStepStatus.Running);
            Touch(run);
            await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);

            var result = await _foreshadowLedgerService.ImportProposedEntriesFromReviewAsync(run, ct)
                .ConfigureAwait(false);

            SetStepStatus(run, "NovelAgent.ImportForeshadowFromReview", NovelAgentStepStatus.Completed);
            if (result.ConflictEntries.Count > 0)
            {
                SetStepStatus(run, "NovelAgent.ConfirmForeshadowStatusFromRun", NovelAgentStepStatus.Pending);
                run.Status = ResolveStatusAfterStep(run);
            }
            else
            {
                SetStepStatus(run, "NovelAgent.ConfirmForeshadowStatusFromRun", NovelAgentStepStatus.Skipped);
                run.Status = ResolveStatusAfterStep(run);
            }

            run.Notes.Add(result.Message);
            Touch(run);
            await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);
            result.Run = run;
            return result;
        }

        public async Task<ForeshadowMaintenanceResult> ConfirmForeshadowStatusFromRunAsync(
            string runId,
            string entryIds = "",
            bool confirmed = false,
            CancellationToken ct = default)
        {
            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var run = document.AgentRuns.FirstOrDefault(r =>
                string.Equals(r.RunId, runId?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (run == null)
            {
                return new ForeshadowMaintenanceResult
                {
                    Success = false,
                    Message = "未找到指定 Agent Run。"
                };
            }

            EnsureForeshadowLedgerStep(run);
            SetStepStatus(run, "NovelAgent.ConfirmForeshadowStatusFromRun", NovelAgentStepStatus.Running);
            run.Notes.Add("Agent 应用高风险伏笔状态变化。");
            Touch(run);
            await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);

            var result = await _foreshadowLedgerService.ConfirmForeshadowStatusFromRunAsync(
                run,
                entryIds,
                confirmed: true,
                ct).ConfigureAwait(false);

            SetStepStatus(run, "NovelAgent.ConfirmForeshadowStatusFromRun", NovelAgentStepStatus.Completed);
            run.Status = ResolveStatusAfterStep(run);
            run.Notes.Add(result.Message);
            Touch(run);
            await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);
            result.Run = run;
            return result;
        }

        public async Task<CharacterMaintenanceResult> ImportCharacterStateFromReviewAsync(
            string runId,
            CancellationToken ct = default)
        {
            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var run = document.AgentRuns.FirstOrDefault(r =>
                string.Equals(r.RunId, runId?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (run == null)
            {
                return new CharacterMaintenanceResult
                {
                    Success = false,
                    Message = "未找到指定 Agent Run。"
                };
            }

            run.PostGenerationReview ??= await _postGenerationReviewer.ReviewAsync(run, ct)
                .ConfigureAwait(false);

            EnsureCharacterLedgerStep(run);
            SetStepStatus(run, "NovelAgent.ImportCharacterStateFromReview", NovelAgentStepStatus.Running);
            Touch(run);
            await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);

            var result = await _characterLedgerService.ImportProposedEntriesFromReviewAsync(run, ct)
                .ConfigureAwait(false);

            SetStepStatus(run, "NovelAgent.ImportCharacterStateFromReview", NovelAgentStepStatus.Completed);
            if (result.ConflictEntries.Count > 0)
            {
                SetStepStatus(run, "NovelAgent.ConfirmCharacterStateFromRun", NovelAgentStepStatus.Pending);
                run.Status = ResolveStatusAfterStep(run);
            }
            else
            {
                SetStepStatus(run, "NovelAgent.ConfirmCharacterStateFromRun", NovelAgentStepStatus.Skipped);
                run.Status = ResolveStatusAfterStep(run);
            }

            run.Notes.Add(result.Message);
            Touch(run);
            await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);
            result.Run = run;
            return result;
        }

        public async Task<CharacterMaintenanceResult> ConfirmCharacterStateFromRunAsync(
            string runId,
            string entryIds = "",
            bool confirmed = false,
            CancellationToken ct = default)
        {
            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var run = document.AgentRuns.FirstOrDefault(r =>
                string.Equals(r.RunId, runId?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (run == null)
            {
                return new CharacterMaintenanceResult
                {
                    Success = false,
                    Message = "未找到指定 Agent Run。"
                };
            }

            EnsureCharacterLedgerStep(run);
            SetStepStatus(run, "NovelAgent.ConfirmCharacterStateFromRun", NovelAgentStepStatus.Running);
            run.Notes.Add("Agent 应用高风险角色状态变化。");
            Touch(run);
            await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);

            var result = await _characterLedgerService.ConfirmCharacterStatusFromRunAsync(
                run,
                entryIds,
                confirmed: true,
                ct).ConfigureAwait(false);

            SetStepStatus(run, "NovelAgent.ConfirmCharacterStateFromRun", NovelAgentStepStatus.Completed);
            run.Status = ResolveStatusAfterStep(run);
            run.Notes.Add(result.Message);
            Touch(run);
            await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);
            result.Run = run;
            return result;
        }

        public async Task<CanonMaintenanceResult> PromoteProposedCanonFromRunAsync(
            string runId,
            string entryIds = "",
            bool confirmed = false,
            CancellationToken ct = default)
        {
            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var run = document.AgentRuns.FirstOrDefault(r =>
                string.Equals(r.RunId, runId?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (run == null)
            {
                return new CanonMaintenanceResult
                {
                    Success = false,
                    Message = "未找到指定 Agent Run。"
                };
            }

            EnsureCanonMaintenanceStep(run);
            SetStepStatus(run, "NovelAgent.PromoteProposedCanonFromRun", NovelAgentStepStatus.Running);
            run.Notes.Add("Agent 将 Proposed 设定升级为 Canon。");
            Touch(run);
            await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);

            var result = await _canonMaintenanceService.PromoteProposedEntriesAsync(
                run,
                entryIds,
                confirmed: true,
                ct).ConfigureAwait(false);

            SetStepStatus(run, "NovelAgent.PromoteProposedCanonFromRun", NovelAgentStepStatus.Completed);
            SetStepStatus(run, "NovelAgent.RejectProposedCanonFromRun", NovelAgentStepStatus.Skipped);
            run.Status = ResolveStatusAfterStep(run);
            run.Notes.Add(result.Message);
            Touch(run);
            await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);
            result.Run = run;
            return result;
        }

        public async Task<CanonMaintenanceResult> RejectProposedCanonFromRunAsync(
            string runId,
            string entryIds = "",
            string reason = "",
            bool confirmed = false,
            CancellationToken ct = default)
        {
            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var run = document.AgentRuns.FirstOrDefault(r =>
                string.Equals(r.RunId, runId?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (run == null)
            {
                return new CanonMaintenanceResult
                {
                    Success = false,
                    Message = "未找到指定 Agent Run。"
                };
            }

            EnsureCanonMaintenanceStep(run);
            SetStepStatus(run, "NovelAgent.RejectProposedCanonFromRun", NovelAgentStepStatus.Running);
            run.Notes.Add("Agent 标记 Proposed 设定为 Rejected。");
            Touch(run);
            await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);

            var result = await _canonMaintenanceService.RejectProposedEntriesAsync(
                run,
                entryIds,
                reason,
                confirmed: true,
                ct).ConfigureAwait(false);

            SetStepStatus(run, "NovelAgent.RejectProposedCanonFromRun", NovelAgentStepStatus.Completed);
            SetStepStatus(run, "NovelAgent.PromoteProposedCanonFromRun", NovelAgentStepStatus.Skipped);
            run.Status = ResolveStatusAfterStep(run);
            run.Notes.Add(result.Message);
            Touch(run);
            await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);
            result.Run = run;
            return result;
        }

        public Task<CreativeKnowledgeRetrievalResult> RetrieveCreativeKnowledgeAsync(
            string query,
            CancellationToken ct = default)
        {
            return _creativeKnowledgeBaseService.RetrieveAsync(query, null, null, topK: 10, ct);
        }

        public async Task<StoryBibleCommitResult> CommitStoryFoundationAsync(
            string runId,
            bool overwrite = false,
            bool confirmed = false,
            CancellationToken ct = default)
        {
            return await CommitStoryFoundationAsync(
                runId,
                overwrite,
                confirmed,
                string.Empty,
                0,
                ct).ConfigureAwait(false);
        }

        public async Task<StoryBibleCommitResult> CommitStoryFoundationAsync(
            string runId,
            bool overwrite,
            bool confirmed,
            string selectedMacroCandidateId,
            int selectedMacroCandidateIndex,
            CancellationToken ct = default)
        {
            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var run = document.AgentRuns.Find(r =>
                string.Equals(r.RunId, runId?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (run?.StoryConstitution == null)
            {
                return new StoryBibleCommitResult
                {
                    Success = false,
                    Message = "未找到可提交的 Story Foundation Run，或该 Run 没有故事创意宪法。",
                    Document = document
                };
            }

            var selectedCandidate = ResolveSelectedMacroCandidate(
                run.MacroCandidates,
                selectedMacroCandidateId,
                selectedMacroCandidateIndex,
                out var selectionError);
            if (!string.IsNullOrWhiteSpace(selectionError))
            {
                return new StoryBibleCommitResult
                {
                    Success = false,
                    Message = selectionError,
                    Document = document
                };
            }

            var constitution = ApplySelectedMacroCandidate(
                run.StoryConstitution,
                selectedCandidate);
            var macroCandidates = selectedCandidate == null
                ? run.MacroCandidates
                : new List<MacroStoryConceptCandidate> { selectedCandidate };

            var result = await _storyBibleService.CommitConstitutionAsync(
                constitution,
                macroCandidates.Count == 0 ? run.MacroCandidates : macroCandidates,
                run.RunId,
                overwrite,
                confirmed,
                ct).ConfigureAwait(false);

            if (result.Success)
            {
                run.Status = NovelAgentRunStatus.Completed;
                SetStepStatus(run, "StoryBible.CommitConstitution", NovelAgentStepStatus.Completed);
                run.Notes.Add("Story Bible 已由 Agent 提交。");
                Touch(run);
                await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);
                result.Document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            }

            return result;
        }
        public async Task<StoryBibleCommitResult> CommitVolumeArcAsync(
            string runId,
            bool overwrite = false,
            bool confirmed = false,
            CancellationToken ct = default)
        {
            var document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            var run = document.AgentRuns.Find(r =>
                string.Equals(r.RunId, runId?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (run?.VolumeArcPlan == null)
            {
                return new StoryBibleCommitResult
                {
                    Success = false,
                    Message = "未找到可提交的卷级规划 Run，或该 Run 没有卷级规划草案。",
                    Document = document
                };
            }

            var result = await _storyBibleService.CommitVolumeArcAsync(
                run.VolumeArcPlan,
                run.RunId,
                overwrite,
                confirmed,
                ct).ConfigureAwait(false);

            if (result.Success)
            {
                run.Status = NovelAgentRunStatus.Completed;
                run.VolumeArcPlan.Status = VolumeArcStatus.Canon;
                SetStepStatus(run, "StoryBible.CommitVolumeArc", NovelAgentStepStatus.Completed);
                run.Notes.Add("卷级规划已由 Agent 提交到 Story Bible。");
                Touch(run);
                await _storyBibleService.SaveRunAsync(run, ct).ConfigureAwait(false);
                result.Document = await _storyBibleService.LoadAsync(ct).ConfigureAwait(false);
            }

            return result;
        }

        public Task<StoryBibleCommitResult> AddLedgerEntryAsync(
            CanonLedgerEntry entry,
            bool confirmed = false,
            CancellationToken ct = default)
        {
            return _storyBibleService.AddLedgerEntryAsync(entry, confirmed, ct);
        }

        public Task<StoryBibleCommitResult> UpdateLedgerEntryStatusAsync(
            string entryId,
            CanonLedgerEntryStatus status,
            string conflictCheck = "",
            bool confirmed = false,
            CancellationToken ct = default)
        {
            return _storyBibleService.UpdateLedgerEntryStatusAsync(entryId, status, conflictCheck, confirmed, ct);
        }

        public Task<ForeshadowMaintenanceResult> AddForeshadowEntryAsync(
            ForeshadowLedgerEntry entry,
            bool confirmed = false,
            CancellationToken ct = default)
        {
            return _storyBibleService.AddForeshadowEntryAsync(entry, confirmed, ct);
        }

        public Task<ForeshadowMaintenanceResult> UpdateForeshadowEntryStatusAsync(
            string entryId,
            ForeshadowLedgerStatus status,
            string chapterId = "",
            string note = "",
            bool confirmed = false,
            CancellationToken ct = default)
        {
            return _storyBibleService.UpdateForeshadowEntryStatusAsync(
                entryId,
                status,
                chapterId,
                note,
                confirmed,
                ct);
        }

        public Task<CharacterMaintenanceResult> AddCharacterEntryAsync(
            CharacterLedgerEntry entry,
            bool confirmed = false,
            CancellationToken ct = default)
        {
            return _storyBibleService.AddCharacterEntryAsync(entry, confirmed, ct);
        }

        public Task<CharacterMaintenanceResult> UpdateCharacterEntryStatusAsync(
            string entryId,
            CharacterLedgerStatus status,
            string chapterId = "",
            string note = "",
            bool confirmed = false,
            CancellationToken ct = default)
        {
            return _storyBibleService.UpdateCharacterEntryStatusAsync(
                entryId,
                status,
                chapterId,
                note,
                confirmed,
                ct);
        }

        private static NovelAgentRun? FindRun(StoryBibleDocument document, string runId)
        {
            return document.AgentRuns.FirstOrDefault(r =>
                string.Equals(r.RunId, runId?.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static NovelAgentExecutionResult? ValidateChapterWritingRun(
            NovelAgentRun? run,
            string actionName)
        {
            if (run == null)
            {
                return new NovelAgentExecutionResult
                {
                    Success = false,
                    Message = "未找到指定 Agent Run。"
                };
            }

            if (run.Intent != NovelAgentIntent.PlanChapter || run.ChapterBrief == null)
            {
                return new NovelAgentExecutionResult
                {
                    Success = false,
                    Message = $"该 Agent Run 不是章节创意简报 Run，无法{actionName}。",
                    Run = run
                };
            }

            if (string.IsNullOrWhiteSpace(run.TargetChapterId))
            {
                return new NovelAgentExecutionResult
                {
                    Success = false,
                    Message = $"该 Agent Run 缺少目标章节ID，无法{actionName}。",
                    Run = run
                };
            }

            if (run.Status == NovelAgentRunStatus.Completed && !string.Equals(actionName, "刷新项目索引", StringComparison.OrdinalIgnoreCase))
            {
                return new NovelAgentExecutionResult
                {
                    Success = true,
                    Message = "该 Agent Run 已完成，无需重复执行。",
                    Run = run
                };
            }

            return null;
        }

        private static NovelAgentRun CreateRun(string userGoal, NovelAgentIntent intent)
        {
            return new NovelAgentRun
            {
                UserGoal = userGoal ?? string.Empty,
                Intent = intent,
                Status = NovelAgentRunStatus.Planning
            };
        }

        private static void Touch(NovelAgentRun run)
        {
            run.UpdatedAt = DateTime.Now;
        }

        private static void SetStepStatus(
            NovelAgentRun run,
            string toolName,
            NovelAgentStepStatus status)
        {
            var step = run.Steps.FirstOrDefault(s =>
                string.Equals(s.ToolName, toolName, StringComparison.OrdinalIgnoreCase));
            if (step != null)
                step.Status = status;
        }

        private static NovelAgentRunStatus ResolveStatusAfterStep(NovelAgentRun run)
        {
            NormalizeRunnableStepState(run);
            if (run.Steps.Any(s => s.Status == NovelAgentStepStatus.Pending))
                return NovelAgentRunStatus.Planning;
            return NovelAgentRunStatus.Completed;
        }

        private static void NormalizeRunnableStepState(NovelAgentRun run)
        {
            foreach (var step in run.Steps)
            {
                step.RequiresConfirmation = false;
                if (step.Status == NovelAgentStepStatus.WaitingUser)
                    step.Status = NovelAgentStepStatus.Pending;
            }

            if (run.Status == NovelAgentRunStatus.AwaitingConfirmation)
                run.Status = run.Steps.Any(s => s.Status == NovelAgentStepStatus.Pending)
                    ? NovelAgentRunStatus.Planning
                    : NovelAgentRunStatus.Completed;
        }

        private static void EnsureReviewStep(NovelAgentRun run)
        {
            if (run.Steps.Any(s => string.Equals(
                    s.ToolName,
                    ProductionStep(NovelAgentProductionStages.QualityReview),
                    StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            run.Steps.Add(new NovelAgentPlanStep
            {
                Name = "生成后复盘",
                Purpose = "检查章节是否重复旧桥段、违反 Story Bible、产生新设定，并生成下一章建议。",
                ToolName = ProductionStep(NovelAgentProductionStages.QualityReview),
                Status = NovelAgentStepStatus.Pending,
                RiskLevel = NovelToolRiskLevel.Medium
            });
        }

        private static void EnsureCandidateSelectionStep(NovelAgentRun run)
        {
            if (run.Steps.Any(s => string.Equals(
                    s.ToolName,
                    "NovelAgent.SelectChapterCandidate",
                    StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            run.Steps.Add(new NovelAgentPlanStep
            {
                Name = "选择章节候选",
                Purpose = "在正式写作前选定本章采用哪个剧情候选，或混合多个候选形成最终简报。",
                ToolName = "NovelAgent.SelectChapterCandidate",
                Status = NovelAgentStepStatus.Pending,
                RiskLevel = NovelToolRiskLevel.Medium
            });
        }

        private static void EnsureCanonMaintenanceStep(NovelAgentRun run)
        {
            if (!run.Steps.Any(s => string.Equals(
                    s.ToolName,
                    "NovelAgent.ImportProposedCanonFromReview",
                    StringComparison.OrdinalIgnoreCase)))
            {
                run.Steps.Add(new NovelAgentPlanStep
                {
                    Name = "导入 Proposed 设定",
                    Purpose = "将生成后复盘发现的新设定导入 Canon Ledger 的 Proposed 状态。",
                    ToolName = "NovelAgent.ImportProposedCanonFromReview",
                    Status = NovelAgentStepStatus.Pending,
                    RiskLevel = NovelToolRiskLevel.Medium
                });
            }

            if (!run.Steps.Any(s => string.Equals(
                    s.ToolName,
                    "NovelAgent.PromoteProposedCanonFromRun",
                    StringComparison.OrdinalIgnoreCase)))
            {
                run.Steps.Add(new NovelAgentPlanStep
                {
                    Name = "升级 Canon 设定",
                    Purpose = "将 Proposed 设定升级为 Canon，作为后续正文依据。",
                    ToolName = "NovelAgent.PromoteProposedCanonFromRun",
                    Status = NovelAgentStepStatus.Pending,
                    RiskLevel = NovelToolRiskLevel.High
                });
            }

            if (!run.Steps.Any(s => string.Equals(
                    s.ToolName,
                    "NovelAgent.RejectProposedCanonFromRun",
                    StringComparison.OrdinalIgnoreCase)))
            {
                run.Steps.Add(new NovelAgentPlanStep
                {
                    Name = "拒绝 Proposed 设定",
                    Purpose = "将 Proposed 设定标记为 Rejected，避免后续章节把它当作正文依据。",
                    ToolName = "NovelAgent.RejectProposedCanonFromRun",
                    Status = NovelAgentStepStatus.Pending,
                    RiskLevel = NovelToolRiskLevel.Medium
                });
            }
        }

        private static void EnsureForeshadowLedgerStep(NovelAgentRun run)
        {
            if (!run.Steps.Any(s => string.Equals(
                    s.ToolName,
                    "NovelAgent.ImportForeshadowFromReview",
                    StringComparison.OrdinalIgnoreCase)))
            {
                run.Steps.Add(new NovelAgentPlanStep
                {
                    Name = "导入伏笔变化",
                    Purpose = "将生成后复盘发现的伏笔投放、强化或候选回收动作写入伏笔账本。",
                    ToolName = "NovelAgent.ImportForeshadowFromReview",
                    Status = NovelAgentStepStatus.Pending,
                    RiskLevel = NovelToolRiskLevel.Medium
                });
            }

            if (!run.Steps.Any(s => string.Equals(
                    s.ToolName,
                    "NovelAgent.ConfirmForeshadowStatusFromRun",
                    StringComparison.OrdinalIgnoreCase)))
            {
                run.Steps.Add(new NovelAgentPlanStep
                {
                    Name = "应用伏笔回收",
                    Purpose = "将高风险伏笔回收、废弃或冲突状态写入伏笔账本。",
                    ToolName = "NovelAgent.ConfirmForeshadowStatusFromRun",
                    Status = NovelAgentStepStatus.Pending,
                    RiskLevel = NovelToolRiskLevel.High
                });
            }
        }

        private static void EnsureCharacterLedgerStep(NovelAgentRun run)
        {
            if (!run.Steps.Any(s => string.Equals(
                    s.ToolName,
                    "NovelAgent.ImportCharacterStateFromReview",
                    StringComparison.OrdinalIgnoreCase)))
            {
                run.Steps.Add(new NovelAgentPlanStep
                {
                    Name = "导入角色状态变化",
                    Purpose = "将生成后复盘发现的角色目标、秘密、关系、能力代价和心理变化写入角色状态账本。",
                    ToolName = "NovelAgent.ImportCharacterStateFromReview",
                    Status = NovelAgentStepStatus.Pending,
                    RiskLevel = NovelToolRiskLevel.Medium
                });
            }

            if (!run.Steps.Any(s => string.Equals(
                    s.ToolName,
                    "NovelAgent.ConfirmCharacterStateFromRun",
                    StringComparison.OrdinalIgnoreCase)))
            {
                run.Steps.Add(new NovelAgentPlanStep
                {
                    Name = "应用高风险角色状态",
                    Purpose = "将角色死亡、身份改写、秘密揭露、关系反转或能力规则变化写入角色状态账本。",
                    ToolName = "NovelAgent.ConfirmCharacterStateFromRun",
                    Status = NovelAgentStepStatus.Pending,
                    RiskLevel = NovelToolRiskLevel.High
                });
            }
        }

        private static bool EnsureChapterCandidateSelected(NovelAgentRun run)
        {
            var brief = run.ChapterBrief;
            if (brief == null)
                return false;

            if (!string.IsNullOrWhiteSpace(brief.SelectedCandidateTitle))
                return true;

            var selected = brief.Candidates
                .FirstOrDefault(c => string.Equals(c.Title, brief.RecommendedCandidateTitle, StringComparison.OrdinalIgnoreCase))
                ?? brief.Candidates.OrderByDescending(c => c.TotalScore).FirstOrDefault();
            if (selected == null)
                return false;

            ApplyCandidateSelection(
                brief,
                new List<PlotCandidate> { selected },
                string.IsNullOrWhiteSpace(brief.RecommendedCandidateTitle) ? "AgentAuto" : "Recommended",
                string.IsNullOrWhiteSpace(selected.RecommendationReason)
                    ? "Agent 自动选择最高分候选继续执行。"
                    : selected.RecommendationReason);
            return true;
        }

        private static void ApplyCandidateSelection(
            ChapterCreativeBrief brief,
            List<PlotCandidate> candidates,
            string selectionMode,
            string selectionRationale)
        {
            if (candidates.Count == 1)
            {
                var selected = candidates[0];
                brief.CoreIdea = selected.CoreTwist;
                brief.ConflictMove = selected.ConflictMove;
                brief.CharacterChoice = selected.CharacterChoice;
                brief.CostOrConsequence = selected.CostOrConsequence;
                brief.SelectedCandidateTitle = selected.Title;
                brief.SelectionMode = $"{NormalizeSelectionMode(selectionMode, "Single")}Confirmed";
                brief.SelectionRationale = string.IsNullOrWhiteSpace(selectionRationale)
                    ? selected.RecommendationReason
                    : selectionRationale.Trim();
                return;
            }

            var ordered = candidates.OrderByDescending(c => c.TotalScore).ToList();
            brief.CoreIdea = string.Join(" + ", ordered.Select(c => c.CoreTwist));
            brief.ConflictMove = string.Join(" / ", ordered.Select(c => c.ConflictMove).Distinct());
            brief.CharacterChoice = ordered.First().CharacterChoice;
            brief.CostOrConsequence = string.Join("；", ordered.Select(c => c.CostOrConsequence).Distinct());
            brief.SelectedCandidateTitle = string.Join(" + ", ordered.Select(c => c.Title));
            brief.SelectionMode = $"{NormalizeSelectionMode(selectionMode, "Mixed")}Confirmed";
            brief.SelectionRationale = string.IsNullOrWhiteSpace(selectionRationale)
                ? $"混合 {ordered.Count} 个候选：保留最高分候选的角色选择，并叠加其余候选的冲突推进和代价。"
                : selectionRationale.Trim();
        }

        private static string NormalizeSelectionMode(string selectionMode, string fallback)
        {
            if (string.IsNullOrWhiteSpace(selectionMode)) return fallback;
            var mode = selectionMode.Trim();
            return mode.EndsWith("Confirmed", StringComparison.OrdinalIgnoreCase)
                ? mode.Substring(0, mode.Length - "Confirmed".Length)
                : mode;
        }

        private static List<PlotCandidate> ResolveChapterCandidateSelection(
            ChapterCreativeBrief brief,
            IReadOnlyList<string> requestedTitles,
            int candidateIndex,
            out string error)
        {
            error = string.Empty;
            var selected = new List<PlotCandidate>();
            var candidates = brief.Candidates;
            if (candidates.Count == 0)
                return selected;

            if (candidateIndex > 0)
            {
                if (candidateIndex > candidates.Count)
                {
                    error = $"候选序号 {candidateIndex} 超出当前章节候选范围（共 {candidates.Count} 个）。";
                    return selected;
                }

                selected.Add(candidates[candidateIndex - 1]);
            }

            foreach (var requestedTitle in requestedTitles)
            {
                var ordinal = ExtractCandidateOrdinal(requestedTitle);
                if (ordinal > 0)
                {
                    if (ordinal > candidates.Count)
                    {
                        error = $"候选序号 {ordinal} 超出当前章节候选范围（共 {candidates.Count} 个）。";
                        return new List<PlotCandidate>();
                    }

                    selected.Add(candidates[ordinal - 1]);
                    continue;
                }

                var byTitle = candidates.FirstOrDefault(c =>
                    string.Equals(requestedTitle, c.Title, StringComparison.OrdinalIgnoreCase));
                if (byTitle != null)
                    selected.Add(byTitle);
            }

            if (selected.Count == 0 && requestedTitles.Count == 0)
            {
                var recommended = candidates.FirstOrDefault(c =>
                    string.Equals(c.Title, brief.RecommendedCandidateTitle, StringComparison.OrdinalIgnoreCase))
                    ?? candidates.OrderByDescending(c => c.TotalScore).FirstOrDefault();
                if (recommended != null)
                    selected.Add(recommended);
            }

            return selected
                .GroupBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private static int ExtractCandidateOrdinal(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            var text = value.Trim();
            if (int.TryParse(text, out var exactNumber))
                return exactNumber;

            var bracketed = ExtractLeadingBracketedNumber(text);
            if (bracketed > 0)
                return bracketed;

            var ordinal = ExtractNumberAfterPrefix(text, "第");
            if (ordinal > 0)
                return ordinal;

            ordinal = ExtractNumberAfterPrefix(text, "候选");
            if (ordinal > 0)
                return ordinal;

            ordinal = ExtractNumberAfterPrefix(text, "选第");
            if (ordinal > 0)
                return ordinal;

            ordinal = ExtractNumberAfterPrefix(text, "选择第");
            if (ordinal > 0)
                return ordinal;

            return ExtractLeadingDelimitedNumber(text);
        }

        private static int ExtractLeadingBracketedNumber(string text)
        {
            if (text.Length < 3)
                return 0;

            var closing = text[0] switch
            {
                '【' => '】',
                '[' => ']',
                '(' => ')',
                '（' => '）',
                _ => '\0'
            };
            if (closing == '\0')
                return 0;

            var closeIndex = text.IndexOf(closing);
            if (closeIndex <= 1)
                return 0;

            return int.TryParse(text.Substring(1, closeIndex - 1).Trim(), out var number)
                ? number
                : 0;
        }

        private static int ExtractNumberAfterPrefix(string text, string prefix)
        {
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return 0;

            var start = prefix.Length;
            while (start < text.Length && char.IsWhiteSpace(text[start]))
                start++;

            var end = start;
            while (end < text.Length && char.IsDigit(text[end]))
                end++;

            return end > start && int.TryParse(text.Substring(start, end - start), out var number)
                ? number
                : 0;
        }

        private static int ExtractLeadingDelimitedNumber(string text)
        {
            var end = 0;
            while (end < text.Length && char.IsDigit(text[end]))
                end++;

            if (end == 0)
                return 0;

            if (end == text.Length || IsOrdinalDelimiter(text[end]))
                return int.TryParse(text.Substring(0, end), out var number) ? number : 0;

            return 0;
        }

        private static bool IsOrdinalDelimiter(char value) =>
            char.IsWhiteSpace(value) ||
            value is '.' or '、' or ':' or '：' or '-' or '—' or ')' or '）' or ']' or '】' or '个' or '项' or '号';

        private static StoryCreativeConstitution ApplySelectedMacroCandidate(
            StoryCreativeConstitution source,
            MacroStoryConceptCandidate? candidate)
        {
            var result = new StoryCreativeConstitution
            {
                Genre = source.Genre,
                SubGenre = source.SubGenre,
                ReaderPromise = source.ReaderPromise,
                CoreHook = source.CoreHook,
                CoreTheme = source.CoreTheme,
                MainPleasure = source.MainPleasure,
                SecondaryPleasure = source.SecondaryPleasure,
                WorldCoreRule = source.WorldCoreRule,
                MainConflictEngine = source.MainConflictEngine,
                ProtagonistEngine = source.ProtagonistEngine,
                NoveltyPoint = source.NoveltyPoint,
                DepthLayer = source.DepthLayer,
                ForbiddenDirections = source.ForbiddenDirections.ToList(),
                CommercialRhythm = source.CommercialRhythm,
                GenreProfile = new GenreDirectionProfile
                {
                    PleasureStrength = source.GenreProfile.PleasureStrength,
                    MysteryStrength = source.GenreProfile.MysteryStrength,
                    EmotionStrength = source.GenreProfile.EmotionStrength,
                    WorldbuildingStrength = source.GenreProfile.WorldbuildingStrength,
                    EnsembleStrength = source.GenreProfile.EnsembleStrength,
                    DepthStrength = source.GenreProfile.DepthStrength,
                    PaceStrength = source.GenreProfile.PaceStrength,
                    Strategy = source.GenreProfile.Strategy,
                    RiskWarnings = source.GenreProfile.RiskWarnings.ToList()
                }
            };

            if (candidate == null)
                return result;

            result.CoreHook = FirstNonEmpty(candidate.CoreHook, result.CoreHook);
            result.WorldCoreRule = FirstNonEmpty(candidate.WorldCoreRule, result.WorldCoreRule);
            result.MainConflictEngine = FirstNonEmpty(candidate.MainConflictEngine, result.MainConflictEngine);
            result.ProtagonistEngine = FirstNonEmpty(candidate.ProtagonistEngine, result.ProtagonistEngine);
            result.DepthLayer = FirstNonEmpty(candidate.DepthLayer, result.DepthLayer);
            result.NoveltyPoint = FirstNonEmpty(candidate.Title, result.NoveltyPoint);
            result.ReaderPromise = FirstNonEmpty(
                candidate.PleasureLoop,
                result.ReaderPromise);
            result.WorldCoreRule = JoinNonEmpty("；", result.WorldCoreRule, candidate.WorldbuildingBlueprint, candidate.ProgressionSystem);
            result.MainConflictEngine = JoinNonEmpty("；", result.MainConflictEngine, candidate.PleasureLoop);
            result.ProtagonistEngine = JoinNonEmpty("；", result.ProtagonistEngine, candidate.ProtagonistProfile);
            result.CommercialRhythm = JoinNonEmpty(
                "；",
                result.CommercialRhythm,
                candidate.FirstThreeVolumes.Count > 0 ? $"前三卷方向：{string.Join(" / ", candidate.FirstThreeVolumes)}" : string.Empty,
                candidate.KeyCharacters.Count > 0 ? $"首批角色：{string.Join(" / ", candidate.KeyCharacters)}" : string.Empty);
            result.GenreProfile.RiskWarnings.AddRange(candidate.Risks);
            result.GenreProfile.RiskWarnings = result.GenreProfile.RiskWarnings
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();
            return result;
        }

        private static string JoinNonEmpty(string separator, params string[] values) =>
            string.Join(separator, values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));

        private static MacroStoryConceptCandidate? ResolveSelectedMacroCandidate(
            IReadOnlyList<MacroStoryConceptCandidate> candidates,
            string selectedCandidateId,
            int selectedCandidateIndex,
            out string error)
        {
            error = string.Empty;
            if (candidates.Count == 0)
                return null;

            if (!string.IsNullOrWhiteSpace(selectedCandidateId))
            {
                var byId = candidates.FirstOrDefault(c =>
                    string.Equals(c.CandidateId, selectedCandidateId.Trim(), StringComparison.OrdinalIgnoreCase));
                if (byId != null) return byId;
                error = $"选择的故事地基候选 ID「{selectedCandidateId}」不在当前 run 中，我不会固化。请重新选择候选。";
                return null;
            }

            if (selectedCandidateIndex == 0)
                return candidates[0];

            if (selectedCandidateIndex > 0)
            {
                if (selectedCandidateIndex <= candidates.Count)
                    return candidates[selectedCandidateIndex - 1];

                error = $"选择的故事地基候选序号 {selectedCandidateIndex} 超出当前 run 的候选范围，请重新选择候选。";
                return null;
            }

            error = "提交故事地基时必须提供候选 ID 或 1-based 候选序号。";
            return null;
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
        }

        private static VolumeArcPlan? FindCurrentVolumeArc(
            IReadOnlyList<VolumeArcPlan>? volumeArcs,
            string chapterId)
        {
            if (volumeArcs == null || volumeArcs.Count == 0 || string.IsNullOrWhiteSpace(chapterId))
                return null;

            var normalizedChapterId = chapterId.Trim();
            var canonicalArcs = volumeArcs
                .Where(v => v.Status == VolumeArcStatus.Canon)
                .ToList();

            return canonicalArcs.FirstOrDefault(v =>
                    !string.IsNullOrWhiteSpace(v.VolumeId)
                    && normalizedChapterId.StartsWith(v.VolumeId, StringComparison.OrdinalIgnoreCase))
                ?? canonicalArcs.FirstOrDefault(v =>
                    IsChapterInsideArc(normalizedChapterId, v))
                ?? canonicalArcs.LastOrDefault();
        }

        private static VolumeChapterBeat? FindVolumeBeat(
            VolumeArcPlan? volumeArc,
            string chapterId)
        {
            if (volumeArc?.ChapterBeats == null || volumeArc.ChapterBeats.Count == 0)
                return null;

            var chapterIndex = ExtractTrailingNumber(chapterId);
            var startIndex = ExtractTrailingNumber(volumeArc.StartChapterId);
            var relativeIndex = chapterIndex > 0
                ? Math.Max(1, chapterIndex - Math.Max(1, startIndex) + 1)
                : 1;

            var clampedIndex = Math.Min(relativeIndex, volumeArc.ChapterBeats.Count);
            return volumeArc.ChapterBeats.FirstOrDefault(b => b.Index == clampedIndex)
                   ?? volumeArc.ChapterBeats[Math.Max(0, clampedIndex - 1)];
        }

        private static bool IsChapterInsideArc(string chapterId, VolumeArcPlan volumeArc)
        {
            if (volumeArc == null) return false;

            var chapterIndex = ExtractTrailingNumber(chapterId);
            var startIndex = ExtractTrailingNumber(volumeArc.StartChapterId);
            var endIndex = ExtractTrailingNumber(volumeArc.EndChapterId);
            if (chapterIndex <= 0 || startIndex <= 0 || endIndex <= 0)
                return false;

            var prefix = ExtractPrefixBeforeTrailingNumber(chapterId);
            var startPrefix = ExtractPrefixBeforeTrailingNumber(volumeArc.StartChapterId);
            var endPrefix = ExtractPrefixBeforeTrailingNumber(volumeArc.EndChapterId);
            if (!string.IsNullOrWhiteSpace(startPrefix)
                && !string.Equals(prefix, startPrefix, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(prefix, endPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return chapterIndex >= startIndex && chapterIndex <= endIndex;
        }

        private static int ExtractTrailingNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return -1;

            var end = value.Length - 1;
            while (end >= 0 && !char.IsDigit(value[end])) end--;
            if (end < 0) return -1;

            var start = end;
            while (start >= 0 && char.IsDigit(value[start])) start--;
            return int.TryParse(value.Substring(start + 1, end - start), out var number)
                ? number
                : -1;
        }

        private static string ExtractPrefixBeforeTrailingNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            var end = value.Length - 1;
            while (end >= 0 && !char.IsDigit(value[end])) end--;
            if (end < 0) return value.Trim();

            var start = end;
            while (start >= 0 && char.IsDigit(value[start])) start--;
            return value.Substring(0, start + 1).Trim();
        }

        private static List<string> SplitCandidateTitles(string candidateTitles)
        {
            if (string.IsNullOrWhiteSpace(candidateTitles))
                return new List<string>();

            return candidateTitles
                .Split(new[] { ',', '，', ';', '；', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
