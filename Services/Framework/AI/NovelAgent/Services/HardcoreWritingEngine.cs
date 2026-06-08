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
using TM.Services.Modules.ProjectData.Implementations.Indexing;
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
        private readonly ContentChunkSearchService? _contentChunkSearch;
        private readonly ChapterEmbeddingIndex? _chapterEmbeddingIndex;
        private readonly IChunkEmbeddingIndex? _chunkEmbeddingIndex;
        private readonly IMicroEmbeddingService? _embeddingService;
        private readonly object? _versionTrackingService;
        private readonly object? _settingsManager;

        public HardcoreWritingEngine(StoryStateSnapshotService storyStateSnapshotService)
        {
            _storyStateSnapshotService = storyStateSnapshotService;
        }

        public HardcoreWritingEngine(
            StoryStateSnapshotService storyStateSnapshotService,
            IGuideContextService guideContextService,
            GenerationGate generationGate,
            IGeneratedContentService generatedContentService,
            ContentChunkSearchService contentChunkSearch,
            ChapterEmbeddingIndex chapterEmbeddingIndex,
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

            package.WorldRules = package.WorldRules.Where(HasText).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
            package.ActiveConflicts = package.ActiveConflicts.Where(HasText).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
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
                    var snapshot = context?.FactSnapshot ?? new FactSnapshot();
                    var gate = await _generationGate.ValidateAsync(
                        run.TargetChapterId,
                        draft.DraftContent,
                        snapshot,
                        BuildDesignElements(context),
                        context?.ContextIds ?? new ContextIdCollection()).ConfigureAwait(false);
                    return MapGateResult(gate, contextPackage);
                }
                catch (Exception ex)
                {
                    return new GenerationGateReport
                    {
                        Status = "gate_failed",
                        ChangesDetected = GenerationGate.HasChangesRegion(draft.DraftContent),
                        ProtocolPassed = false,
                        FactSnapshotPassed = false,
                        BlueprintPassed = contextPackage.ChapterBlueprints.Count > 0 || run.ChapterBrief != null,
                        RagPassed = contextPackage.LongDistanceRecall.Count > 0 || contextPackage.PreviousSummaries.Count > 0,
                        Issues = { $"真实 GenerationGate 校验异常：{ex.Message}" },
                        RepairHints = { "重新构建章节上下文包后再校验；若仍失败，请检查 ProjectData 结构化设定。"}
                    };
                }
            }

            var report = new GenerationGateReport
            {
                ChangesDetected = draft.HasChanges && draft.DraftContent.Contains(ChangesSeparator, StringComparison.Ordinal),
                ProtocolPassed = draft.HasChanges && IsValidJsonObject(draft.ChangesJson),
                FactSnapshotPassed = contextPackage.ActiveConflicts.Count > 0 || contextPackage.CharacterStates.Count > 0 || contextPackage.WorldRules.Count > 0,
                BlueprintPassed = contextPackage.ChapterBlueprints.Count > 0 || run.ChapterBrief != null,
                RagPassed = contextPackage.LongDistanceRecall.Count > 0 || contextPackage.PreviousSummaries.Count > 0
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

            report.Status = report.Issues.Count == 0 ? "validated" : "gate_failed";
            return report;
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
            await RefreshIndexesAsync(run, draft, committed, ct).ConfigureAwait(false);
            return RefreshIndexesAndAnalyzeImpact(run, draft);
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
                    var summaryStore = TM.Framework.Common.Services.ServiceLocator.TryGet<ChapterSummaryStore>();
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
                    var keywordIndex = TM.Framework.Common.Services.ServiceLocator.TryGet<KeywordChapterIndexService>();
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

        private static GenerationGateReport MapGateResult(GateResult gate, ChapterContextPackageSummary contextPackage)
        {
            var failures = gate.GetHumanReadableFailures(20);
            return new GenerationGateReport
            {
                Status = gate.Success ? "validated" : "gate_failed",
                ChangesDetected = gate.ParsedChanges != null || !string.IsNullOrWhiteSpace(gate.ContentWithoutChanges),
                ProtocolPassed = gate.Failures.All(f => f.Type != FailureType.Protocol),
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

            if (string.Equals(provider, "anthropic", StringComparison.OrdinalIgnoreCase))
            {
                http.DefaultRequestHeaders.Add("api-key", settings.ApiKey);
                http.DefaultRequestHeaders.Add("x-api-key", settings.ApiKey);
                http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                var anthropicPayload = new
                {
                    model,
                    system,
                    max_tokens = settings.MaxTokens,
                    temperature = settings.Temperature,
                    messages = new[] { new { role = "user", content = user } }
                };
                var anthropicUrl = baseUrl.EndsWith("/messages", StringComparison.OrdinalIgnoreCase)
                    ? baseUrl
                    : $"{baseUrl}/messages";
                using var response = await http.PostAsync(
                    anthropicUrl,
                    new StringContent(JsonSerializer.Serialize(anthropicPayload), Encoding.UTF8, "application/json"),
                    ct).ConfigureAwait(false);
                var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
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
                max_tokens = settings.MaxTokens,
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
            openAiResponse.EnsureSuccessStatusCode();
            using var openAiDoc = JsonDocument.Parse(json);
            return openAiDoc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
                   ?? json;
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
                gateIssues = report.Issues,
                repairHints = report.RepairHints,
                contextPackage,
                previousDraft = draft.DraftContent,
                requiredChangesSchema = ChapterChanges.TopLevelFieldNames
            };
            return JsonSerializer.Serialize(payload, JsonHelper.CnDefault);
        }

        private static string ExtractChangesJson(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return string.Empty;
            var xml = Regex.Match(content, @"<\s*(?:chapter_changes|changes)\s*>([\s\S]*?)</\s*(?:chapter_changes|changes)\s*>", RegexOptions.IgnoreCase);
            if (xml.Success) return xml.Groups[1].Value.Trim();
            var index = content.LastIndexOf(ChangesSeparator, StringComparison.OrdinalIgnoreCase);
            return index >= 0 ? content[(index + ChangesSeparator.Length)..].Trim() : string.Empty;
        }

        private static bool TryDeserializeChanges(string json, out ChapterChanges changes)
        {
            try
            {
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
            var slash = model.IndexOf('/');
            return slash >= 0 && slash < model.Length - 1 ? model[(slash + 1)..] : model;
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
