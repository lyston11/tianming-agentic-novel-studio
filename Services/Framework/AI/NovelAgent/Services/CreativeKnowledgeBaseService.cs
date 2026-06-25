using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.Embedding;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Services.VectorStore;

namespace TM.Services.Framework.AI.NovelAgent.Services
{
    public sealed class CreativeKnowledgeBaseService
    {
        private const int MaxHits = 16;

        private readonly IVectorStore? _vectorStore;
        private readonly IMicroEmbeddingService? _embeddingService;
        private readonly ICurrentUserService? _currentUserService;
        private readonly IAgentMemoryRepository? _memoryRepository;
        private readonly string? _projectId;
        private readonly IReadOnlyList<CreativeKnowledgeEntry> _builtInEntries;

        public CreativeKnowledgeBaseService(
            IVectorStore? vectorStore = null,
            IMicroEmbeddingService? embeddingService = null,
            ICurrentUserService? currentUserService = null,
            IAgentMemoryRepository? memoryRepository = null,
            string? projectId = null)
        {
            _vectorStore = vectorStore;
            _embeddingService = embeddingService;
            _currentUserService = currentUserService;
            _memoryRepository = memoryRepository;
            _projectId = string.IsNullOrWhiteSpace(projectId) ? null : projectId.Trim();
            _builtInEntries = BuildSeedEntries()
                .Select(Clone)
                .ToList();
        }

        public async Task<CreativeKnowledgeRetrievalResult> RetrieveAsync(
            string query,
            StoryCreativeConstitution? constitution = null,
            IEnumerable<string>? usedPlotPatterns = null,
            int topK = 8,
            CancellationToken ct = default)
        {
            var normalizedQuery = BuildQuery(query, constitution, usedPlotPatterns);

            if (_vectorStore == null || _embeddingService == null || _currentUserService == null)
            {
                return BuildTokenMatchingResult(normalizedQuery, constitution, usedPlotPatterns, topK);
            }

            try
            {
                var userId = _currentUserService.GetUserId();
                var projectId = _projectId;

                if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(projectId))
                {
                    return BuildTokenMatchingResult(normalizedQuery, constitution, usedPlotPatterns, topK);
                }

                var queryVector = await _embeddingService.EncodeAsync(normalizedQuery, EmbeddingMode.Query, ct)
                    .ConfigureAwait(false);
                var searchResults = await _vectorStore.SearchSimilarAsync(
                    userId,
                    queryVector,
                    topK * 2,
                    new Dictionary<string, object>
                    {
                        { "project_id", projectId },
                        { "source_type", "knowledge" }
                    },
                    ct).ConfigureAwait(false);

                ProjectMemory? projectMemory = null;
                AuthorMemory? authorMemory = null;

                if (_memoryRepository != null)
                {
                    try
                    {
                        projectMemory = await _memoryRepository.GetProjectMemoryAsync(userId, projectId, ct)
                            .ConfigureAwait(false);
                        authorMemory = await _memoryRepository.GetAuthorMemoryAsync(userId, ct)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[CreativeKnowledgeBaseService] 加载记忆失败，跳过增强: {ex.Message}");
                    }
                }

                var entryMap = _builtInEntries.ToDictionary(e => e.Id, e => e, StringComparer.OrdinalIgnoreCase);
                var scoredResults = searchResults
                    .Where(r => !string.IsNullOrWhiteSpace(r.SourceId) && entryMap.ContainsKey(r.SourceId))
                    .Select(r => new
                    {
                        Result = r,
                        Entry = entryMap[r.SourceId!],
                        Score = CalculateScore(r, entryMap[r.SourceId!], constitution, projectMemory, authorMemory)
                    })
                    .OrderByDescending(x => x.Score)
                    .ToList();

                var usedPatterns = NormalizeUsedPatterns(usedPlotPatterns);
                var hits = scoredResults
                    .Where(x => x.Entry.Category != CreativeKnowledgeCategory.TropePattern ||
                                !usedPatterns.Any(p => HasTokenOverlap(x.Entry.Content, p)))
                    .Take(Math.Clamp(topK, 1, MaxHits))
                    .Select(x => BuildVectorResult(x.Entry, x.Score, x.Result))
                    .ToList();

                return BuildResult(normalizedQuery, hits, hits.Count == 0
                    ? "创意知识库暂无命中，已返回空结果。"
                    : $"创意知识库命中 {hits.Count} 条（向量检索）。");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[CreativeKnowledgeBaseService] 向量检索失败，改用内核内置知识检索: {ex.Message}");
                return BuildTokenMatchingResult(normalizedQuery, constitution, usedPlotPatterns, topK);
            }
        }

