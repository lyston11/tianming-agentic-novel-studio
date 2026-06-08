using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using TM.Services.Framework.AI.QueryRouting;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class DataLookupPlugin
    {
        private QueryRoutingService? _routingService;
        private QueryRoutingService RoutingService => _routingService ??= ServiceLocator.Get<QueryRoutingService>();

        private static void TryAppendReferences(string? json, string typeLabel)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            if (json.StartsWith("[未找到]", StringComparison.Ordinal)) return;
            if (json.StartsWith("[获取失败]", StringComparison.Ordinal)) return;
            if (json[0] != '[' && json[0] != '{') return;

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return;

                var items = new List<SearchResult>();
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;

                    var id = TryReadString(el, "Id");
                    if (string.IsNullOrWhiteSpace(id))
                        id = TryReadString(el, "ChapterId");

                    var display = TryReadString(el, "Display");
                    if (string.IsNullOrWhiteSpace(display))
                    {
                        var name = TryReadString(el, "Name");
                        display = string.IsNullOrWhiteSpace(id) ? name : $"{name}({id})";
                    }
                    if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(display)) continue;

                    var tail = TryReadString(el, "Brief");
                    if (string.IsNullOrWhiteSpace(tail))
                    {
                        var raw = TryReadString(el, "Content");
                        if (!string.IsNullOrWhiteSpace(raw))
                            tail = raw.Length > 60 ? raw.Substring(0, 60) + "..." : raw;
                    }
                    var head = string.IsNullOrWhiteSpace(typeLabel) ? display : $"{typeLabel}: {display}";
                    var content = string.IsNullOrWhiteSpace(tail) ? head : $"{head} · {tail}";

                    items.Add(new SearchResult
                    {
                        ChapterId = string.IsNullOrWhiteSpace(id) ? display : id,
                        Position = 0,
                        Content = content,
                        Score = 1.0
                    });
                }
                if (items.Count == 0) return;

                var chat = ServiceLocator.Get<SKChatService>();
                chat.AppendToolReferences(items);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataLookupPlugin] TryAppendReferences({typeLabel}) 失败(忽略): {ex.Message}");
            }
        }

        private static string TryReadString(JsonElement el, string propertyName)
        {
            return el.TryGetProperty(propertyName, out var p) && p.ValueKind == JsonValueKind.String
                ? (p.GetString() ?? string.Empty)
                : string.Empty;
        }

        [KernelFunction("GetProjectContext")]
        [Description("获取项目整体概况：包含大纲结构、分卷划分、章节数量、各类设计数据统计。用于回答'项目有多少章''作品概况'等问题")]
        public async Task<string> GetProjectContextAsync()
        {
            var ctx = await TM.Services.Framework.AI.SemanticKernel.ProjectContextBuilder.BuildAsync().ConfigureAwait(false);
            return ctx ?? string.Empty;
        }

        [KernelFunction("GetProtagonists")]
        [Description("从角色设计中查找标记为'主角'的角色。注意：如果用户问的是'素材库的主角'，应使用 SearchCreativeMaterials 而非本工具")]
        public async Task<string> GetProtagonistsAsync(
            [Description("返回数量上限，默认5")] int topK = 5)
        {
            try
            {
                var guide = ServiceLocator.Get<IGuideContextService>();
                var characters = await guide.GetAllCharactersAsync().ConfigureAwait(false);

                var protagonists = characters
                    .Where(c => string.Equals(c.CharacterType?.Trim(), "主角", StringComparison.OrdinalIgnoreCase)
                                || (c.CharacterType?.Contains("主角", StringComparison.OrdinalIgnoreCase) == true))
                    .OrderByDescending(c => c.GetImportanceWeight())
                    .Take(Math.Max(1, topK))
                    .ToList();

                if (protagonists.Count == 0)
                    return "[未找到] 当前作品未标注主角（角色设计中 CharacterType=主角）";

                var sb = new StringBuilder();
                sb.AppendLine($"主角（最多返回{topK}）：");
                foreach (var p in protagonists)
                {
                    var identity = string.IsNullOrWhiteSpace(p.Identity) ? "" : $" / 身份：{p.Identity}";
                    sb.AppendLine($"- {p.Name} ({p.Id}){identity}");
                }

                var refs = protagonists.Select(p => new SearchResult
                {
                    ChapterId = p.Id ?? string.Empty,
                    Position = 0,
                    Content = string.IsNullOrWhiteSpace(p.Identity)
                        ? $"主角: {p.Name}({p.Id})"
                        : $"主角: {p.Name}({p.Id}) · {p.Identity}",
                    Score = 1.0
                }).ToList();
                ServiceLocator.Get<SKChatService>().AppendToolReferences(refs);

                return sb.ToString().Trim();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataLookupPlugin] GetProtagonists失败: {ex.Message}");
                return $"[获取失败] {ex.Message}";
            }
        }

        [KernelFunction("GetGeneratedChaptersSummary")]
        [Description("获取已生成章节的数量与总字数统计。用于回答'写了多少字''生成了几章'等问题")]
        public async Task<string> GetGeneratedChaptersSummaryAsync()
        {
            try
            {
                var contentService = ServiceLocator.Get<GeneratedContentService>();
                var chapters = await contentService.GetGeneratedChaptersAsync().ConfigureAwait(false);
                var totalWords = chapters.Sum(c => c.WordCount);
                return $"已生成章节数：{chapters.Count} 章\n总字数：{totalWords:N0} 字";
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataLookupPlugin] GetGeneratedChaptersSummary失败: {ex.Message}");
                return $"[获取失败] {ex.Message}";
            }
        }

        [KernelFunction("ListGeneratedChapters")]
        [Description("分页列出已生成的章节（ID/标题/字数）。用于查看哪些章节已经有正文内容")]
        public async Task<string> ListGeneratedChaptersAsync(
            [Description("跳过数量，默认0")] int skip = 0,
            [Description("返回数量上限，默认20")] int take = 20)
        {
            try
            {
                var contentService = ServiceLocator.Get<GeneratedContentService>();
                var chapters = await contentService.GetGeneratedChaptersAsync().ConfigureAwait(false);

                var page = chapters
                    .Skip(Math.Max(0, skip))
                    .Take(Math.Max(1, take))
                    .ToList();

                var sb = new StringBuilder();
                sb.AppendLine($"已生成章节：总计 {chapters.Count} 章，本次返回 {page.Count} 章（skip={skip}, take={take}）");
                foreach (var ch in page)
                {
                    var wc = ch.WordCount > 0 ? $"{ch.WordCount:N0}字" : "字数未知";
                    sb.AppendLine($"- [{ch.Id}] {ch.Title} / {wc}");
                }
                return sb.ToString().Trim();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataLookupPlugin] ListGeneratedChapters失败: {ex.Message}");
                return $"[获取失败] {ex.Message}";
            }
        }

        [KernelFunction("ListUngeneratedChapters")]
        [Description("分页列出尚未生成正文的章节。用于查看哪些章节还没有写")]
        public async Task<string> ListUngeneratedChaptersAsync(
            [Description("跳过数量，默认0")] int skip = 0,
            [Description("返回数量上限，默认20")] int take = 20)
        {
            try
            {
                var contentService = ServiceLocator.Get<GeneratedContentService>();
                var chapters = await contentService.GetGeneratedChaptersAsync().ConfigureAwait(false);
                var generatedIds = chapters.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

                var guide = ServiceLocator.Get<IGuideContextService>();
                var contentGuide = await guide.GetContentGuideAsync().ConfigureAwait(false);
                if (contentGuide?.Chapters == null || contentGuide.Chapters.Count == 0)
                    return "[未找到] 内容大纲（ContentGuide）为空";

                var ungenerated = contentGuide.Chapters.Keys
                    .Where(id => !generatedIds.Contains(id))
                    .OrderBy(id => id)
                    .ToList();

                var page = ungenerated
                    .Skip(Math.Max(0, skip))
                    .Take(Math.Max(1, take))
                    .ToList();

                var sb = new StringBuilder();
                sb.AppendLine($"未生成章节：总计 {ungenerated.Count} 章，本次返回 {page.Count} 章（skip={skip}, take={take}）");
                foreach (var id in page)
                {
                    if (contentGuide.Chapters.TryGetValue(id, out var entry))
                        sb.AppendLine($"- [{id}] {entry.Title}");
                    else
                        sb.AppendLine($"- [{id}]");
                }
                return sb.ToString().Trim();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataLookupPlugin] ListUngeneratedChapters失败: {ex.Message}");
                return $"[获取失败] {ex.Message}";
            }
        }

        [KernelFunction("SearchCreativeMaterials")]
        [Description("搜索创作素材库中的参考资料（文风、风格、灵感、拆书分析、主角塑造、剧情结构等）。用于回答'素材库有什么''素材库的主角是谁'等问题")]
        public async Task<string> SearchCreativeMaterialsAsync(
            [Description("搜索关键词，留空列出全部")] string query = "",
            [Description("返回数量上限，默认10")] int topK = 10)
        {
            try
            {
                var guide = ServiceLocator.Get<IGuideContextService>();
                var materials = await guide.GetAllTemplatesAsync().ConfigureAwait(false);

                var filtered = materials
                    .Where(m => m.IsEnabled)
                    .ToList();

                if (!string.IsNullOrWhiteSpace(query))
                {
                    filtered = filtered.Where(m =>
                        m.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || m.Genre.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || m.OverallIdea.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || m.WorldBuildingMethod.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || m.ProtagonistDesign.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || m.PlotStructure.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                if (filtered.Count == 0)
                    return string.IsNullOrWhiteSpace(query)
                        ? "[未找到] 当前作品暂无创作素材"
                        : $"[未找到] 没有匹配 \"{query}\" 的创作素材";

                var page = filtered.Take(Math.Max(1, topK)).ToList();
                var sb = new StringBuilder();
                sb.AppendLine($"创作素材（共 {filtered.Count} 条，返回 {page.Count} 条）：");
                foreach (var m in page)
                {
                    sb.AppendLine($"- 【{m.Name}】({m.Id}) 流派:{m.Genre}");
                    if (!string.IsNullOrWhiteSpace(m.OverallIdea))
                        sb.AppendLine($"  核心创意：{(m.OverallIdea.Length > 80 ? m.OverallIdea[..80] + "..." : m.OverallIdea)}");
                    if (!string.IsNullOrWhiteSpace(m.WorldBuildingMethod))
                        sb.AppendLine($"  世界观构建：{(m.WorldBuildingMethod.Length > 60 ? m.WorldBuildingMethod[..60] + "..." : m.WorldBuildingMethod)}");
                    if (!string.IsNullOrWhiteSpace(m.ProtagonistDesign))
                        sb.AppendLine($"  主角塑造：{(m.ProtagonistDesign.Length > 60 ? m.ProtagonistDesign[..60] + "..." : m.ProtagonistDesign)}");
                    if (!string.IsNullOrWhiteSpace(m.PlotStructure))
                        sb.AppendLine($"  剧情结构：{(m.PlotStructure.Length > 60 ? m.PlotStructure[..60] + "..." : m.PlotStructure)}");
                }

                var refs = page.Select(m => new SearchResult
                {
                    ChapterId = m.Id ?? string.Empty,
                    Position = 0,
                    Content = string.IsNullOrWhiteSpace(m.Genre)
                        ? $"创作素材: {m.Name}({m.Id})"
                        : $"创作素材: {m.Name}({m.Id}) · {m.Genre}",
                    Score = 1.0
                }).ToList();
                ServiceLocator.Get<SKChatService>().AppendToolReferences(refs);

                return sb.ToString().Trim();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataLookupPlugin] SearchCreativeMaterials失败: {ex.Message}");
                return $"[获取失败] {ex.Message}";
            }
        }

        [KernelFunction("GetCreativeMaterialById")]
        [Description("根据素材ID获取创作素材的完整详情（核心创意、世界观构建、主角塑造、剧情结构等全部字段）")]
        public async Task<string> GetCreativeMaterialByIdAsync(
            [Description("创作素材ID")] string materialId)
        {
            try
            {
                var guide = ServiceLocator.Get<IGuideContextService>();
                var results = await guide.ExtractTemplatesAsync(new System.Collections.Generic.List<string> { materialId }).ConfigureAwait(false);
                var m = results.FirstOrDefault();
                if (m == null) return $"[未找到] 素材ID: {materialId}";

                var sb = new StringBuilder();
                sb.AppendLine($"【{m.Name}】({m.Id})");
                sb.AppendLine($"流派：{m.Genre}");
                if (!string.IsNullOrWhiteSpace(m.OverallIdea)) sb.AppendLine($"核心创意：{m.OverallIdea}");
                if (!string.IsNullOrWhiteSpace(m.WorldBuildingMethod)) sb.AppendLine($"世界观构建手法：{m.WorldBuildingMethod}");
                if (!string.IsNullOrWhiteSpace(m.PowerSystemDesign)) sb.AppendLine($"力量体系：{m.PowerSystemDesign}");
                if (!string.IsNullOrWhiteSpace(m.EnvironmentDescription)) sb.AppendLine($"环境描述：{m.EnvironmentDescription}");
                if (!string.IsNullOrWhiteSpace(m.FactionDesign)) sb.AppendLine($"势力设计：{m.FactionDesign}");
                if (!string.IsNullOrWhiteSpace(m.WorldviewHighlights)) sb.AppendLine($"世界观亮点：{m.WorldviewHighlights}");
                if (!string.IsNullOrWhiteSpace(m.ProtagonistDesign)) sb.AppendLine($"主角塑造：{m.ProtagonistDesign}");
                if (!string.IsNullOrWhiteSpace(m.SupportingRoles)) sb.AppendLine($"配角设计：{m.SupportingRoles}");
                if (!string.IsNullOrWhiteSpace(m.CharacterRelations)) sb.AppendLine($"人物关系：{m.CharacterRelations}");
                if (!string.IsNullOrWhiteSpace(m.GoldenFingerDesign)) sb.AppendLine($"金手指设计：{m.GoldenFingerDesign}");
                if (!string.IsNullOrWhiteSpace(m.CharacterHighlights)) sb.AppendLine($"角色亮点：{m.CharacterHighlights}");
                if (!string.IsNullOrWhiteSpace(m.PlotStructure)) sb.AppendLine($"情节结构：{m.PlotStructure}");
                if (!string.IsNullOrWhiteSpace(m.ConflictDesign)) sb.AppendLine($"冲突设计：{m.ConflictDesign}");
                if (!string.IsNullOrWhiteSpace(m.ClimaxArrangement)) sb.AppendLine($"高潮安排：{m.ClimaxArrangement}");
                if (!string.IsNullOrWhiteSpace(m.ForeshadowingTechnique)) sb.AppendLine($"伏笔手法：{m.ForeshadowingTechnique}");
                if (!string.IsNullOrWhiteSpace(m.PlotHighlights)) sb.AppendLine($"剧情亮点：{m.PlotHighlights}");
                return sb.ToString().Trim();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataLookupPlugin] GetCreativeMaterialById失败: {ex.Message}");
                return $"[获取失败] {ex.Message}";
            }
        }

        [KernelFunction("GetCharacterById")]
        [Description("根据角色ID获取角色设计的完整详情（性格、身份、能力、背景等全部字段）")]
        public Task<string> GetCharacterByIdAsync(
            [Description("角色ID")] string characterId)
        {
            return RoutingService.GetCharacterByIdAsync(characterId);
        }

        [KernelFunction("GetCharactersByIds")]
        [Description("批量获取多个角色的完整信息")]
        public Task<string> GetCharactersByIdsAsync(
            [Description("角色ID列表，逗号分隔")] string characterIds)
        {
            return RoutingService.GetCharactersByIdsAsync(characterIds);
        }

        [KernelFunction("GetLocationById")]
        [Description("根据地点ID获取完整信息")]
        public Task<string> GetLocationByIdAsync(
            [Description("地点ID")] string locationId)
        {
            return RoutingService.GetLocationByIdAsync(locationId);
        }

        [KernelFunction("GetFactionById")]
        [Description("根据势力ID获取完整信息")]
        public Task<string> GetFactionByIdAsync(
            [Description("势力ID")] string factionId)
        {
            return RoutingService.GetFactionByIdAsync(factionId);
        }

        [KernelFunction("GetPlotRuleById")]
        [Description("根据剧情规则ID获取完整信息")]
        public Task<string> GetPlotRuleByIdAsync(
            [Description("剧情规则ID")] string plotRuleId)
        {
            return RoutingService.GetPlotRuleByIdAsync(plotRuleId);
        }

        [KernelFunction("GetWorldRuleById")]
        [Description("根据世界观规则ID获取完整信息")]
        public Task<string> GetWorldRuleByIdAsync(
            [Description("世界观规则ID")] string worldRuleId)
        {
            return RoutingService.GetWorldRuleByIdAsync(worldRuleId);
        }

        [KernelFunction("GetExpandedChapterContext")]
        [Description("获取章节展开上下文（含角色/地点/规则详情）")]
        public Task<string> GetExpandedChapterContextAsync(
            [Description("章节ID")] string chapterId)
        {
            return RoutingService.GetExpandedChapterContextAsync(chapterId);
        }

        [KernelFunction("GetChapterContext")]
        [Description("获取章节轻量上下文（仅索引）")]
        public Task<string> GetChapterContextAsync(
            [Description("章节ID")] string chapterId)
        {
            return RoutingService.GetChapterContextAsync(chapterId);
        }

        [KernelFunction("GetLocationsByIds")]
        [Description("批量获取多个地点的完整信息")]
        public Task<string> GetLocationsByIdsAsync(
            [Description("地点ID列表，逗号分隔")] string locationIds)
        {
            return RoutingService.GetLocationsByIdsAsync(locationIds);
        }

        [KernelFunction("GetFactionsByIds")]
        [Description("批量获取多个势力的完整信息")]
        public Task<string> GetFactionsByIdsAsync(
            [Description("势力ID列表，逗号分隔")] string factionIds)
        {
            return RoutingService.GetFactionsByIdsAsync(factionIds);
        }

        [KernelFunction("GetPlotRulesByIds")]
        [Description("批量获取多个剧情规则的完整信息")]
        public Task<string> GetPlotRulesByIdsAsync(
            [Description("剧情规则ID列表，逗号分隔")] string plotRuleIds)
        {
            return RoutingService.GetPlotRulesByIdsAsync(plotRuleIds);
        }

        [KernelFunction("GetWorldRulesByIds")]
        [Description("批量获取多个世界观规则的完整信息")]
        public Task<string> GetWorldRulesByIdsAsync(
            [Description("世界观规则ID列表，逗号分隔")] string worldRuleIds)
        {
            return RoutingService.GetWorldRulesByIdsAsync(worldRuleIds);
        }

        [KernelFunction("ListAvailableIds")]
        [Description("列出某类别所有可用ID")]
        public Task<string> ListAvailableIdsAsync(
            [Description("类别名")] string category)
        {
            return RoutingService.ListAvailableIdsAsync(category);
        }

        [KernelFunction("ValidateDataConsistency")]
        [Description("校验打包数据一致性")]
        public Task<string> ValidateDataConsistencyAsync()
        {
            return RoutingService.ValidateDataConsistencyAsync();
        }

        [KernelFunction("SearchCharacters")]
        [Description("搜索角色设计列表。用于查找'有哪些角色''某个角色的信息'等。query传角色名或特征词")]
        public async Task<string> SearchCharactersAsync(
            [Description("搜索关键词")] string query,
            [Description("返回结果数量上限，默认5")] int topK = 5)
        {
            var json = await RoutingService.SearchCharactersAsync(query, topK).ConfigureAwait(false);
            TryAppendReferences(json, "角色");
            return json;
        }

        [KernelFunction("SearchLocations")]
        [Description("搜索地点设计列表。query传地点名或场景特征词")]
        public async Task<string> SearchLocationsAsync(
            [Description("搜索关键词")] string query,
            [Description("返回结果数量上限，默认5")] int topK = 5)
        {
            var json = await RoutingService.SearchLocationsAsync(query, topK).ConfigureAwait(false);
            TryAppendReferences(json, "地点");
            return json;
        }

        [KernelFunction("SearchFactions")]
        [Description("搜索势力/门派/阵营/组织设计列表。query传势力名或组织特征词")]
        public async Task<string> SearchFactionsAsync(
            [Description("搜索关键词")] string query,
            [Description("返回结果数量上限，默认5")] int topK = 5)
        {
            var json = await RoutingService.SearchFactionsAsync(query, topK).ConfigureAwait(false);
            TryAppendReferences(json, "势力");
            return json;
        }

        [KernelFunction("SearchWorldRules")]
        [Description("搜索世界观规则（修炼体系、能力设定、世界法则等）。query传设定名或概念词")]
        public async Task<string> SearchWorldRulesAsync(
            [Description("搜索关键词")] string query,
            [Description("返回结果数量上限，默认5")] int topK = 5)
        {
            var json = await RoutingService.SearchWorldRulesAsync(query, topK).ConfigureAwait(false);
            TryAppendReferences(json, "世界观规则");
            return json;
        }

        [KernelFunction("SearchPlotRules")]
        [Description("搜索剧情规则（伏笔、冲突、转折、悬念等情节约束）。query传情节关键词")]
        public async Task<string> SearchPlotRulesAsync(
            [Description("搜索关键词")] string query,
            [Description("返回结果数量上限，默认5")] int topK = 5)
        {
            var json = await RoutingService.SearchPlotRulesAsync(query, topK).ConfigureAwait(false);
            TryAppendReferences(json, "剧情规则");
            return json;
        }

        [KernelFunction("GetCreativeMaterialsByIds")]
        [Description("批量获取多个创作素材的完整信息")]
        public Task<string> GetCreativeMaterialsByIdsAsync(
            [Description("素材ID列表，逗号分隔")] string materialIds)
        {
            return RoutingService.GetCreativeMaterialsByIdsAsync(materialIds);
        }

        [KernelFunction("GetOutlinesByIds")]
        [Description("批量获取多个大纲的完整信息")]
        public Task<string> GetOutlinesByIdsAsync(
            [Description("大纲ID列表，逗号分隔")] string outlineIds)
        {
            return RoutingService.GetOutlinesByIdsAsync(outlineIds);
        }

        [KernelFunction("GetVolumeDesignsByIds")]
        [Description("批量获取多个分卷设计的完整信息")]
        public Task<string> GetVolumeDesignsByIdsAsync(
            [Description("分卷ID列表，逗号分隔")] string volumeIds)
        {
            return RoutingService.GetVolumeDesignsByIdsAsync(volumeIds);
        }

        [KernelFunction("GetChapterPlansByIds")]
        [Description("批量获取多个章节规划的完整信息")]
        public Task<string> GetChapterPlansByIdsAsync(
            [Description("章节规划ID列表，逗号分隔")] string chapterIds)
        {
            return RoutingService.GetChapterPlansByIdsAsync(chapterIds);
        }

        [KernelFunction("GetBlueprintsByIds")]
        [Description("批量获取多个章节蓝图的完整信息")]
        public Task<string> GetBlueprintsByIdsAsync(
            [Description("蓝图ID列表，逗号分隔")] string blueprintIds)
        {
            return RoutingService.GetBlueprintsByIdsAsync(blueprintIds);
        }

        [KernelFunction("GetOutlineById")]
        [Description("根据大纲ID获取完整信息")]
        public Task<string> GetOutlineByIdAsync(
            [Description("大纲ID")] string outlineId)
        {
            return RoutingService.GetOutlineByIdAsync(outlineId);
        }

        [KernelFunction("SearchOutlines")]
        [Description("搜索大纲设计。query传大纲名或主题词")]
        public async Task<string> SearchOutlinesAsync(
            [Description("搜索关键词")] string query,
            [Description("返回结果数量上限，默认5")] int topK = 5)
        {
            var json = await RoutingService.SearchOutlinesAsync(query, topK).ConfigureAwait(false);
            TryAppendReferences(json, "大纲");
            return json;
        }

        [KernelFunction("GetVolumeDesignById")]
        [Description("根据分卷设计ID获取完整信息")]
        public Task<string> GetVolumeDesignByIdAsync(
            [Description("分卷ID")] string volumeId)
        {
            return RoutingService.GetVolumeDesignByIdAsync(volumeId);
        }

        [KernelFunction("SearchVolumeDesigns")]
        [Description("搜索分卷设计。query传卷名或分卷主题词")]
        public async Task<string> SearchVolumeDesignsAsync(
            [Description("搜索关键词")] string query,
            [Description("返回结果数量上限，默认5")] int topK = 5)
        {
            var json = await RoutingService.SearchVolumeDesignsAsync(query, topK).ConfigureAwait(false);
            TryAppendReferences(json, "分卷");
            return json;
        }

        [KernelFunction("GetChapterPlanById")]
        [Description("根据章节规划ID获取完整信息")]
        public Task<string> GetChapterPlanByIdAsync(
            [Description("章节规划ID")] string chapterId)
        {
            return RoutingService.GetChapterPlanByIdAsync(chapterId);
        }

        [KernelFunction("SearchChapterPlans")]
        [Description("搜索章节规划。query传章节名或内容关键词")]
        public async Task<string> SearchChapterPlansAsync(
            [Description("搜索关键词")] string query,
            [Description("返回结果数量上限，默认10")] int topK = 10)
        {
            var json = await RoutingService.SearchChapterPlansAsync(query, topK).ConfigureAwait(false);
            TryAppendReferences(json, "章节规划");
            return json;
        }

        [KernelFunction("GetBlueprintById")]
        [Description("根据章节蓝图ID获取完整信息")]
        public Task<string> GetBlueprintByIdAsync(
            [Description("蓝图ID")] string blueprintId)
        {
            return RoutingService.GetBlueprintByIdAsync(blueprintId);
        }

        [KernelFunction("SearchBlueprints")]
        [Description("搜索章节蓝图（章节的详细写作方案）。query传章节名或蓝图关键词")]
        public async Task<string> SearchBlueprintsAsync(
            [Description("搜索关键词")] string query,
            [Description("返回结果数量上限，默认10")] int topK = 10)
        {
            var json = await RoutingService.SearchBlueprintsAsync(query, topK).ConfigureAwait(false);
            TryAppendReferences(json, "章节蓝图");
            return json;
        }

        [KernelFunction("SearchContent")]
        [Description("在已生成的正文中语义搜索相关段落。用于查找'正文中提到某角色的地方'等")]
        public async Task<string> SearchContentAsync(
            [Description("搜索关键词")] string query,
            [Description("返回结果数量，默认5")] int topK = 5)
        {
            var json = await RoutingService.SearchContentAsync(query, topK).ConfigureAwait(false);
            TryAppendReferences(json, "章节片段");
            return json;
        }

        [KernelFunction("FindRelatedChapters")]
        [Description("根据描述内容查找相关章节。用于'哪些章节涉及某个事件'等")]
        public Task<string> FindRelatedChaptersAsync(
            [Description("描述内容")] string description)
        {
            return RoutingService.FindRelatedChaptersAsync(description);
        }

        [KernelFunction("SmartSearch")]
        [Description("智能搜索：不确定数据属于哪个业务域时使用，自动路由到最合适的数据源")]
        public Task<string> SmartSearchAsync(
            [Description("查询内容")] string query)
        {
            return RoutingService.SmartSearchAsync(query);
        }
    }
}
