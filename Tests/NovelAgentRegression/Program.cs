using System.Text.Json;
using TM.Framework.Common.Helpers.Storage;
using TM.Services.Framework.AI.Embedding;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Implementations.Indexing;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.TaskContexts;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Tests.NovelAgentRegression;

internal static class Program
{
    private static readonly List<(string Name, Func<Task> Test)> Tests = new()
    {
        ("Genre direction planner protects type promise", GenreDirectionPlannerProtectsTypePromise),
        ("Book concept designer generates durable macro candidates", BookConceptDesignerGeneratesDurableMacroCandidates),
        ("Story foundation commit uses structured candidate identity", StoryFoundationCommitUsesStructuredCandidateIdentityAsync),
        ("Creative knowledge base seeds and project memory are retrievable", CreativeKnowledgeBaseRetrievesSeedsAndProjectMemoryAsync),
        ("NovelAgentOrchestrator runs foundation and chapter planning loop", NovelAgentOrchestratorRunsFoundationAndChapterPlanningLoopAsync),
        ("NovelAgentOrchestrator enforces split chapter writing chain", NovelAgentOrchestratorEnforcesSplitChapterWritingChainAsync),
        ("StoryBible persists character ledger and canonical constitution", StoryBiblePersistsCoreAgentStateAsync),
        ("CharacterLedger imports low-risk review entries", CharacterLedgerImportsLowRiskReviewEntriesAsync),
        ("CharacterLedger stops high-risk status until confirmation", CharacterLedgerRequiresConfirmationForHighRiskAsync),
        ("StoryBible commits volume arc and creates foreshadow ledger", CommitVolumeArcCreatesForeshadowLedgerAsync),
        ("Canon ledger high-risk status requires confirmation", CanonLedgerHighRiskRequiresConfirmationAsync),
        ("StoryStateSnapshot uses vector chapter and chunk recall", StoryStateSnapshotUsesVectorRecallAsync),
        ("Commercial rhythm checker feeds planning and review", CommercialRhythmFeedsPlanningAndReview),
        ("ChapterNoveltyPlanner uses emotion and relationship knowledge", ChapterPlannerUsesEmotionRelationshipKnowledge),
        ("Sample novel chapter fixture feeds RAG repetition planning", SampleNovelChapterFixtureFeedsRagPlanningAsync),
        ("Sample novel regression fixture is loadable", SampleNovelRegressionFixtureIsLoadableAsync),
        ("Quality evaluation fixtures cover five reviewer dimensions", QualityEvaluationFixturesCoverFiveReviewerDimensionsAsync)
    };