        private CreativeKnowledgeRetrievalResult BuildTokenMatchingResult(
            string normalizedQuery,
            StoryCreativeConstitution? constitution,
            IEnumerable<string>? usedPlotPatterns,
            int topK)
        {
            var queryTokens = Tokenize(normalizedQuery);
            var usedPatterns = NormalizeUsedPatterns(usedPlotPatterns);
            var hits = _builtInEntries
                .Select(entry => ScoreEntry(entry, queryTokens, constitution, usedPatterns))
                .Where(hit => hit.Score > 0)
                .OrderByDescending(hit => hit.Score)
                .Take(Math.Clamp(topK, 1, MaxHits))
                .ToList();

            return BuildResult(normalizedQuery, hits, hits.Count == 0
                ? "创意知识库暂无命中，已返回空结果。"
                : $"创意知识库命中 {hits.Count} 条。");
        }

        private static CreativeKnowledgeRetrievalResult BuildResult(
            string normalizedQuery,
            List<CreativeKnowledgeHit> hits,
            string message)
        {
            var result = new CreativeKnowledgeRetrievalResult
            {
                Success = true,
                Query = normalizedQuery,
                Message = message
            };

            result.Hits.AddRange(hits);
            result.GenrePrinciples.AddRange(ProjectContents(hits, CreativeKnowledgeCategory.GenrePrinciple, 4));
            result.TropeWarnings.AddRange(ProjectContents(hits, CreativeKnowledgeCategory.TropePattern, 4));
            result.AntiTropeStrategies.AddRange(ProjectContents(hits, CreativeKnowledgeCategory.AntiTropeStrategy, 5));
            result.EmotionRelationshipGuides.AddRange(ProjectContents(hits, CreativeKnowledgeCategory.EmotionArc, 4));
            result.EmotionRelationshipGuides.AddRange(ProjectContents(hits, CreativeKnowledgeCategory.RelationshipDynamic, 4));
            result.ProjectMemory.AddRange(ProjectContents(hits, CreativeKnowledgeCategory.ProjectUsedPattern, 5));
            result.HardFacts.AddRange(ProjectContents(hits, CreativeKnowledgeCategory.HardFact, 8));
            return result;
        }

