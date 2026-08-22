using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TM.Services.Framework.AI.Embedding;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Design.Characters;
using TM.Services.Modules.ProjectData.Models.Design.Factions;
using TM.Services.Modules.ProjectData.Models.Design.Location;
using TM.Services.Modules.ProjectData.Models.Design.Plot;
using TM.Services.Modules.ProjectData.Models.Design.Templates;
using TM.Services.Modules.ProjectData.Models.Design.Worldview;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterBlueprint;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterPlanning;
using TM.Services.Modules.ProjectData.Models.Generate.StrategicOutline;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;
using TM.Services.Modules.ProjectData.Models.Generated;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Index;
using TM.Services.Modules.ProjectData.Models.TaskContexts;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Production;

namespace TM.Tests.NovelAgentRegression;

internal static class Program
{
    private static readonly List<(string Name, Func<Task> Test)> Tests = new()
    {
        ("Genre direction planner protects type promise", GenreDirectionPlannerProtectsTypePromise),
        ("Book concept designer generates durable macro candidates", BookConceptDesignerGeneratesDurableMacroCandidates),
        ("Book concept designer removes rejected macro templates", BookConceptDesignerRemovesRejectedMacroTemplates),
        ("Book concept designer keeps positive power fantasy direction with long exclusions", BookConceptDesignerKeepsPositivePowerFantasyDirectionWithLongExclusions),
        ("Book concept designer ignores forbidden phrases embedded in natural seed", BookConceptDesignerIgnoresForbiddenPhrasesEmbeddedInNaturalSeed),
        ("Story foundation commit uses structured candidate identity", StoryFoundationCommitUsesStructuredCandidateIdentityAsync),
        ("Creative knowledge base built-in guidance is retrievable", CreativeKnowledgeBaseRetrievesBuiltInGuidanceAsync),
        ("NovelAgentOrchestrator runs foundation and chapter planning loop", NovelAgentOrchestratorRunsFoundationAndChapterPlanningLoopAsync),
        ("NovelAgentOrchestrator enforces split chapter writing chain", NovelAgentOrchestratorEnforcesSplitChapterWritingChainAsync),
        ("StoryBible persists character ledger and canonical constitution", StoryBiblePersistsCoreAgentStateAsync),
        ("CharacterLedger imports low-risk review entries", CharacterLedgerImportsLowRiskReviewEntriesAsync),
        ("CharacterLedger stops high-risk status until confirmation", CharacterLedgerRequiresConfirmationForHighRiskAsync),
        ("StoryBible commits volume arc and creates foreshadow ledger", CommitVolumeArcCreatesForeshadowLedgerAsync),
        ("Canon ledger high-risk status requires confirmation", CanonLedgerHighRiskRequiresConfirmationAsync),
        ("StoryStateSnapshot uses vector chapter and chunk recall", StoryStateSnapshotUsesVectorRecallAsync),
        ("Commercial rhythm checker feeds planning and review", CommercialRhythmFeedsPlanningAndReview),
        ("VolumeArcPlanner follows power progression direction", VolumeArcPlannerFollowsPowerProgressionDirection),
        ("ChapterNoveltyPlanner uses emotion and relationship knowledge", ChapterPlannerUsesEmotionRelationshipKnowledge),
        ("ChapterNoveltyPlanner removes rejected plot templates", ChapterPlannerRemovesRejectedPlotTemplates),
        ("ChapterNoveltyPlanner respects direct forbidden directions", ChapterPlannerRespectsDirectForbiddenDirections),
        ("Sample novel chapter fixture feeds RAG repetition planning", SampleNovelChapterFixtureFeedsRagPlanningAsync),
        ("Sample novel regression fixture is loadable", SampleNovelRegressionFixtureIsLoadableAsync),
        ("Quality evaluation fixtures cover five reviewer dimensions", QualityEvaluationFixturesCoverFiveReviewerDimensionsAsync)
    };

    public static async Task<int> RunStandaloneAsync()
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
        var profile = planner.BuildProfile("玄幻", "学院流");

        Check.Equal(6, profile.PleasureStrength, "Genre profile should stay neutral; Agent supplies explicit candidateDirections.");
        Check.Equal(5, profile.MysteryStrength, "Genre profile should not infer suspense strength from raw labels.");
        Check.Equal(5, profile.EmotionStrength, "Genre profile should not infer emotion strength from raw labels.");
        Check.Equal(6, profile.WorldbuildingStrength, "Genre profile should not infer worldbuilding strength from raw labels.");
        Check.Contains("保持清晰主线", profile.Strategy, "Default profile should give only generic planning guidance.");
        Check.True(profile.RiskWarnings.Any(w => w.Contains("不改变故事状态", StringComparison.OrdinalIgnoreCase)),
            "Genre profile should keep generic state-change risk guidance.");

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
            CandidateDirections =
            {
                "规则裂纹升级破局",
                "学院试炼与债务代价",
                "师徒关系拉扯推进主线"
            }
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
        Check.True(candidates.All(c => !string.IsNullOrWhiteSpace(c.Title) && !string.IsNullOrWhiteSpace(c.CoreHook)),
            "Macro candidates should be generated from the current brief, not from empty placeholders.");
        Check.True(candidates.Any(c => c.Risks.Any(r => r.Contains("线索", StringComparison.OrdinalIgnoreCase)
                                                        || r.Contains("代价账本", StringComparison.OrdinalIgnoreCase))),
            "Macro candidates should carry execution risks, not only ideas.");