    public static async Task<int> Main()
    {
        var failed = 0;
        Console.WriteLine("NovelAgent regression suite");
        Console.WriteLine("===========================");

        foreach (var (name, test) in Tests)
        {
            try
            {
                await test();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine($"FAIL {name}");
                Console.WriteLine($"     {ex.Message}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? $"All {Tests.Count} regression checks passed."
            : $"{failed} of {Tests.Count} regression checks failed.");

        return failed == 0 ? 0 : 1;
    }

    private static Task GenreDirectionPlannerProtectsTypePromise()
    {
        var planner = new GenreDirectionPlanner();
        var profile = planner.BuildProfile("玄幻", "学院流", "爽文推进 + 烧脑规则悬疑 + 情绪代价");

        Check.True(profile.PleasureStrength >= 9, "Satisfying xuanhuan/s爽 direction should strongly protect pleasure payoff.");
        Check.True(profile.MysteryStrength >= 9, "Suspense direction should strongly protect mystery progression.");
        Check.True(profile.EmotionStrength >= 8, "Emotion-cost direction should protect emotional consequence.");
        Check.True(profile.WorldbuildingStrength >= 8, "Rule-suspense xuanhuan should protect worldbuilding pressure.");
        Check.Contains("代价", profile.Strategy + string.Join("；", profile.RiskWarnings),
            "Genre strategy should warn against cost-free payoff.");
        Check.True(profile.RiskWarnings.Any(w => w.Contains("反派降智", StringComparison.OrdinalIgnoreCase)
                                                || w.Contains("临时新设定", StringComparison.OrdinalIgnoreCase)),
            "Genre profile should name common stale-plot risks.");

        return Task.CompletedTask;
    }

    private static Task BookConceptDesignerGeneratesDurableMacroCandidates()
    {
        var designer = new BookConceptDesigner(new GenreDirectionPlanner());
        var request = new StoryFoundationRequest
        {
            UserSeed = "一个能听见世界规则裂纹的少年，被迫用成长偿还规则债务。",
            Genre = "玄幻",
            SubGenre = "学院流",
            TargetReader = "喜欢升级、设定解谜、强情绪代价的长篇读者",
            DesiredDirection = "爽文推进 + 规则悬疑 + 师徒关系拉扯"
        };

        var constitution = designer.BuildConstitution(request);
        Check.Contains("规则裂纹", constitution.CoreHook, "Book constitution should preserve the seed's strongest hook.");
        Check.Contains("代价", constitution.ProtagonistEngine + constitution.NoveltyPoint + string.Join("；", constitution.ForbiddenDirections),
            "Book constitution should force cost/consequence into the long-form engine.");
        Check.True(constitution.ForbiddenDirections.Any(d => d.Contains("临时新设定", StringComparison.OrdinalIgnoreCase)),
            "Book constitution should forbid solving crises with temporary new settings.");
        Check.True(constitution.GenreProfile?.PleasureStrength >= 8, "Book constitution should embed the genre direction profile.");

        var candidates = designer.GenerateMacroCandidates(request).ToList();
        Check.True(candidates.Count >= 3, "Book concept designer should produce multiple macro story candidates.");
        var candidateScores = candidates
            .Select(c => c.NoveltyScore + c.SustainabilityScore + c.TypeMatchScore)
            .ToList();
        Check.True(candidateScores.SequenceEqual(candidateScores.OrderByDescending(s => s)),
            "Macro candidates should be sorted by novelty/sustainability/type match.");
        Check.True(candidates.Take(2).Any(c => c.Title == "真相递进型"),
            "Top macro candidates should keep the truth-progression route available for rule-suspense stories.");
        Check.True(candidates.Any(c => c.Risks.Any(r => r.Contains("线索", StringComparison.OrdinalIgnoreCase)
                                                        || r.Contains("代价账本", StringComparison.OrdinalIgnoreCase))),
            "Macro candidates should carry execution risks, not only ideas.");

        return Task.CompletedTask;
    }

    private static async Task StoryFoundationCommitUsesStructuredCandidateIdentityAsync()
    {
        ResetStorage("StructuredCandidateIdentityIndex");
        var orchestrator = CreateOrchestrator(CreateChapterContext("chapter-identity-001"));
        var run = await orchestrator.PlanStoryFoundationAsync(new StoryFoundationRequest
        {
            UserSeed = "一个能听见世界规则裂纹的少年，被迫用成长偿还规则债务。",
            Genre = "玄幻",
            SubGenre = "学院流",
            TargetReader = "喜欢升级、规则悬疑和强代价的长篇读者",
            DesiredDirection = "爽文推进 + 规则悬疑 + 关系代价"
        });
        var first = run.MacroCandidates[0];
        var byIndex = await orchestrator.CommitStoryFoundationAsync(
            run.RunId,
            overwrite: false,
            confirmed: true,
            selectedMacroCandidateTitle: "规则反哺型",
            selectedMacroCandidateId: string.Empty,
            selectedMacroCandidateIndex: 1);
        Check.True(byIndex.Success,
            "A valid candidateIndex should commit even when the title was misspelled by the LLM.");
        Check.Equal(first.Title, byIndex.Document?.Constitution?.NoveltyPoint,
            "Index-based commit should apply the canonical selected candidate.");

        ResetStorage("StructuredCandidateIdentityId");
        orchestrator = CreateOrchestrator(CreateChapterContext("chapter-identity-002"));
        run = await orchestrator.PlanStoryFoundationAsync(new StoryFoundationRequest
        {
            UserSeed = "一个能听见世界规则裂纹的少年，被迫用成长偿还规则债务。",
            Genre = "玄幻",
            SubGenre = "学院流",
            TargetReader = "喜欢升级、规则悬疑和强代价的长篇读者",
            DesiredDirection = "爽文推进 + 规则悬疑 + 关系代价"
        });
        var second = run.MacroCandidates[1];
        var byId = await orchestrator.CommitStoryFoundationAsync(
            run.RunId,
            overwrite: false,
            confirmed: true,
            selectedMacroCandidateTitle: "错别字标题",
            selectedMacroCandidateId: second.CandidateId,
            selectedMacroCandidateIndex: 0);
        Check.True(byId.Success,
            "A valid candidateId should commit even when the title is wrong.");
        Check.Equal(second.Title, byId.Document?.Constitution?.NoveltyPoint,
            "Id-based commit should apply the canonical selected candidate.");

        ResetStorage("StructuredCandidateIdentityTitleOnlyFailure");
        orchestrator = CreateOrchestrator(CreateChapterContext("chapter-identity-003"));
        run = await orchestrator.PlanStoryFoundationAsync(new StoryFoundationRequest
        {
            UserSeed = "一个能听见世界规则裂纹的少年，被迫用成长偿还规则债务。",
            Genre = "玄幻",
            SubGenre = "学院流",
            TargetReader = "喜欢升级、规则悬疑和强代价的长篇读者",
            DesiredDirection = "爽文推进 + 规则悬疑 + 关系代价"
        });
        var titleOnlyMiss = await orchestrator.CommitStoryFoundationAsync(
            run.RunId,
            overwrite: false,
            confirmed: true,
            selectedMacroCandidateTitle: "规则反哺型");
        Check.True(!titleOnlyMiss.Success,
            "A misspelled title without candidateId/index should not silently commit the default constitution.");
        Check.Contains("不在当前 run", titleOnlyMiss.Message,
            "Title-only mismatch should explain that the candidate identity is invalid.");
    }

    private static async Task CreativeKnowledgeBaseRetrievesSeedsAndProjectMemoryAsync()
    {
        ResetStorage("CreativeKnowledge");
        var service = new CreativeKnowledgeBaseService();
        var document = await service.LoadAsync();

        Check.True(document.Entries.Count >= 10, "Creative knowledge base should seed built-in genre, trope and relationship knowledge.");
        Check.True(document.Entries.Any(e => e.Category == CreativeKnowledgeCategory.RelationshipDynamic),
            "Creative knowledge base should include relationship-dynamic knowledge.");

        var constitution = new StoryCreativeConstitution
        {
            Genre = "玄幻",
            SubGenre = "爽文",
            ReaderPromise = "规则债务下的压迫、反击、代价和升级。",
            CoreHook = "主角每次利用规则获胜都会欠下未来债务。",
            GenreProfile = new GenreDirectionProfile
            {
                PleasureStrength = 9,
                EmotionStrength = 8,
                DepthStrength = 7
            }
        };

        await service.RecordUsedPatternAsync("chapter-006", "关键人物刚好路过救场", "此前已使用过导师及时救场。");
        var result = await service.RetrieveAsync(
            "玄幻 爽文 规则反噬 关系变化 关键人物刚好路过救场 反套路 代价 信息差",
            constitution,
            new[] { "关键人物刚好路过救场" },
            topK: 12);

        Check.True(result.Success, "Creative knowledge retrieval should succeed.");
        Check.True(result.GenrePrinciples.Any(p => p.Contains("代价", StringComparison.OrdinalIgnoreCase)),
            "Genre principles should retrieve cost-return guidance for xuanhuan/s爽.");
        Check.True(result.TropeWarnings.Any(p => p.Contains("救场", StringComparison.OrdinalIgnoreCase)),
            "Trope warnings should retrieve the already-risky rescue pattern.");
        Check.True(result.AntiTropeStrategies.Any(p => p.Contains("代价", StringComparison.OrdinalIgnoreCase)
                                                       || p.Contains("信息来源", StringComparison.OrdinalIgnoreCase)),
            "Anti-trope strategies should retrieve actionable variation strategies.");
        Check.True(result.ProjectMemory.Any(p => p.Contains("chapter-006", StringComparison.OrdinalIgnoreCase)),
            "Project memory should retrieve recorded used plot patterns.");
        Check.True(result.Hits.Any(h => h.Reason.Contains("命中项目已用桥段风险", StringComparison.OrdinalIgnoreCase)),
            "Used plot patterns should increase retrieval score with an explicit reason.");
    }

    private static async Task NovelAgentOrchestratorRunsFoundationAndChapterPlanningLoopAsync()
    {
        ResetStorage("OrchestratorPlanningLoop");
        var orchestrator = CreateOrchestrator(new ContentTaskContext
        {
            ChapterId = "chapter-001",
            Title = "第一章 裂纹初响",
            Summary = "主角第一次听见规则裂纹。",
            ChapterPlan = new ChapterPlanStub
            {
                ChapterTitle = "裂纹初响",
                MainGoal = "让林昼发现规则债务的第一条线索",
                KeyTurn = "他用一次小胜利换来未来债务",
                ReaderExperienceGoal = "压迫后的反击、代价和悬疑钩子"
            },
            FactSnapshot = new FactSnapshot
            {
                ConflictProgress =
                {
                    new ConflictProgressSnapshot
                    {
                        Name = "学院规则债务",
                        Status = "刚刚显形",
                        RecentProgress = { "入学试炼被规则裂纹篡改" }
                    }
                },
                ForeshadowingStatus =
                {
                    new ForeshadowingStatusSnapshot
                    {
                        Name = "裂纹会记账",
                        IsSetup = true,
                        PayoffChapterId = "chapter-006"
                    }
                },
                CharacterStates =
                {
                    new CharacterStateSnapshot
                    {
                        Name = "林昼",
                        Stage = "被迫入局",
                        Abilities = "能听见规则裂纹",
                        Relationships = "尚未信任导师"
                    }
                }
            }
        });

        var foundationRun = await orchestrator.PlanStoryFoundationAsync(new StoryFoundationRequest
        {
            UserSeed = "一个能听见世界规则裂纹的少年，被迫用成长偿还规则债务。",
            Genre = "玄幻",
            SubGenre = "学院流",
            TargetReader = "喜欢升级、规则悬疑和强代价的长篇读者",
            DesiredDirection = "爽文推进 + 规则悬疑 + 关系代价"
        });

        Check.Equal(NovelAgentIntent.CreateStoryFoundation, foundationRun.Intent,
            "Foundation planning should create a story-foundation Agent Run.");
        Check.Equal(NovelAgentRunStatus.AwaitingConfirmation, foundationRun.Status,
            "Foundation planning should stop at user confirmation.");
        Check.True(foundationRun.MacroCandidates.Count >= 3,
            "Foundation planning should produce macro story candidates.");
        Check.True(foundationRun.Steps.Any(s => s.ToolName == "StoryBible.CommitConstitution"
                                                && s.RequiresConfirmation),
            "Foundation Run should include a high-risk Story Bible confirmation step.");

        var selectedMacro = foundationRun.MacroCandidates.First(c => c.Title == "真相递进型");
        var commit = await orchestrator.CommitStoryFoundationAsync(
            foundationRun.RunId,
            overwrite: false,
            confirmed: true,
            selectedMacroCandidateTitle: selectedMacro.Title);
        Check.True(commit.Success, "Selected macro candidate should be committed to Story Bible.");
        Check.Equal(selectedMacro.Title, commit.Document?.Constitution?.NoveltyPoint,
            "Committed Story Bible should reflect the selected macro candidate.");
        Check.True(commit.Document?.AgentRuns.Any(r => r.RunId == foundationRun.RunId
                                                       && r.Status == NovelAgentRunStatus.Completed) == true,
            "Foundation Run should be marked completed after Story Bible commit.");

        var chapterRun = await orchestrator.PlanChapterAsync(new ChapterCreativeRequest
        {
            ChapterId = "chapter-001",
            UserGoal = "写出第一章：主角初次发现规则裂纹，但必须付出一个可追踪代价。"
        });

        Check.Equal(NovelAgentIntent.PlanChapter, chapterRun.Intent,
            "Chapter planning should create a chapter-planning Agent Run.");
        Check.Equal(NovelAgentRunStatus.AwaitingConfirmation, chapterRun.Status,
            "Chapter planning should stop before candidate confirmation.");
        Check.True(chapterRun.StoryState?.ActiveConflicts.Any(c => c.Contains("学院规则债务", StringComparison.OrdinalIgnoreCase)) == true,
            "Chapter planning should carry story-state conflict pressure into the run.");
        Check.True(chapterRun.ChapterBrief?.Candidates.Count >= 6,
            "Chapter planning should produce the full candidate set.");
        Check.True(chapterRun.ChapterBrief?.KnowledgeNotes.Any(n => n.Contains("创意知识", StringComparison.OrdinalIgnoreCase)
                                                                    || n.Contains("商业节奏", StringComparison.OrdinalIgnoreCase)) == true,
            "Chapter planning should include creative knowledge or commercial rhythm notes.");

        var recommended = chapterRun.ChapterBrief!.RecommendedCandidateTitle;
        var unconfirmed = await orchestrator.SelectChapterCandidateAsync(chapterRun.RunId, recommended);
        Check.True(!unconfirmed.Success && unconfirmed.RequiresConfirmation,
            "Candidate selection should require explicit confirmation.");

        var selection = await orchestrator.SelectChapterCandidateAsync(
            chapterRun.RunId,
            candidateTitles: recommended,
            selectionMode: "Single",
            selectionRationale: "Regression user confirmed recommended candidate.",
            confirmed: true);
        Check.True(selection.Success, "Confirmed candidate selection should update the Agent Run.");
        Check.Equal(recommended, selection.Run?.ChapterBrief?.SelectedCandidateTitle,
            "Confirmed selection should persist the selected candidate title.");
        Check.Contains("Confirmed", selection.Run?.ChapterBrief?.SelectionMode ?? string.Empty,
            "Confirmed selection should mark the selection mode as confirmed.");
        Check.True(selection.Run?.Steps.Any(s => s.ToolName == "NovelAgent.SelectChapterCandidate"
                                                 && s.Status == NovelAgentStepStatus.Completed) == true,
            "Candidate selection step should be marked completed.");
    }

    private static async Task NovelAgentOrchestratorEnforcesSplitChapterWritingChainAsync()
    {
        ResetStorage("OrchestratorExecutionLoop");
        var orchestrator = CreateOrchestrator(CreateChapterContext("chapter-002"));

        var foundationRun = await orchestrator.PlanStoryFoundationAsync(new StoryFoundationRequest
        {
            UserSeed = "一个能听见世界规则裂纹的少年，被迫用成长偿还规则债务。",
            Genre = "玄幻",
            SubGenre = "学院流",
            TargetReader = "喜欢升级、规则悬疑和强代价的长篇读者",
            DesiredDirection = "爽文推进 + 规则悬疑 + 关系代价"
        });
        var foundationCommit = await orchestrator.CommitStoryFoundationAsync(
            foundationRun.RunId,
            overwrite: false,
            confirmed: true,
            selectedMacroCandidateTitle: foundationRun.MacroCandidates.First().Title);
        Check.True(foundationCommit.Success, "Execution loop needs a committed Story Bible first.");

        var chapterRun = await orchestrator.PlanChapterAsync(new ChapterCreativeRequest
        {
            ChapterId = "chapter-002",
            UserGoal = "写第二章：让主角第一次主动利用规则裂纹，但留下关系代价。"
        });
        var selection = await orchestrator.SelectChapterCandidateAsync(
            chapterRun.RunId,
            chapterRun.ChapterBrief!.RecommendedCandidateTitle,
            "Recommended",
            "Regression confirmed recommended candidate before execution.",
            confirmed: true);
        Check.True(selection.Success, "Execution loop requires a confirmed chapter candidate.");

        var contextPackage = await orchestrator.BuildChapterContextPackageAsync(chapterRun.RunId);
        Check.True(contextPackage.Success, "Split writing chain should build the chapter context package first.");
        Check.True(contextPackage.ContextPackage?.Status.StartsWith("context_ready", StringComparison.OrdinalIgnoreCase) == true,
            "Context package should be marked ready before draft generation.");

        var unconfirmedDraft = await orchestrator.GenerateChapterWithChangesAsync(chapterRun.RunId, confirmed: false);
        Check.True(!unconfirmedDraft.Success && unconfirmedDraft.RequiresConfirmation && unconfirmedDraft.RiskLevel == NovelToolRiskLevel.High,
            "Draft generation should require high-risk confirmation.");

        var draft = await orchestrator.GenerateChapterWithChangesAsync(chapterRun.RunId, confirmed: true);
        Check.True(draft.Success, "Confirmed split draft generation should execute the GenerateChapterWithChanges step.");
        Check.True(draft.DraftArtifact != null, "Draft artifact should be attached to the run even when LLM settings block writing.");
        Check.True(draft.Run?.Steps.Any(s => s.ToolName == "NovelAgent.GenerateChapterWithChanges"
                                             && s.Status == NovelAgentStepStatus.Completed) == true,
            "Split writing chain should mark GenerateChapterWithChanges completed.");
        Check.True(draft.Run?.Steps.Any(s => s.ToolName == "Writer.GenerateChapter") != true,
            "Split writing chain must not use the removed Writer.GenerateChapter step.");

        var gate = await orchestrator.ValidateChapterDraftAsync(chapterRun.RunId);
        Check.True(!gate.Success, "Missing CHANGES or blocked draft should fail the hard GenerationGate.");
        Check.Equal("gate_failed", gate.GateReport?.Status ?? string.Empty,
            "GenerationGate should fail blocked or invalid drafts.");
        Check.True(gate.Run?.Steps.Any(s => s.ToolName == "NovelAgent.ValidateChapterDraft"
                                            && s.Status == NovelAgentStepStatus.Failed) == true,
            "ValidateChapterDraft should record gate failure.");

        var commitBlocked = await orchestrator.CommitValidatedChapterAsync(chapterRun.RunId, confirmed: true);
        Check.True(!commitBlocked.Success,
            "CommitValidatedChapter must refuse chapters that did not pass GenerationGate.");
    }

    private static async Task StoryBiblePersistsCoreAgentStateAsync()
    {
        ResetStorage("StoryBibleCoreState");
        var service = new StoryBibleService();

        var constitution = new StoryCreativeConstitution
        {
            Genre = "玄幻",
            SubGenre = "学院流",
            ReaderPromise = "每一卷都有明确成长、代价和反转。",
            CoreHook = "主角能听见世界规则的裂纹。",
            WorldCoreRule = "每次改写规则都会留下债务。",
            MainConflictEngine = "规则债务与学院权力结构互相挤压。",
            ProtagonistEngine = "用洞察换取成长，但必须付出关系和身体代价。",
            GenreProfile = new GenreDirectionProfile
            {
                PleasureStrength = 8,
                MysteryStrength = 7,
                EmotionStrength = 8,
                WorldbuildingStrength = 9,
                DepthStrength = 7
            }
        };

        var commit = await service.CommitConstitutionAsync(
            constitution,
            new[]
            {
                new MacroStoryConceptCandidate
                {
                    Title = "裂纹听者",
                    CoreHook = constitution.CoreHook,
                    WorldCoreRule = constitution.WorldCoreRule,
                    NoveltyScore = 8,
                    SustainabilityScore = 9,
                    TypeMatchScore = 8
                }
            },
            sourceRunId: "run-foundation",
            confirmed: true);

        Check.True(commit.Success, "Story Bible should commit after explicit confirmation.");
        Check.Equal("玄幻", commit.Document?.Constitution?.Genre, "Constitution genre should persist.");
        Check.True(commit.Document?.CanonLedger.Any(e => e.Title == "故事创意宪法" && e.Status == CanonLedgerEntryStatus.Canon) == true,
            "Committing Story Bible should create a canonical constitution ledger entry.");

        var character = new CharacterLedgerEntry
        {
            CharacterName = "林昼",
            Role = "主角",
            Type = CharacterLedgerEntryType.Goal,
            Status = CharacterLedgerStatus.Active,
            Summary = "想查明规则裂纹的来源。",
            CurrentGoal = "进入禁书楼读取旧规则。",
            Psychology = new CharacterPsychologyState
            {
                Emotion = "克制的焦虑",
                StressLevel = 12
            },
            Importance = 99
        };
        var characterResult = await service.AddCharacterEntryAsync(character);
        Check.True(characterResult.Success, "Low-risk active character state should be added.");

        var reloaded = await service.LoadAsync();
        var persisted = reloaded.CharacterLedger.Single(e => e.CharacterName == "林昼");
        Check.Equal(10, persisted.Importance, "Character importance should be normalized to 1..10.");
        Check.Equal(10, persisted.Psychology.StressLevel, "Character stress level should be normalized to 1..10.");
        Check.Contains("sqlite-redis://", service.GetStoragePath(),
            "Story Bible runtime identity should point at the SQLite/Redis store, not a disk file.");
    }

    private static async Task CharacterLedgerImportsLowRiskReviewEntriesAsync()
    {
        ResetStorage("CharacterLowRisk");
        var storyBible = new StoryBibleService();
        var service = new CharacterLedgerService(storyBible);
        var run = new NovelAgentRun
        {
            RunId = "run-chapter-12",
            TargetChapterId = "chapter-012",
            PostGenerationReview = new NovelAgentPostGenerationReview
            {
                ChapterId = "chapter-012",
                ProposedCharacterEntries =
                {
                    new CharacterLedgerEntry
                    {
                        CharacterName = "林昼",
                        Role = "主角",
                        Type = CharacterLedgerEntryType.Psychology,
                        Status = CharacterLedgerStatus.PsychologicalShifted,
                        Summary = "从压抑恐惧转为主动承受规则债务。",
                        Psychology = new CharacterPsychologyState
                        {
                            Emotion = "冷静后的孤注一掷",
                            StressLevel = 8
                        },
                        Evidence = { "他主动割舍一次求救机会，换取禁书楼开启。" }
                    }
                }
            }
        };

        var result = await service.ImportProposedEntriesFromReviewAsync(run);
        Check.True(result.Success, "Low-risk character review import should succeed.");
        Check.True(!result.RequiresConfirmation, "Low-risk character review import should not require confirmation.");
        Check.Equal(1, result.ImportedEntries.Count, "One low-risk character entry should be imported.");

        var document = await storyBible.LoadAsync();
        var entry = document.CharacterLedger.Single();
        Check.Equal("run-chapter-12", entry.SourceRunId, "Imported character entry should inherit source run id.");
        Check.Equal("chapter-012", entry.SourceChapterId, "Imported character entry should inherit target chapter id.");
        Check.Equal(CharacterLedgerStatus.PsychologicalShifted, entry.Status, "Imported low-risk status should be preserved.");
    }

    private static async Task CharacterLedgerRequiresConfirmationForHighRiskAsync()
    {
        ResetStorage("CharacterHighRisk");
        var storyBible = new StoryBibleService();
        var service = new CharacterLedgerService(storyBible);
        var proposed = new CharacterLedgerEntry
        {
            Id = "secret-linzhao",
            CharacterName = "林昼",
            Role = "主角",
            Type = CharacterLedgerEntryType.Secret,
            Status = CharacterLedgerStatus.SecretRevealed,
            Summary = "主角真实身份被揭露。",
            Secret = new CharacterSecretState
            {
                Content = "林昼其实是上一代规则债务的继承者。",
                Status = CharacterSecretStatus.PartiallyRevealed
            },
            Evidence = { "反派念出只有继承者才会触发的旧誓词。" }
        };
        var run = new NovelAgentRun
        {
            RunId = "run-secret",
            TargetChapterId = "chapter-020",
            PostGenerationReview = new NovelAgentPostGenerationReview
            {
                ProposedCharacterEntries = { proposed }
            }
        };

        var import = await service.ImportProposedEntriesFromReviewAsync(run);
        Check.True(import.Success, "High-risk review import should still complete as proposed state.");
        Check.True(import.RequiresConfirmation, "High-risk review import should require user confirmation.");
        Check.Equal(1, import.ConflictEntries.Count, "High-risk entry should be reported as conflict/confirmation item.");

        var afterImport = await storyBible.LoadAsync();
        Check.Equal(CharacterLedgerStatus.Proposed, afterImport.CharacterLedger.Single().Status,
            "High-risk character entry should be stored as Proposed before confirmation.");

        var rejected = await service.ConfirmCharacterStatusFromRunAsync(run, confirmed: false);
        Check.True(!rejected.Success, "Confirming without explicit confirmation flag should stop.");
        Check.True(rejected.RequiresConfirmation, "Confirming high-risk entry should expose confirmation requirement.");

        var confirmed = await service.ConfirmCharacterStatusFromRunAsync(run, confirmed: true);
        Check.True(confirmed.Success, "High-risk character status should update after explicit confirmation.");
        var document = await storyBible.LoadAsync();
        var entry = document.CharacterLedger.Single();
        Check.Equal(CharacterLedgerStatus.SecretRevealed, entry.Status, "Confirmed high-risk status should persist.");
        Check.Equal(CharacterSecretStatus.Revealed, entry.Secret.Status, "Secret state machine should move to Revealed.");
    }

    private static Task ChapterPlannerUsesEmotionRelationshipKnowledge()
    {
        var planner = new ChapterNoveltyPlanner();
        var brief = planner.BuildBrief(new ChapterCreativeRequest
        {
            ChapterId = "chapter-018",
            UserGoal = "让林昼拿到禁书楼钥匙",
            ActiveConflicts = { "导师隐瞒禁书楼真实代价" },
            CharacterStates = { "林昼想相信导师，但秘密债务正在逼迫他独立选择" },
            ActiveForeshadowing = { "钥匙会记住第一次持有者的恐惧" },
            Constitution = new StoryCreativeConstitution
            {
                Genre = "玄幻",
                GenreProfile = new GenreDirectionProfile
                {
                    EmotionStrength = 9,
                    EnsembleStrength = 8,
                    MysteryStrength = 7,
                    WorldbuildingStrength = 8
                }
            },
            CreativeKnowledge = new CreativeKnowledgeRetrievalResult
            {
                Hits =
                {
                    new CreativeKnowledgeHit
                    {
                        Score = 9,
                        Entry = new CreativeKnowledgeEntry
                        {
                            Category = CreativeKnowledgeCategory.RelationshipDynamic,
                            Title = "关系破局必须留下行动后果",
                            Content = "关系变化要通过选择、代价和后续资源变化体现，不只写情绪宣言。",
                            Tags = { "关系", "代价" }
                        }
                    }
                },
                EmotionRelationshipGuides =
                {
                    "关系变化要通过选择、代价和后续资源变化体现，不只写情绪宣言。"
                },
                AntiTropeStrategies =
                {
                    "不要让导师突然降智，关系破裂必须来自双方信息差。"
                }
            }
        });

        Check.True(brief.Candidates.Count >= 6, "Chapter planner should generate the full candidate set.");
        Check.True(brief.KnowledgeNotes.Any(n => n.Contains("关系变化", StringComparison.OrdinalIgnoreCase)),
            "Emotion/relationship guide should enter chapter brief knowledge notes.");

        var relationCandidate = brief.Candidates.FirstOrDefault(c => c.Title == "关系破局");
        Check.True(relationCandidate != null, "Candidate set should include relationship breakthrough.");
        Check.True(relationCandidate!.RecommendationReason.Contains("创意知识支持", StringComparison.OrdinalIgnoreCase),
            "Relationship candidate should receive creative knowledge support.");
        Check.Contains("导师", relationCandidate.ConflictMove + relationCandidate.CharacterChoice,
            "Relationship candidate should keep the active conflict pressure visible.");

        return Task.CompletedTask;
    }

    private static async Task StoryStateSnapshotUsesVectorRecallAsync()
    {
        ResetStorage("VectorRecallSnapshot");

        var guideContext = new FakeGuideContextService(new ContentTaskContext
        {
            Title = "第十八章 禁书楼钥匙",
            ChapterId = "chapter-018",
            PreviousChapterId = "chapter-017",
            ChapterPlan = new ChapterPlanStub
            {
                ChapterTitle = "禁书楼钥匙",
                MainGoal = "让林昼拿到禁书楼钥匙",
                KeyTurn = "钥匙要求他说出第一次恐惧",
                ReaderExperienceGoal = "爽点和代价同时出现"
            },
            FactSnapshot = new FactSnapshot
            {
                ConflictProgress =
                {
                    new ConflictProgressSnapshot
                    {
                        Name = "导师隐瞒债务账本",
                        Status = "即将破裂",
                        RecentProgress = { "导师阻止林昼触碰钥匙" }
                    }
                },
                ForeshadowingStatus =
                {
                    new ForeshadowingStatusSnapshot
                    {
                        Name = "钥匙记住恐惧",
                        IsSetup = true,
                        IsResolved = false,
                        PayoffChapterId = "chapter-024"
                    }
                },
                CharacterStates =
                {
                    new CharacterStateSnapshot
                    {
                        Name = "林昼",
                        Stage = "主动承担代价前夜",
                        Abilities = "听见规则裂纹",
                        Relationships = "与导师信任摇晃"
                    }
                }
            }
        });

        var chunkSearch = new ContentChunkSearchService();
        chunkSearch.AddChunk(
            "chapter-006",
            2,
            "导师第一次看见钥匙变冷，立刻按住林昼的手，像是在阻止一笔旧债苏醒。");
        chunkSearch.AddChunk(
            "chapter-009",
            1,
            "禁书楼的旧钥匙会记住持有者的恐惧，恐惧越深，开门时讨还越重。");

        var chapterIndex = new ChapterEmbeddingIndex();
        chapterIndex.AddHit("chapter-006", 0.93f);
        chapterIndex.AddHit("chapter-009", 0.91f);
        chapterIndex.AddHit("chapter-018", 0.99f);
        chapterIndex.AddHit("chapter-017", 0.98f);

        var chunkIndex = new FakeChunkEmbeddingIndex();
        chunkIndex.AddHit("chapter-006", 2, 0.96f);
        chunkIndex.AddHit("chapter-009", 1, 0.94f);
        chunkIndex.AddHit("chapter-018", 1, 0.99f);

        var service = new StoryStateSnapshotService(
            guideContext,
            chunkSearch,
            new StoryBibleService(),
            chapterIndex,
            chunkIndex,
            new FakeEmbeddingService());

        var snapshot = await service.BuildForChapterAsync("chapter-018");

        Check.True(snapshot.RagSearchQueries.Any(q => q.Contains("禁书楼钥匙", StringComparison.OrdinalIgnoreCase)),
            "Vector recall query should be recorded in the snapshot.");
        Check.True(snapshot.LongDistanceRecall.Any(f => f.Contains("chapter-006@2", StringComparison.OrdinalIgnoreCase)),
            "Vector recall should add the matched historical chunk to long-distance recall.");
        Check.True(snapshot.SimilarContentFragments.Any(f => f.Contains("旧债苏醒", StringComparison.OrdinalIgnoreCase)),
            "Vector recall should add real chunk content to similar-content fragments.");
        Check.True(!snapshot.LongDistanceRecall.Any(f => f.Contains("chapter-018@", StringComparison.OrdinalIgnoreCase)),
            "Vector recall should exclude the current chapter.");
        Check.True(!snapshot.LongDistanceRecall.Any(f => f.Contains("chapter-017@", StringComparison.OrdinalIgnoreCase)),
            "Vector recall should exclude the previous chapter.");
    }

    private static Task CommercialRhythmFeedsPlanningAndReview()
    {
        var constitution = new StoryCreativeConstitution
        {
            CommercialRhythm = "每章必须有爽点兑现、线索推进、情绪回报和章末钩子。",
            GenreProfile = new GenreDirectionProfile
            {
                PleasureStrength = 8,
                MysteryStrength = 8,
                EmotionStrength = 8,
                PaceStrength = 8
            }
        };

        var planner = new ChapterNoveltyPlanner();
        var brief = planner.BuildBrief(new ChapterCreativeRequest
        {
            ChapterId = "chapter-021",
            UserGoal = "让林昼用钥匙破局",
            Constitution = constitution,
            ActiveConflicts = { "禁书楼债务开始追索" },
            ActiveForeshadowing = { "钥匙记住恐惧" },
            CharacterStates = { "林昼必须在胜利和记忆代价之间选择" }
        });

        Check.True(brief.KnowledgeNotes.Any(n => n.Contains("商业节奏", StringComparison.OrdinalIgnoreCase)),
            "Chapter brief should include commercial rhythm planning notes.");
        Check.True(brief.KnowledgeNotes.Any(n => n.Contains("章末", StringComparison.OrdinalIgnoreCase)),
            "Chapter brief should include page-turn hook guidance when pace is important.");

        var checker = new CommercialRhythmChecker();
        var storyState = new StoryStateSnapshot
        {
            ChapterId = "chapter-021",
            ActiveForeshadowing = { "钥匙记住恐惧" },
            ActiveConflicts = { "禁书楼债务开始追索" },
            CharacterStates = { "林昼必须在胜利和记忆代价之间选择" }
        };

        var strongChecks = checker.EvaluateGeneratedChapter(
            constitution,
            brief,
            storyState,
            "林昼终于破局，夺回钥匙。钥匙上的线索揭示了债务账本的真相，他承认恐惧并选择承担代价。然而门后传来母亲的名字。");

        Check.True(strongChecks.All(c => c.Status == NovelAgentReviewCheckStatus.Pass),
            "Strong commercial chapter should pass rhythm checks.");

        var weakChecks = checker.EvaluateGeneratedChapter(
            constitution,
            brief,
            storyState,
            "林昼走进房间，看了看四周，和导师说了几句话。天气很安静。");

        Check.True(weakChecks.Any(c => c.Key == "commercial_ending_hook" && c.Status == NovelAgentReviewCheckStatus.Warning),
            "Weak chapter should warn about missing ending hook.");
        Check.True(weakChecks.Any(c => c.Key == "commercial_continuity_drive" && c.Status == NovelAgentReviewCheckStatus.Warning),
            "Weak chapter should warn about missing continuity drive.");

        return Task.CompletedTask;
    }

    private static async Task CommitVolumeArcCreatesForeshadowLedgerAsync()
    {
        ResetStorage("VolumeArcForeshadow");
        var service = new StoryBibleService();
        var plan = new VolumeArcPlan
        {
            VolumeId = "V1",
            Title = "禁书楼债务",
            StartChapterId = "chapter-001",
            EndChapterId = "chapter-024",
            ExpectedChapterCount = 24,
            VolumePromise = "林昼第一次理解规则债务。",
            CoreQuestion = "如果破局一定欠债，还要不要改写规则。",
            MidpointReversal = "导师不是保护林昼，而是在延迟债务追索。",
            Climax = "禁书楼开启，规则债务第一次公开讨还。",
            ChapterBeats =
            {
                new VolumeChapterBeat
                {
                    Index = 1,
                    Role = "入局",
                    Goal = "通过学院初选",
                    Turn = "考题被规则裂纹篡改",
                    Cost = "失去公开求助机会"
                },
                new VolumeChapterBeat
                {
                    Index = 24,
                    Role = "高潮",
                    Goal = "打开禁书楼",
                    Turn = "钥匙要求交出恐惧",
                    Cost = "失去一段记忆"
                }
            },
            ForeshadowingPlan =
            {
                new VolumeForeshadowPlan
                {
                    Name = "钥匙记住恐惧",
                    Setup = "钥匙第一次变冷。",
                    Payoff = "钥匙要求交出恐惧作为开门代价。",
                    PayoffBeatIndex = 24
                }
            }
        };

        var blocked = await service.CommitVolumeArcAsync(plan, confirmed: false);
        Check.True(!blocked.Success, "Volume arc commit should be blocked without confirmation.");
        Check.True(blocked.RequiresConfirmation, "Volume arc commit should expose high-risk confirmation.");

        var result = await service.CommitVolumeArcAsync(plan, sourceRunId: "run-volume", confirmed: true);
        Check.True(result.Success, "Volume arc commit should succeed after confirmation.");
        Check.Equal(VolumeArcStatus.Canon, result.Document?.VolumeArcs.Single().Status,
            "Committed volume arc should become Canon.");

        var foreshadow = result.Document?.ForeshadowLedger.SingleOrDefault(e => e.Name == "钥匙记住恐惧");
        Check.True(foreshadow != null, "Committing volume foreshadow plan should create foreshadow ledger entry.");
        Check.Equal(ForeshadowLedgerStatus.Planned, foreshadow!.Status,
            "Volume-created foreshadow ledger should start as Planned.");
        Check.Equal("chapter-024", foreshadow.PlannedPayoffChapterId,
            "Foreshadow payoff chapter should resolve from volume beat index.");
    }

    private static async Task CanonLedgerHighRiskRequiresConfirmationAsync()
    {
        ResetStorage("CanonRiskGate");
        var service = new StoryBibleService();
        var entry = new CanonLedgerEntry
        {
            Id = "canon-debt-rule",
            Type = CanonLedgerEntryType.WorldRule,
            Status = CanonLedgerEntryStatus.Proposed,
            Title = "规则债务守恒",
            Content = "所有改写规则都会生成等价债务。",
            Rationale = "防止无代价开挂。",
            ImpactScope = "全书"
        };

        var add = await service.AddLedgerEntryAsync(entry);
        Check.True(add.Success, "Proposed canon entry should be added without high-risk confirmation.");

        var blocked = await service.UpdateLedgerEntryStatusAsync(entry.Id, CanonLedgerEntryStatus.Canon);
        Check.True(!blocked.Success, "Promoting canon entry should be blocked without confirmation.");
        Check.True(blocked.RequiresConfirmation, "Promoting canon entry should require confirmation.");

        var confirmed = await service.UpdateLedgerEntryStatusAsync(
            entry.Id,
            CanonLedgerEntryStatus.Canon,
            conflictCheck: "与现有 Story Bible 不冲突。",
            confirmed: true);
        Check.True(confirmed.Success, "Promoting canon entry should succeed after confirmation.");
        var canon = confirmed.Document?.CanonLedger.Single(e => e.Id == entry.Id);
        Check.Equal(CanonLedgerEntryStatus.Canon, canon!.Status, "Confirmed canon status should persist.");
        Check.Contains("不冲突", canon.ConflictCheck, "Conflict check note should persist.");
    }

    private static async Task SampleNovelRegressionFixtureIsLoadableAsync()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NovelAgent", "rules-debt-academy.story_bible.json");
        Check.True(File.Exists(fixturePath), "Sample Story Bible fixture should be copied to output.");

        await using var stream = File.OpenRead(fixturePath);
        var document = await JsonSerializer.DeserializeAsync<StoryBibleDocument>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Check.True(document != null, "Sample fixture should deserialize into StoryBibleDocument.");
        Check.True(document!.Constitution != null, "Sample fixture should include a constitution.");
        Check.True(document.CharacterLedger.Count >= 2, "Sample fixture should include character ledger entries.");
        Check.True(document.ForeshadowLedger.Count >= 1, "Sample fixture should include foreshadow ledger entries.");
        Check.True(document.VolumeArcs.Any(v => v.ChapterBeats.Count >= 3), "Sample fixture should include volume beats.");
    }

    private static async Task QualityEvaluationFixturesCoverFiveReviewerDimensionsAsync()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NovelAgent", "quality-eval-fixtures.json");
        Check.True(File.Exists(fixturePath), "Quality eval fixture should be copied to output.");

        await using var stream = File.OpenRead(fixturePath);
        var document = await JsonSerializer.DeserializeAsync<QualityEvalFixtureDocument>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Check.True(document != null, "Quality eval fixture should deserialize.");
        var dimensions = document!.Fixtures.Select(f => f.Dimension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var expected in new[] { "pacing", "motivation", "conflict", "continuity", "style" })
            Check.True(dimensions.Contains(expected), $"Quality fixtures should cover {expected} reviewer.");
        Check.True(document.Fixtures.All(f => f.ExpectedStatus == "needs_rewrite"),
            "Known bad quality fixtures should expect rewrite, not commit.");
        Check.True(document.Fixtures.All(f => !string.IsNullOrWhiteSpace(f.Text)),
            "Quality fixtures should include concrete prose evidence.");
    }