        private static List<string> NormalizeUsedPatterns(IEnumerable<string>? usedPlotPatterns) =>
            (usedPlotPatterns ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        private double CalculateScore(
            SearchResult vectorResult,
            CreativeKnowledgeEntry entry,
            StoryCreativeConstitution? constitution,
            ProjectMemory? projectMemory,
            AuthorMemory? authorMemory)
        {
            var score = (double)vectorResult.Score;

            if (constitution != null)
            {
                if (!string.IsNullOrWhiteSpace(constitution.Genre)
                    && Matches(entry, constitution.Genre))
                {
                    score += 3.0;
                }

                if (!string.IsNullOrWhiteSpace(constitution.SubGenre)
                    && Matches(entry, constitution.SubGenre))
                {
                    score += 2.0;
                }

                if (entry.Category == CreativeKnowledgeCategory.ThemeDepth
                    && (constitution.GenreProfile?.DepthStrength ?? 0) >= 7)
                {
                    score += 2.0;
                }

                if (entry.Category is CreativeKnowledgeCategory.EmotionArc or CreativeKnowledgeCategory.RelationshipDynamic
                    && (constitution.GenreProfile?.EmotionStrength ?? 0) >= 7)
                {
                    score += 2.0;
                }
            }

            score += Math.Clamp(entry.Weight, 1, 10) * 0.3;
            return Math.Round(score, 2);
        }

        private CreativeKnowledgeHit BuildVectorResult(
            CreativeKnowledgeEntry entry,
            double score,
            SearchResult vectorResult)
        {
            return new CreativeKnowledgeHit
            {
                Entry = Clone(entry),
                Score = score,
                Reason = $"向量相似度 {vectorResult.Score:F2}，最终得分 {score:F2}"
            };
        }

        private static CreativeKnowledgeHit ScoreEntry(
            CreativeKnowledgeEntry entry,
            IReadOnlyCollection<string> queryTokens,
            StoryCreativeConstitution? constitution,
            IReadOnlyCollection<string> usedPatterns)
        {
            var entryText = $"{entry.Genre} {entry.SubGenre} {entry.Title} {entry.Content} {string.Join(" ", entry.Tags)}";
            var entryTokens = Tokenize(entryText);
            var overlap = entryTokens.Intersect(queryTokens, StringComparer.OrdinalIgnoreCase).Count();
            var score = overlap * 2.0 + Math.Clamp(entry.Weight, 1, 10) * 0.4;
            var reasons = new List<string>();

            if (overlap > 0)
                reasons.Add($"关键词重合 {overlap}");

            if (constitution != null)
            {
                if (!string.IsNullOrWhiteSpace(constitution.Genre)
                    && Matches(entry, constitution.Genre))
                {
                    score += 3;
                    reasons.Add("匹配题材");
                }

                if (!string.IsNullOrWhiteSpace(constitution.SubGenre)
                    && Matches(entry, constitution.SubGenre))
                {
                    score += 2;
                    reasons.Add("匹配子类型");
                }

                if (entry.Category == CreativeKnowledgeCategory.ThemeDepth
                    && (constitution.GenreProfile?.DepthStrength ?? 0) >= 7)
                {
                    score += 2;
                    reasons.Add("匹配主题深度需求");
                }

                if (entry.Category is CreativeKnowledgeCategory.EmotionArc or CreativeKnowledgeCategory.RelationshipDynamic
                    && (constitution.GenreProfile?.EmotionStrength ?? 0) >= 7)
                {
                    score += 2;
                    reasons.Add("匹配情绪线需求");
                }
            }

            if (entry.Category == CreativeKnowledgeCategory.TropePattern
                && usedPatterns.Any(p => HasTokenOverlap(entryText, p)))
            {
                score += 4;
                reasons.Add("命中项目已用桥段风险");
            }

            return new CreativeKnowledgeHit
            {
                Entry = Clone(entry),
                Score = Math.Round(score, 2),
                Reason = reasons.Count == 0 ? "基础权重命中" : string.Join("；", reasons)
            };
        }

        private static bool Matches(CreativeKnowledgeEntry entry, string text)
        {
            return HasTokenOverlap(entry.Genre, text)
                || HasTokenOverlap(entry.SubGenre, text)
                || HasTokenOverlap(entry.Title, text)
                || entry.Tags.Any(tag => HasTokenOverlap(tag, text));
        }

        private static IEnumerable<string> ProjectContents(
            IEnumerable<CreativeKnowledgeHit> hits,
            CreativeKnowledgeCategory category,
            int take)
        {
            return hits
                .Where(h => h.Entry.Category == category)
                .OrderByDescending(h => h.Score)
                .Take(take)
                .Select(h => $"{h.Entry.Title}：{h.Entry.Content}");
        }

        private static string BuildQuery(
            string query,
            StoryCreativeConstitution? constitution,
            IEnumerable<string>? usedPlotPatterns)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(query)) parts.Add(query.Trim());
            if (!string.IsNullOrWhiteSpace(constitution?.Genre)) parts.Add(constitution.Genre);
            if (!string.IsNullOrWhiteSpace(constitution?.SubGenre)) parts.Add(constitution.SubGenre);
            if (!string.IsNullOrWhiteSpace(constitution?.ReaderPromise)) parts.Add(constitution.ReaderPromise);
            if (!string.IsNullOrWhiteSpace(constitution?.CoreHook)) parts.Add(constitution.CoreHook);
            parts.AddRange((usedPlotPatterns ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Take(8));
            return string.Join("；", parts.Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private static List<CreativeKnowledgeEntry> BuildSeedEntries()
        {
            return new List<CreativeKnowledgeEntry>
            {
                Seed("genre-xuanhuan-cost", CreativeKnowledgeCategory.GenrePrinciple, "玄幻", "爽文",
                    "爽文必须有代价回流",
                    "升级、打脸、夺宝之后必须留下敌人升级、资源债务、身份暴露或规则反噬，否则爽点会变成无成本流水账。",
                    new[] { "玄幻", "爽文", "代价", "规则反噬" }, 9),
                Seed("genre-suspense-evidence", CreativeKnowledgeCategory.GenrePrinciple, "悬疑", "烧脑",
                    "反转必须提前有证据",
                    "烧脑章节可以误导读者，但不能靠临时新设定解释一切；至少提前放入一个可回看证据和一个合理误读路径。",
                    new[] { "悬疑", "烧脑", "反转", "伏笔" }, 10),
                Seed("genre-ensemble-position", CreativeKnowledgeCategory.GenrePrinciple, "群像", "",
                    "群像推进依靠立场变化",
                    "群像章节的有效推进不是增加人物出场，而是让至少一个人物的利益、关系或阵营立场发生可追踪变化。",
                    new[] { "群像", "势力", "关系", "立场" }, 8),
                Seed("trope-sudden-rescue", CreativeKnowledgeCategory.TropePattern, "", "",
                    "关键人物刚好路过救场",
                    "当主角困局由外部强者突然解决，读者会觉得前文压力失效。除非救场者早有行动线索，否则应改成主角用已有信息自救。",
                    new[] { "救场", "巧合", "降智" }, 9),
                Seed("trope-face-slapping", CreativeKnowledgeCategory.TropePattern, "", "爽文",
                    "重复打脸循环",
                    "嘲讽、证明、全场震惊如果连续出现，会让爽点疲劳。需要改变冲突功能：一次改资源，一次改关系，一次改规则认知。",
                    new[] { "打脸", "全场震惊", "爽文", "套路" }, 8),
                Seed("anti-cost", CreativeKnowledgeCategory.AntiTropeStrategy, "", "",
                    "用代价替代巧合",
                    "遇到机械反转或突然救场时，把解决方案改成角色主动付出代价：暴露秘密、牺牲资源、破坏关系或签下未来债务。",
                    new[] { "反套路", "代价", "主动选择" }, 10),
                Seed("anti-perspective", CreativeKnowledgeCategory.AntiTropeStrategy, "", "",
                    "改变信息来源",
                    "如果桥段和旧章节相似，不只换场景名；应改变信息来源、误判对象、反转主体和最终承担后果的人。",
                    new[] { "反套路", "信息差", "重复桥段" }, 9),
                Seed("depth-theme-choice", CreativeKnowledgeCategory.ThemeDepth, "", "",
                    "主题深度落在选择而不是旁白",
                    "不要用议论解释主题。让角色在两种都不完美的选择里暴露价值观，并让选择带来后续不可撤销的后果。",
                    new[] { "主题", "深度", "选择", "后果" }, 8),
                Seed("emotion-arc-trigger-externalize", CreativeKnowledgeCategory.EmotionArc, "", "",
                    "情绪线必须外化为行动",
                    "有效的情绪变化要有触发、压抑、外化行动和后果。不要只写角色心里难受；让难受改变一次选择、一次关系或一次风险承担。",
                    new[] { "情绪线", "心理", "外化", "行动", "后果" }, 9),
                Seed("emotion-arc-pressure-memory", CreativeKnowledgeCategory.EmotionArc, "", "",
                    "心理变化需要旧伤与当前压力共振",
                    "角色突然崩溃或转变会显得机械。应让当前事件击中旧伤、信念或秘密，再用一个具体行为证明心理状态已经变化。",
                    new[] { "旧伤", "压力", "心理变化", "信念" }, 8),
                Seed("relationship-dynamic-power", CreativeKnowledgeCategory.RelationshipDynamic, "", "",
                    "关系变化必须改变权力或信任结构",
                    "和解、背叛、暧昧、结盟都不能只停在对话层面；它必须改变信息共享、资源分配、保护义务、威胁关系中的至少一项。",
                    new[] { "关系变化", "信任", "权力", "资源", "背叛" }, 10),
                Seed("relationship-dynamic-traceable", CreativeKnowledgeCategory.RelationshipDynamic, "", "",
                    "关系弧要有可追踪状态机",
                    "关系线至少记录起点、裂痕、试探、代价、转折和新状态。每次变化都要能在角色账本中留下下一章可使用的压力。",
                    new[] { "关系弧", "状态机", "裂痕", "试探", "角色账本" }, 9),
                Seed("reader-promise-payoff", CreativeKnowledgeCategory.ReaderPromise, "", "",
                    "读者承诺需要阶段兑现",
                    "每三到五章至少兑现一次类型承诺：爽文给明确胜利，悬疑给可信线索，情绪文给关系变化，群像文给格局移动。",
                    new[] { "读者承诺", "节奏", "兑现" }, 8)
            };
        }

        private static CreativeKnowledgeEntry Seed(
            string id,
            CreativeKnowledgeCategory category,
            string genre,
            string subGenre,
            string title,
            string content,
            IEnumerable<string> tags,
            int weight)
        {
            return new CreativeKnowledgeEntry
            {
                Id = id,
                Category = category,
                Genre = genre,
                SubGenre = subGenre,
                Title = title,
                Content = content,
                Tags = tags.ToList(),
                Weight = weight,
                Source = "BuiltIn"
            };
        }

        private static List<string> Tokenize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            return text
                .Split(new[] { ' ', '\t', '\r', '\n', '，', '。', '、', '；', ';', ',', '.', '：', ':', '！', '？', '(', ')', '（', '）', '/', '|' },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => t.Length >= 2)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(80)
                .ToList();
        }

        private static bool HasTokenOverlap(string content, string expected)
        {
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(expected))
                return false;

            var normalizedExpected = expected.Trim();
            if (normalizedExpected.Length <= 8)
                return content.Contains(normalizedExpected, StringComparison.OrdinalIgnoreCase);

            var tokens = Tokenize(normalizedExpected).Take(12).ToList();
            if (tokens.Count == 0)
                return content.Contains(normalizedExpected, StringComparison.OrdinalIgnoreCase);

            var hitCount = tokens.Count(t => content.Contains(t, StringComparison.OrdinalIgnoreCase));
            return hitCount >= Math.Max(1, Math.Min(3, tokens.Count / 2));
        }

        private static CreativeKnowledgeEntry Clone(CreativeKnowledgeEntry entry)
        {
            return new CreativeKnowledgeEntry
            {
                Id = entry.Id,
                Category = entry.Category,
                Genre = entry.Genre,
                SubGenre = entry.SubGenre,
                Title = entry.Title,
                Content = entry.Content,
                Tags = entry.Tags.ToList(),
                Weight = entry.Weight,
                Source = entry.Source,
                CreatedAt = entry.CreatedAt,
                UpdatedAt = entry.UpdatedAt
            };
        }
    }
}