        return Task.CompletedTask;
    }

    private static Task BookConceptDesignerRemovesRejectedMacroTemplates()
    {
        var designer = new BookConceptDesigner(new GenreDirectionPlanner());
        var request = new StoryFoundationRequest
        {
            UserSeed = "灵气复苏末日，主角猥琐发育，带系统金手指，核心就是一路打怪升级，突破境界，碾压敌人。",
            Genre = "末世打怪升级爽文",
            CandidateDirections =
            {
                "连续战斗升级变强",
                "怪物晶核资源争夺",
                "基地扩张与战力碾压"
            },
            ForbiddenDirections = { "规则反噬型", "真相递进型", "关系代价型" }
        };

        var candidates = designer.GenerateMacroCandidates(request).ToList();
        var allText = string.Join("\n", candidates.Select(c => $"{c.Title} {c.CoreHook} {c.WorldCoreRule} {c.MainConflictEngine}"));

        Check.True(candidates.Count >= 3, "Designer should still produce multiple macro candidates after deleting fixed templates.");
        Check.DoesNotContain("规则反噬", allText, "Rejected rule-backlash template must not appear in generated macro candidates.");
        Check.DoesNotContain("真相递进", allText, "Rejected truth-ladder template must not appear in generated macro candidates.");
        Check.DoesNotContain("关系代价", allText, "Rejected relationship-cost template must not appear in generated macro candidates.");
        Check.True(candidates.Any(c => (c.Title + c.CoreHook + c.MainConflictEngine).Contains("打怪", StringComparison.OrdinalIgnoreCase)
                                      || (c.Title + c.CoreHook + c.MainConflictEngine).Contains("升级", StringComparison.OrdinalIgnoreCase)),
            "Generated candidates should follow the user's positive direction.");
        Check.True(candidates.All(c => !string.IsNullOrWhiteSpace(c.ProgressionSystem)
                                      && !string.IsNullOrWhiteSpace(c.WorldbuildingBlueprint)
                                      && !string.IsNullOrWhiteSpace(c.ProtagonistProfile)
                                      && !string.IsNullOrWhiteSpace(c.PleasureLoop)
                                      && c.FirstThreeVolumes.Count >= 3
                                      && c.KeyCharacters.Count >= 5),
            "Story foundation candidates must be complete enough to feed volume and chapter planning, not just thin hook summaries.");

        return Task.CompletedTask;
    }

    private static Task BookConceptDesignerKeepsPositivePowerFantasyDirectionWithLongExclusions()
    {
        var designer = new BookConceptDesigner(new GenreDirectionPlanner());
        var request = new StoryFoundationRequest
        {
            UserSeed = "末世玄幻爽文，男主从底层幸存者开始，系统吞噬怪物晶核升级，一路打怪升级、建基地、收伙伴。后宫流，升级系统，灵气复苏，征服类剧情，纯打怪升级变强流，碾压流。",
            Genre = "末世玄幻",
            SubGenre = "升级流爽文",
            CandidateDirections =
            {
                "开篇绝境求生+首次觉醒系统的冲击力",
                "末世社会生态+男主的底层视角代入感",
                "吞噬系统的独特机制展示+首次战力碾压的爽感爆发"
            },
            ForbiddenDirections =
            {
                "规则反噬：不要设计系统反噬或升级代价机制",
                "真相递进：不要隐藏世界真相逐步揭露的阴谋线",
                "关系代价：不要让感情关系成为力量获取的代价或束缚",
                "无成本碾压：不要让爽点完全没有后续影响"
            }
        };

        var candidates = designer.GenerateMacroCandidates(request).ToList();
        var allText = string.Join("\n", candidates.Select(c => $"{c.Title} {c.CoreHook} {c.WorldCoreRule} {c.MainConflictEngine}"));

        Check.True(candidates.Count >= 3,
            "Long exclusion descriptions should not erase the user's positive power-fantasy direction.");
        Check.True(candidates.Any(c => (c.Title + c.CoreHook + c.MainConflictEngine).Contains("打怪", StringComparison.OrdinalIgnoreCase)
                                      || (c.Title + c.CoreHook + c.MainConflictEngine).Contains("升级", StringComparison.OrdinalIgnoreCase)
                                      || (c.Title + c.CoreHook + c.MainConflictEngine).Contains("吞噬", StringComparison.OrdinalIgnoreCase)),
            "Generated candidates should preserve monster-core devouring and power-growth fantasy.");
        Check.DoesNotContain("规则反噬", allText, "Rejected rule-backlash direction must not appear.");
        Check.DoesNotContain("真相递进", allText, "Rejected truth-ladder direction must not appear.");
        Check.DoesNotContain("关系代价", allText, "Rejected relationship-cost direction must not appear.");

        return Task.CompletedTask;
    }

    private static Task BookConceptDesignerIgnoresForbiddenPhrasesEmbeddedInNaturalSeed()
    {
        var designer = new BookConceptDesigner(new GenreDirectionPlanner());
        var request = new StoryFoundationRequest
        {
            UserSeed = "新写一本小说，书名《裂穹机兵：从冻土矿奴到天轨霸主》，类型是废土机甲打怪升级爽文。请直接开始执行，先建立故事地基，包含世界观、升级体系、主角、爽点循环、前三卷、首批角色。不要规则反噬、真相递进、关系代价候选。",
            Genre = "废土机甲打怪升级爽文",
            CandidateDirections =
            {
                "机甲融合与进化升级体系，主角机甲可吞噬怪物核心进化",
                "废土末世资源争夺，矿奴身份底层逆袭",
                "天轨系统作为升级与战力衡量的核心设定",
                "碾压式战斗爽感，以弱胜强后持续升级碾压"
            },
            ForbiddenDirections =
            {
                "规则反噬：升级或使用能力会带来负面代价的设定",
                "真相递进：世界真相层层剥开、最终发现一切都是骗局的叙事",
                "关系代价：角色关系发展需要牺牲能力或资源作为代价的设计",
                "虐主向：长期压迫主角、主角反复受挫的叙事"
            }
        };

        var candidates = designer.GenerateMacroCandidates(request).ToList();
        var allText = string.Join("\n", candidates.Select(c => $"{c.Title} {c.CoreHook} {c.WorldbuildingBlueprint} {c.ProgressionSystem} {c.PleasureLoop}"));

        Check.True(candidates.Count >= 3,
            "Natural full-prompt seeds that include forbidden phrases should still generate usable macro candidates.");
        Check.True(candidates.Any(c => allText.Contains("机甲", StringComparison.OrdinalIgnoreCase)
                                      || allText.Contains("战甲", StringComparison.OrdinalIgnoreCase)),
            "Candidates should preserve the positive mecha/power progression direction.");
        Check.DoesNotContain("规则反噬", allText, "Forbidden phrases embedded in the user's natural prompt must not leak into candidate text.");
        Check.DoesNotContain("真相递进", allText, "Forbidden phrases embedded in the user's natural prompt must not leak into candidate text.");
        Check.DoesNotContain("关系代价", allText, "Forbidden phrases embedded in the user's natural prompt must not leak into candidate text.");
        Check.DoesNotContain("请直接开始执行", allText, "Operational instructions must not become story concept text.");
        Check.DoesNotContain("先建立故事地基", allText, "Workflow instructions must not become story concept text.");
        Check.DoesNotContain("包含世界观", allText, "Checklist wording must not become story concept text.");
        Check.DoesNotContain("首批角色", allText, "Checklist wording must not become story concept text.");

        return Task.CompletedTask;
    }

    private static async Task StoryFoundationCommitUsesStructuredCandidateIdentityAsync()
    {
        var orchestrator = CreateOrchestrator(CreateChapterContext("chapter-identity-001"));
        var run = await orchestrator.PlanStoryFoundationAsync(new StoryFoundationRequest
        {
            UserSeed = "一个能听见世界规则裂纹的少年，被迫用成长偿还规则债务。",
            Genre = "玄幻",
            SubGenre = "学院流",
            CandidateDirections = { "规则裂纹升级流", "真相递进型", "关系代价型" }
        });
        var first = run.MacroCandidates[0];
        var byIndex = await orchestrator.CommitStoryFoundationAsync(
            run.RunId,
            overwrite: false,
            confirmed: true,
            selectedMacroCandidateId: string.Empty,
            selectedMacroCandidateIndex: 1);
        Check.True(byIndex.Success,
            "A valid candidateIndex should commit even when the title was misspelled by the LLM.");
        Check.Equal(first.Title, byIndex.Document?.Constitution?.NoveltyPoint,
            "Index-based commit should apply the canonical selected candidate.");
        orchestrator = CreateOrchestrator(CreateChapterContext("chapter-identity-002"));
        run = await orchestrator.PlanStoryFoundationAsync(new StoryFoundationRequest
        {
            UserSeed = "一个能听见世界规则裂纹的少年，被迫用成长偿还规则债务。",
            Genre = "玄幻",
            SubGenre = "学院流",
            CandidateDirections = { "规则裂纹升级流", "真相递进型", "关系代价型" }
        });
        var second = run.MacroCandidates[1];
        var byId = await orchestrator.CommitStoryFoundationAsync(
            run.RunId,
            overwrite: false,
            confirmed: true,
            selectedMacroCandidateId: second.CandidateId,
            selectedMacroCandidateIndex: 0);
        Check.True(byId.Success,
            "A valid candidateId should commit even when the title is wrong.");
        Check.Equal(second.Title, byId.Document?.Constitution?.NoveltyPoint,
            "Id-based commit should apply the canonical selected candidate.");
        orchestrator = CreateOrchestrator(CreateChapterContext("chapter-identity-003"));
        run = await orchestrator.PlanStoryFoundationAsync(new StoryFoundationRequest
        {
            UserSeed = "一个能听见世界规则裂纹的少年，被迫用成长偿还规则债务。",
            Genre = "玄幻",
            SubGenre = "学院流",
            CandidateDirections = { "规则裂纹升级流", "真相递进型", "关系代价型" }
        });
        var missingIdentity = await orchestrator.CommitStoryFoundationAsync(
            run.RunId,
            overwrite: false,
            confirmed: true);
        Check.True(!missingIdentity.Success,
            "A commit without candidateId/index should not silently commit the default constitution.");
        Check.Contains("候选 ID", missingIdentity.Message,
            "Missing candidate identity should explain that a stable ID or index is required.");
    }

    private static async Task CreativeKnowledgeBaseRetrievesBuiltInGuidanceAsync()
    {
        var service = new CreativeKnowledgeBaseService();
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

        var result = await service.RetrieveAsync(
            "玄幻 爽文 规则反噬 关系变化 关键人物刚好路过救场 反套路 代价 信息差",
            constitution,
            new[] { "关键人物刚好路过救场" },
            topK: 12);

        Check.True(result.Success, "Creative knowledge retrieval should succeed.");
        Check.True(result.Hits.Count >= 6, "Creative knowledge retrieval should return built-in genre, trope and relationship guidance.");
        Check.True(result.Hits.Any(h => h.Entry.Category == CreativeKnowledgeCategory.RelationshipDynamic),
            "Creative knowledge retrieval should include relationship-dynamic guidance.");
        Check.True(result.GenrePrinciples.Any(p => p.Contains("代价", StringComparison.OrdinalIgnoreCase)),
            "Genre principles should retrieve cost-return guidance for xuanhuan/s爽.");
        Check.True(result.TropeWarnings.Any(p => p.Contains("救场", StringComparison.OrdinalIgnoreCase)),
            "Trope warnings should retrieve the already-risky rescue pattern.");
        Check.True(result.AntiTropeStrategies.Any(p => p.Contains("代价", StringComparison.OrdinalIgnoreCase)
                                                       || p.Contains("信息来源", StringComparison.OrdinalIgnoreCase)),
            "Anti-trope strategies should retrieve actionable variation strategies.");
        Check.True(result.Hits.Any(h => h.Reason.Contains("命中项目已用桥段风险", StringComparison.OrdinalIgnoreCase)),
            "Used plot patterns should increase retrieval score with an explicit reason.");
    }

    private static async Task NovelAgentOrchestratorRunsFoundationAndChapterPlanningLoopAsync()
    {
        var orchestrator = CreateOrchestrator(new ContentTaskContext
        {
            ChapterId = "chapter-001",
            Title = "第一章 裂纹初响",
            Summary = "主角第一次听见规则裂纹。",
            ChapterPlan = new ChapterData
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
            CandidateDirections = { "规则裂纹升级流", "真相递进型", "关系代价型" }
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
            selectedMacroCandidateId: selectedMacro.CandidateId,
            selectedMacroCandidateIndex: 0);
        Check.True(commit.Success, "Selected macro candidate should be committed to Story Bible.");
        Check.Equal(selectedMacro.Title, commit.Document?.Constitution?.NoveltyPoint,
            "Committed Story Bible should reflect the selected macro candidate.");
        Check.True(commit.Document?.AgentRuns.Any(r => r.RunId == foundationRun.RunId
                                                       && r.Status == NovelAgentRunStatus.Completed) == true,
            "Foundation Run should be marked completed after Story Bible commit.");

        var chapterRun = await orchestrator.PlanChapterAsync(new ChapterCreativeRequest
        {
            ChapterId = "chapter-001",
            UserGoal = "写出第一章：主角初次发现规则裂纹，但必须付出一个可追踪代价。",
            CandidateDirections =
            {
                "规则裂纹初显",
                "代价承诺",
                "证据误导",
                "人物关系代价",
                "世界规则压力",
                "旧细节伏笔"
            }
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
        var orchestrator = CreateOrchestrator(CreateChapterContext("chapter-002"));

        var foundationRun = await orchestrator.PlanStoryFoundationAsync(new StoryFoundationRequest
        {
            UserSeed = "一个能听见世界规则裂纹的少年，被迫用成长偿还规则债务。",
            Genre = "玄幻",
            SubGenre = "学院流",
            CandidateDirections = { "规则裂纹升级流", "真相递进型", "关系代价型" }
        });
        var foundationCommit = await orchestrator.CommitStoryFoundationAsync(
            foundationRun.RunId,
            overwrite: false,
            confirmed: true,
            selectedMacroCandidateId: foundationRun.MacroCandidates.First().CandidateId,
            selectedMacroCandidateIndex: 0);
        Check.True(foundationCommit.Success, "Execution loop needs a committed Story Bible first.");

        var chapterRun = await orchestrator.PlanChapterAsync(new ChapterCreativeRequest
        {
            ChapterId = "chapter-002",
            UserGoal = "写第二章：让主角第一次主动利用规则裂纹，但留下关系代价。",
            CandidateDirections =
            {
                "规则裂纹主动利用",
                "关系代价承接",
                "世界规则压力升级"
            }
        });
        var selection = await orchestrator.SelectChapterCandidateAsync(
            chapterRun.RunId,
            chapterRun.ChapterBrief!.RecommendedCandidateTitle,
            "Recommended",
            "Regression confirmed recommended candidate before execution.",
            confirmed: true);
        Check.True(selection.Success, "Execution loop requires a confirmed chapter candidate.");

        var contextPackage = await orchestrator.BuildContextPackageStageAsync(chapterRun.RunId);
        Check.True(contextPackage.Success, "Split writing chain should build the chapter context package first.");
        Check.True(contextPackage.ContextPackage?.Status.StartsWith("context_ready", StringComparison.OrdinalIgnoreCase) == true,
            "Context package should be marked ready before draft generation.");

        var unconfirmedDraft = await orchestrator.GenerateChapterDraftStageAsync(chapterRun.RunId, confirmed: false);
        Check.True(!unconfirmedDraft.Success && unconfirmedDraft.RequiresConfirmation && unconfirmedDraft.RiskLevel == NovelToolRiskLevel.High,
            "Draft generation should require high-risk confirmation.");

        var draft = await orchestrator.GenerateChapterDraftStageAsync(chapterRun.RunId, confirmed: true);
        Check.True(draft.Success, "Confirmed split draft generation should execute the draft generation stage.");
        Check.True(draft.DraftArtifact != null, "Draft artifact should be attached to the run even when LLM settings block writing.");
        Check.True(draft.Run?.Steps.Any(s => s.ToolName == NovelAgentProductionStages.StepToolName(NovelAgentProductionStages.DraftGeneration)
                                             && s.Status == NovelAgentStepStatus.Completed) == true,
            "Split writing chain should mark draft generation completed.");
        Check.True(draft.Run?.Steps.Any(s => s.ToolName == "Writer.GenerateChapter") != true,
            "Split writing chain must not use the removed Writer.GenerateChapter step.");

        var gate = await orchestrator.ValidateDraftGateStageAsync(chapterRun.RunId);
        Check.True(!gate.Success, "Missing CHANGES or blocked draft should fail the hard GenerationGate.");
        Check.Equal("gate_failed", gate.GateReport?.Status ?? string.Empty,
            "GenerationGate should fail blocked or invalid drafts.");
        Check.True(gate.Run?.Steps.Any(s => s.ToolName == NovelAgentProductionStages.StepToolName(NovelAgentProductionStages.GateValidation)
                                            && s.Status == NovelAgentStepStatus.Failed) == true,
            "Gate validation should record failure.");

        var commitBlocked = await orchestrator.CommitChapterStageAsync(chapterRun.RunId, confirmed: true);
        Check.True(!commitBlocked.Success,
            "Chapter commit must refuse chapters that did not pass GenerationGate.");
    }

    private static async Task StoryBiblePersistsCoreAgentStateAsync()
    {
        var service = new StoryBibleService(new InMemoryStoryBibleDocumentStore());

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
    }

    private static async Task CharacterLedgerImportsLowRiskReviewEntriesAsync()
    {
        var storyBible = new StoryBibleService(new InMemoryStoryBibleDocumentStore());
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
        var storyBible = new StoryBibleService(new InMemoryStoryBibleDocumentStore());
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

    private static Task VolumeArcPlannerFollowsPowerProgressionDirection()
    {
        var planner = new VolumeArcPlanner();
        var plan = planner.BuildPlan(new VolumeArcPlanningRequest
        {
            UserGoal = "这一卷就写主角一路打怪升级、抢资源点、突破境界，不要规则展示、不要第一次错误胜利、不要代价显形、不要中段反转、不要伏笔回收。",
            ExpectedChapterCount = 12,
            CandidateDirections = { "怪物压力升级", "资源点争夺", "境界突破门槛", "强敌压迫" },
            ForbiddenDirections = { "规则展示", "第一次错误胜利", "代价显形", "中段反转", "伏笔回收" }
        }, new StoryCreativeConstitution
        {
            Genre = "末世打怪升级爽文",
            MainPleasure = "打怪、升级、资源获取和碾压强敌。",
            GenreProfile = new GenreDirectionProfile
            {
                PleasureStrength = 9,
                PaceStrength = 9,
                WorldbuildingStrength = 7
            }
        }, knowledge: null);

        var planText = string.Join("\n", new[]
        {
            plan.Title,
            plan.CoreQuestion,
            plan.MidpointReversal,
            plan.Climax,
            string.Join("\n", plan.ChapterBeats.Select(b => $"{b.Role} {b.Goal} {b.Turn} {b.Cost}")),
            string.Join("\n", plan.ForeshadowingPlan.Select(f => $"{f.Name} {f.Setup} {f.Payoff}"))
        });

        Check.Contains("打怪", planText + " " + plan.Title,
            "Power progression volume should preserve monster-fighting direction.");
        Check.True(plan.ChapterBeats.Any(b => (b.Role + b.Goal + b.Turn).Contains("资源", StringComparison.OrdinalIgnoreCase)
                                           || (b.Role + b.Goal + b.Turn).Contains("境界", StringComparison.OrdinalIgnoreCase)),
            "Power progression volume should include resource or realm progression beats.");
        Check.DoesNotContain("规则展示", planText, "Rejected fixed volume beat must not appear.");
        Check.DoesNotContain("第一次错误胜利", planText, "Rejected fixed volume beat must not appear.");
        Check.DoesNotContain("代价显形", planText, "Rejected fixed volume beat must not appear.");
        Check.DoesNotContain("中段反转", planText, "Rejected fixed volume beat must not appear.");
        Check.DoesNotContain("伏笔回收", planText, "Rejected fixed volume beat must not appear.");

        return Task.CompletedTask;
    }

    private static Task ChapterPlannerRemovesRejectedPlotTemplates()
    {
        var planner = new ChapterNoveltyPlanner();
        var brief = planner.BuildBrief(new ChapterCreativeRequest
        {
            UserGoal = "写主角一路打怪升级，不要规则反噬，不要认知反转，不要关系破局。",
            ActiveConflicts = { "怪潮压境" },
            CharacterStates = { "主角正在猥琐发育" },
            Constitution = new StoryCreativeConstitution
            {
                Genre = "末世打怪升级爽文",
                MainPleasure = "打怪、升级、资源获取和碾压强敌。",
                GenreProfile = new GenreDirectionProfile
                {
                    PleasureStrength = 9,
                    PaceStrength = 9,
                    WorldbuildingStrength = 7,
                    Strategy = "用打怪升级循环兑现爽点。"
                },
                ForbiddenDirections = { "规则反噬", "认知反转", "关系破局" }
            }
        });

        var candidateText = string.Join("\n", brief.Candidates.Select(c => $"{c.Title} {c.CoreTwist} {c.ConflictMove}"));
        Check.DoesNotContain("规则反噬", candidateText, "Rejected rule-backlash plot template must not appear.");
        Check.DoesNotContain("认知反转", candidateText, "Rejected cognitive-reversal plot template must not appear.");
        Check.DoesNotContain("关系破局", candidateText, "Rejected relationship-breakthrough plot template must not appear.");
        Check.DoesNotContain("失败推进", candidateText, "Unrequested failure-progression template must not be injected into pure leveling candidates.");
        Check.DoesNotContain("代价交换", candidateText, "Unrequested cost-exchange template must not be injected into pure leveling candidates.");
        Check.DoesNotContain("伏笔回收", candidateText, "Unrequested foreshadowing template must not be injected into pure leveling candidates.");
        Check.True(brief.Candidates.Any(c => (c.Title + c.CoreTwist + c.ConflictMove).Contains("打怪", StringComparison.OrdinalIgnoreCase)
                                           || (c.Title + c.CoreTwist + c.ConflictMove).Contains("升级", StringComparison.OrdinalIgnoreCase)),
            "Chapter candidates should follow the positive chapter direction.");

        return Task.CompletedTask;
    }

    private static Task ChapterPlannerRespectsDirectForbiddenDirections()
    {
        var planner = new ChapterNoveltyPlanner();
        var brief = planner.BuildBrief(new ChapterCreativeRequest
        {
            UserGoal = "这一章写主角和女机械师谈判结盟，重点是阵营拉扯。",
            ActiveConflicts = { "斗场怪物压境" },
            CharacterStates = { "主角还是底层维修工" },
            CandidateDirections = { "人物立场重组推进主线" },
            ForbiddenDirections = { "人物立场重组", "关系", "认知改写" },
            Constitution = new StoryCreativeConstitution
            {
                Genre = "废土机甲打怪升级爽文",
                MainPleasure = "打怪、升级、资源获取和碾压强敌。",
                GenreProfile = new GenreDirectionProfile
                {
                    PleasureStrength = 9,
                    PaceStrength = 9,
                    WorldbuildingStrength = 7,
                    Strategy = "用战斗和改装成长兑现爽点。"
                }
            }
        });

        var candidateText = string.Join("\n", brief.Candidates.Select(c => $"{c.Title} {c.CoreTwist} {c.ConflictMove} {c.CharacterChoice}"));
        Check.DoesNotContain("人物立场", candidateText, "Direct forbidden directions must remove matching chapter candidates.");
        Check.DoesNotContain("关系", candidateText, "Direct forbidden directions must remove matching chapter candidates.");
        Check.True(brief.Candidates.Any(c => (c.Title + c.CoreTwist + c.ConflictMove).Contains("战斗", StringComparison.OrdinalIgnoreCase)
                                           || (c.Title + c.CoreTwist + c.ConflictMove).Contains("资源", StringComparison.OrdinalIgnoreCase)
                                           || (c.Title + c.CoreTwist + c.ConflictMove).Contains("升级", StringComparison.OrdinalIgnoreCase)),
            "Chapter candidates should keep positive combat progression after filtering direct forbidden directions.");

        return Task.CompletedTask;
    }

    private static async Task StoryStateSnapshotUsesVectorRecallAsync()
    {

        var guideContext = new FakeGuideContextService(new ContentTaskContext
        {
            Title = "第十八章 禁书楼钥匙",
            ChapterId = "chapter-018",
            PreviousChapterId = "chapter-017",
            ChapterPlan = new ChapterData
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

        var chunkSearch = new InMemoryContentChunkSearchDouble();
        chunkSearch.AddChunk(
            "chapter-006",
            2,
            "导师第一次看见钥匙变冷，立刻按住林昼的手，像是在阻止一笔旧债苏醒。");
        chunkSearch.AddChunk(
            "chapter-009",
            1,
            "禁书楼的旧钥匙会记住持有者的恐惧，恐惧越深，开门时讨还越重。");

        var chapterIndex = new InMemoryChapterVectorIndexDouble();
        chapterIndex.AddHit("chapter-006", 0.93f);
        chapterIndex.AddHit("chapter-009", 0.91f);
        chapterIndex.AddHit("chapter-018", 0.99f);
        chapterIndex.AddHit("chapter-017", 0.98f);

        var chunkIndex = new InMemoryChunkVectorIndexDouble();
        chunkIndex.AddHit("chapter-006", 2, 0.96f);
        chunkIndex.AddHit("chapter-009", 1, 0.94f);
        chunkIndex.AddHit("chapter-018", 1, 0.99f);

        var service = new StoryStateSnapshotService(
            guideContext,
            chunkSearch,
            new StoryBibleService(new InMemoryStoryBibleDocumentStore()),
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
        var service = new StoryBibleService(new InMemoryStoryBibleDocumentStore());
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
        var service = new StoryBibleService(new InMemoryStoryBibleDocumentStore());
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
        var storyBible = await LoadSampleStoryBibleAsync();
        var chapterFixture = await LoadSampleChaptersAsync();
        Check.True(chapterFixture.Chapters.Count >= 3, "Sample chapter fixture should include multiple chapter samples.");

        var chunkSearch = new InMemoryContentChunkSearchDouble();
        var chapterIndex = new InMemoryChapterVectorIndexDouble();
        var chunkIndex = new InMemoryChunkVectorIndexDouble();

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
            ChapterPlan = new ChapterData
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
            new StoryBibleService(new InMemoryStoryBibleDocumentStore()),
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
        var storyBibleService = new StoryBibleService(new InMemoryStoryBibleDocumentStore());
        var guideContextService = new FakeGuideContextService(context);
        var contentChunkSearch = new InMemoryContentChunkSearchDouble();
        var generatedContent = new FakeGeneratedContentService();
        var scopeFactory = CreateRegressionScopeFactory();
        IWorkspaceProductionRuntimeBuilder runtimeBuilder = new WorkspaceProductionRuntimeBuilder();
        var runtime = runtimeBuilder.Build(new WorkspaceProductionRuntimeRequest(
            "regression-user",
            "regression-project",
            storyBibleService,
            new CreativeKnowledgeBaseService(),
            RegressionUserSettingsFactory.CreateDbBacked("regression-user"),
            VectorStore: null,
            EmbeddingService: new FakeEmbeddingService(),
            CurrentUserService: null,
            MemoryRepository: null,
            ScopeFactory: scopeFactory,
            UnifiedValidationService: new FakeUnifiedValidationService(),
            GuideContextService: guideContextService,
            ContentChunkSearchService: contentChunkSearch,
            GeneratedContentService: generatedContent));

        return runtime.Orchestrator;
    }

    private static IServiceScopeFactory CreateRegressionScopeFactory()
    {
        var services = new ServiceCollection();
        services.AddDbContext<NovelAgentDbContext>(options =>
            options.UseInMemoryDatabase($"NovelAgentRegression_{Guid.NewGuid():N}"));
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
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
            ChapterPlan = new ChapterData
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

        public Task InitializeCacheAsync() => Task.CompletedTask;

        public void ClearCache()
        {
        }

        public Task<List<CharacterRulesData>> ExtractCharactersAsync(List<string>? ids) => Task.FromResult(new List<CharacterRulesData>());

        public Task<List<CharacterRulesData>> GetAllCharactersAsync() => Task.FromResult(new List<CharacterRulesData>());

        public Task<List<LocationRulesData>> ExtractLocationsAsync(List<string>? ids) => Task.FromResult(new List<LocationRulesData>());

        public Task<List<LocationRulesData>> GetAllLocationsAsync() => Task.FromResult(new List<LocationRulesData>());

        public Task<List<PlotRulesData>> ExtractPlotRulesAsync(List<string>? ids) => Task.FromResult(new List<PlotRulesData>());

        public Task<List<PlotRulesData>> GetAllPlotRulesAsync() => Task.FromResult(new List<PlotRulesData>());

        public Task<List<FactionRulesData>> ExtractFactionsAsync(List<string>? ids) => Task.FromResult(new List<FactionRulesData>());

        public Task<List<FactionRulesData>> GetAllFactionsAsync() => Task.FromResult(new List<FactionRulesData>());

        public Task<List<CreativeMaterialData>> ExtractTemplatesAsync(List<string>? ids) => Task.FromResult(new List<CreativeMaterialData>());

        public Task<List<CreativeMaterialData>> GetAllTemplatesAsync() => Task.FromResult(new List<CreativeMaterialData>());

        public Task<List<WorldRulesData>> ExtractWorldRulesAsync(List<string>? ids) => Task.FromResult(new List<WorldRulesData>());

        public Task<List<WorldRulesData>> GetAllWorldRulesAsync() => Task.FromResult(new List<WorldRulesData>());

        public Task<OutlineData> ExtractVolumeAsync(string volumeId) => Task.FromResult(new OutlineData());

        public Task<ChapterData?> ExtractChapterPlanAsync(string chapterPlanId) => Task.FromResult<ChapterData?>(_context.ChapterPlan);

        public Task<List<BlueprintData>> ExtractBlueprintsAsync(List<string>? blueprintIds) => Task.FromResult(new List<BlueprintData>());

        public Task<VolumeDesignData?> ExtractVolumeDesignAsync(string volumeDesignId) => Task.FromResult<VolumeDesignData?>(null);

        public Task<List<OutlineData>> ExtractPreviousOutlinesAsync(List<string> outlineIds) => Task.FromResult(new List<OutlineData>());

        public Task<ContextIdValidationResult> ValidateContextIdsAsync(ContextIdCollection? contextIds) =>
            Task.FromResult(new ContextIdValidationResult());

        public Task<OutlineTaskContext?> BuildOutlineContextAsync(string volumeId) => Task.FromResult<OutlineTaskContext?>(null);

        public Task<PlanningTaskContext?> BuildPlanningContextAsync(string volumeId) => Task.FromResult<PlanningTaskContext?>(null);

        public Task<BlueprintTaskContext?> BuildBlueprintContextAsync(string chapterId) => Task.FromResult<BlueprintTaskContext?>(null);

        public Task<string?> GetChapterTitleAsync(string chapterId) => Task.FromResult<string?>(_context.Title);

        public Task<int> GetVolumeMaxChapterAsync(int volumeNumber) => Task.FromResult(0);

        public Task<FactSnapshot> ExtractFactSnapshotForChapterAsync(string chapterId, ContextIdCollection contextIds) =>
            Task.FromResult(_context.FactSnapshot ?? new FactSnapshot());

        public Task<ContentGuide> GetContentGuideAsync() => Task.FromResult(new ContentGuide());

        public void InvalidateContentGuideCache()
        {
        }

        public Task<string> GetChapterSummaryAsync(string chapterId) => Task.FromResult(_context.Summary);

        public Task<(List<IndexItem> Direct, List<IndexItem> Indirect)> GetRelatedEntitiesAsync(string focusId, string layer) =>
            Task.FromResult((new List<IndexItem>(), new List<IndexItem>()));
    }

    private sealed class FakeGeneratedContentService : IGeneratedContentService
    {
        private readonly Dictionary<string, string> _chapters = new(StringComparer.OrdinalIgnoreCase);

        public Task SaveChapterAsync(string chapterId, string content)
        {
            _chapters[chapterId] = content;
            return Task.CompletedTask;
        }

        public Task<string?> GetChapterAsync(string chapterId)
        {
            _chapters.TryGetValue(chapterId, out var content);
            return Task.FromResult<string?>(content);
        }

        public Task<bool> DeleteChapterAsync(string chapterId) =>
            Task.FromResult(_chapters.Remove(chapterId));

        public bool ChapterExists(string chapterId) => _chapters.ContainsKey(chapterId);

        public Task<List<ChapterInfo>> GetGeneratedChaptersAsync() => Task.FromResult(new List<ChapterInfo>());

        public Task<bool> VolumeExistsAsync(int volumeNumber) => Task.FromResult(false);

        public Task<string> GenerateNextChapterIdFromSourceAsync(string sourceChapterId) =>
            Task.FromResult(string.IsNullOrWhiteSpace(sourceChapterId) ? "chapter-001" : sourceChapterId);
    }

    private sealed class FakeUnifiedValidationService : IUnifiedValidationService
    {
        public Task<ChapterValidationResult> ValidateChapterAsync(string chapterId, CancellationToken ct = default) =>
            Task.FromResult(new ChapterValidationResult
            {
                ChapterId = chapterId,
                OverallResult = "通过"
            });

        public Task<VolumeValidationResult> ValidateVolumeAsync(int volumeNumber, CancellationToken ct = default) =>
            Task.FromResult(new VolumeValidationResult
            {
                VolumeNumber = volumeNumber
            });

        public Task<bool> NeedsRepublishAsync() => Task.FromResult(false);
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