    private static async Task SampleNovelChapterFixtureFeedsRagPlanningAsync()
    {
        ResetStorage("SampleChapterRag");
        var storyBible = await LoadSampleStoryBibleAsync();
        var chapterFixture = await LoadSampleChaptersAsync();
        Check.True(chapterFixture.Chapters.Count >= 3, "Sample chapter fixture should include multiple chapter samples.");

        var chunkSearch = new ContentChunkSearchService();
        var chapterIndex = new ChapterEmbeddingIndex();
        var chunkIndex = new FakeChunkEmbeddingIndex();

        foreach (var chapter in chapterFixture.Chapters)
        {
            chapterIndex.AddHit(chapter.ChapterId, chapter.ChapterId == "chapter-001" ? 0.97f : 0.90f);
            for (var i = 0; i < chapter.Chunks.Count; i++)
            {
                var position = i + 1;
                chunkSearch.AddChunk(chapter.ChapterId, position, chapter.Chunks[i]);
                chunkIndex.AddHit(chapter.ChapterId, position, chapter.ChapterId == "chapter-001" && position == 3 ? 0.99f : 0.88f);
            }
        }

        var guideContext = new FakeGuideContextService(new ContentTaskContext
        {
            ChapterId = "chapter-013",
            Title = "第十三章 债痕回声",
            PreviousChapterId = "chapter-012",
            ChapterPlan = new ChapterPlanStub
            {
                ChapterTitle = "债痕回声",
                MainGoal = "让林昼再次利用世界规则推进目标，但这次必须换一种信息来源和代价形式。",
                KeyTurn = "规则没有立刻反噬身体，而是开始动摇师徒信任。",
                ReaderExperienceGoal = "避免重复规则反噬桥段，强化伏笔与关系代价。"
            },
            FactSnapshot = new FactSnapshot
            {
                ConflictProgress =
                {
                    new ConflictProgressSnapshot
                    {
                        Name = "规则债务追索",
                        Status = "从身体账痕转向关系信任",
                        RecentProgress = { "林昼意识到债务不只讨还身体，也会讨还关系。" }
                    }
                },
                ForeshadowingStatus =
                {
                    new ForeshadowingStatusSnapshot
                    {
                        Name = "钥匙记住恐惧",
                        IsSetup = true,
                        IsResolved = false,
                        PayoffChapterId = "chapter-024"
                    }
                },
                CharacterStates =
                {
                    new CharacterStateSnapshot
                    {
                        Name = "林昼",
                        Stage = "开始怀疑导师",
                        Abilities = "能听见规则裂纹",
                        Relationships = "师徒信任出现裂痕"
                    }
                }
            }
        });

        var snapshot = await new StoryStateSnapshotService(
            guideContext,
            chunkSearch,
            new StoryBibleService(),
            chapterIndex,
            chunkIndex,
            new FakeEmbeddingService()).BuildForChapterAsync("chapter-013");

        Check.True(snapshot.SimilarContentFragments.Any(f => f.Contains("隐藏代价", StringComparison.OrdinalIgnoreCase)),
            "Sample chapter chunks should be recalled as similar content.");
        Check.True(snapshot.LongDistanceRecall.Any(f => f.Contains("chapter-001@3", StringComparison.OrdinalIgnoreCase)),
            "Sample chapter fixture should feed vector long-distance recall.");

        var planner = new ChapterNoveltyPlanner();
        var usedPatterns = chapterFixture.Chapters.Select(c => c.UsedPlotPattern).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        var brief = planner.BuildBrief(new ChapterCreativeRequest
        {
            ChapterId = "chapter-013",
            UserGoal = "让林昼利用世界规则推进调查，但避免复用上一轮规则反噬桥段。",
            Constitution = storyBible.Constitution,
            ActiveConflicts = { snapshot.ActiveConflicts.FirstOrDefault() ?? "规则债务追索" },
            ActiveForeshadowing = { snapshot.ActiveForeshadowing.FirstOrDefault() ?? "钥匙记住恐惧" },
            CharacterStates = { snapshot.CharacterStates.FirstOrDefault() ?? "林昼与导师信任裂痕" },
            UsedPlotPatterns = usedPatterns,
            SimilarContentFragments = snapshot.SimilarContentFragments
        });

        Check.True(brief.SimilarContentWarnings.Any(w => w.Contains("隐藏代价", StringComparison.OrdinalIgnoreCase)),
            "Chapter brief should expose similar-content warnings from sample chapters.");
        var ruleBacklash = brief.Candidates.FirstOrDefault(c => c.Title == "规则反噬");
        Check.True(ruleBacklash != null, "Candidate set should include rule backlash for this genre.");
        Check.True(ruleBacklash!.Risks.Any(r => r.Contains("相似正文", StringComparison.OrdinalIgnoreCase)
                                                || r.Contains("已用桥段", StringComparison.OrdinalIgnoreCase)),
            "Rule-backlash candidate should carry repetition risk from sample chapters.");
        Check.True(ruleBacklash.RecommendationReason.Contains("相似正文风险", StringComparison.OrdinalIgnoreCase)
                   || ruleBacklash.RecommendationReason.Contains("疑似重复旧桥段", StringComparison.OrdinalIgnoreCase),
            "Rule-backlash candidate scoring should name the RAG or used-pattern penalty.");
    }

    private static void ResetStorage(string projectName)
    {
        StoragePathHelper.Reset(projectName);
    }

    private static async Task<StoryBibleDocument> LoadSampleStoryBibleAsync()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NovelAgent", "rules-debt-academy.story_bible.json");
        await using var stream = File.OpenRead(fixturePath);
        return await JsonSerializer.DeserializeAsync<StoryBibleDocument>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new StoryBibleDocument();
    }

    private static async Task<SampleChapterFixture> LoadSampleChaptersAsync()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NovelAgent", "rules-debt-academy.chapters.json");
        Check.True(File.Exists(fixturePath), "Sample chapter fixture should be copied to output.");
        await using var stream = File.OpenRead(fixturePath);
        return await JsonSerializer.DeserializeAsync<SampleChapterFixture>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new SampleChapterFixture();
    }

    private static NovelAgentOrchestrator CreateOrchestrator(ContentTaskContext context)
    {
        var storyBibleService = new StoryBibleService();
        var storyStateSnapshotService = new StoryStateSnapshotService(
            new FakeGuideContextService(context),
            new ContentChunkSearchService(),
            storyBibleService);
        var reviewer = new ChapterPostGenerationReviewer();

        return new NovelAgentOrchestrator(
            new BookConceptDesigner(new GenreDirectionPlanner()),
            new VolumeArcPlanner(),
            new ChapterNoveltyPlanner(),
            storyBibleService,
            storyStateSnapshotService,
            reviewer,
            new NovelAgentRewriteLoopService(),
            new CanonMaintenanceService(storyBibleService),
            new ForeshadowLedgerService(storyBibleService),
            new CharacterLedgerService(storyBibleService),
            new CreativeKnowledgeBaseService());
    }

    private static ContentTaskContext CreateChapterContext(string chapterId)
    {
        return new ContentTaskContext
        {
            ChapterId = chapterId,
            Title = $"{chapterId} 裂纹回声",
            Summary = "主角继续追踪规则债务的来源。",
            PreviousChapterId = "chapter-001",
            PreviousChapterSummary = "主角初次听见规则裂纹。",
            ChapterPlan = new ChapterPlanStub
            {
                ChapterTitle = "裂纹回声",
                MainGoal = "让林昼主动利用规则裂纹推进调查",
                KeyTurn = "他赢下小局，却让导师开始怀疑他的秘密",
                ReaderExperienceGoal = "胜利、代价、关系裂痕和章末钩子"
            },
            FactSnapshot = new FactSnapshot
            {
                ConflictProgress =
                {
                    new ConflictProgressSnapshot
                    {
                        Name = "学院规则债务",
                        Status = "持续升级",
                        RecentProgress = { "规则裂纹开始回应林昼" }
                    }
                },
                ForeshadowingStatus =
                {
                    new ForeshadowingStatusSnapshot
                    {
                        Name = "裂纹会记账",
                        IsSetup = true,
                        PayoffChapterId = "chapter-006"
                    }
                },
                CharacterStates =
                {
                    new CharacterStateSnapshot
                    {
                        Name = "林昼",
                        Stage = "第一次主动利用能力",
                        Abilities = "能听见规则裂纹",
                        Relationships = "导师开始怀疑他隐瞒了能力"
                    }
                }
            }
        };
    }

    private sealed class FakeGuideContextService : IGuideContextService
    {
        private readonly ContentTaskContext _context;

        public FakeGuideContextService(ContentTaskContext context)
        {
            _context = context;
        }

        public Task<ContentTaskContext?> BuildContentContextAsync(string chapterId, CancellationToken ct = default)
        {
            return Task.FromResult<ContentTaskContext?>(_context);
        }
    }

    private sealed class FakeEmbeddingService : IMicroEmbeddingService
    {
        public int Dimension => 3;

        public Task<float[]> EncodeAsync(string text, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default)
        {
            return Task.FromResult(new[] { 1f, 0f, 0f });
        }

        public Task<float[][]> EncodeBatchAsync(IReadOnlyList<string> texts, EmbeddingMode mode = EmbeddingMode.Passage, CancellationToken ct = default)
        {
            return Task.FromResult(texts.Select(_ => new[] { 1f, 0f, 0f }).ToArray());
        }

        public bool IsModelReady() => true;

        public void ReleaseSession()
        {
        }
    }

    private sealed class SampleChapterFixture
    {
        public string SampleName { get; set; } = string.Empty;
        public List<SampleChapter> Chapters { get; set; } = new();
    }

    private sealed class SampleChapter
    {
        public string ChapterId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string UsedPlotPattern { get; set; } = string.Empty;
        public List<string> Chunks { get; set; } = new();
    }

    private sealed class QualityEvalFixtureDocument
    {
        public string SampleName { get; set; } = string.Empty;
        public List<QualityEvalFixture> Fixtures { get; set; } = new();
    }

    private sealed class QualityEvalFixture
    {
        public string Id { get; set; } = string.Empty;
        public string Dimension { get; set; } = string.Empty;
        public string ExpectedStatus { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}
