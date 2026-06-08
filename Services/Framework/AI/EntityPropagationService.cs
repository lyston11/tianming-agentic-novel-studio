using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TM.Modules.Design.Elements.CharacterRules.Services;
using TM.Modules.Design.Elements.FactionRules.Services;
using TM.Modules.Design.Elements.LocationRules.Services;
using TM.Modules.Design.Elements.PlotRules.Services;
using TM.Modules.Design.GlobalSettings.WorldRules.Services;
using TM.Modules.Design.Templates.CreativeMaterials.Services;
using TM.Modules.Generate.GlobalSettings.Outline.Services;
using TM.Modules.Generate.Elements.VolumeDesign.Services;
using TM.Modules.Generate.Elements.Chapter.Services;
using TM.Modules.Generate.Elements.Blueprint.Services;
using TM.Services.Modules.ProjectData.Models.Common;
using TM.Services.Modules.ProjectData.Metadata;

namespace TM.Services.Framework.AI
{
    public class EntityPropagationService
    {
        private static readonly Dictionary<string, Type> EntityServiceMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["characters"] = typeof(CharacterRulesService),
            ["locations"] = typeof(LocationRulesService),
            ["factions"] = typeof(FactionRulesService),
            ["plotrules"] = typeof(PlotRulesService),
            ["worldrules"] = typeof(WorldRulesService),
            ["templates"] = typeof(CreativeMaterialsService),
            ["outline"] = typeof(OutlineService),
            ["volumedesign"] = typeof(VolumeDesignService),
            ["chapter"] = typeof(ChapterService),
            ["blueprint"] = typeof(BlueprintService),
        };

        public async Task<string> PropagateRenameAsync(string entityId, string entityType, string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
                return "[跳过] oldName 或 newName 为空";

            var changes = new StringBuilder();
            int count = 0;

            foreach (var typeKv in EntityServiceMap)
            {
                var service = GetService(typeKv.Key);
                if (service == null) continue;

                var getAllMethod = service.GetType().GetMethod("GetAllData");
                if (getAllMethod?.Invoke(service, null) is not IList allData) continue;

                foreach (var item in allData)
                {
                    if (item is not BusinessDataBase bdb) continue;
                    bool modified = false;

                    foreach (var prop in item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (prop.PropertyType != typeof(string) || !prop.CanWrite) continue;
                        var val = prop.GetValue(item) as string;
                        if (string.IsNullOrEmpty(val) || !val.Contains(oldName, StringComparison.Ordinal)) continue;

                        var newVal = val.Replace(oldName, newName, StringComparison.Ordinal);
                        prop.SetValue(item, newVal);
                        modified = true;
                        var typeDisp = EntityFieldMeta.GetEntityTypeDisplayName(typeKv.Key);
                        var fieldDisp = EntityFieldMeta.GetFieldDisplayName(typeKv.Key, prop.Name);
                        changes.AppendLine($"  {typeDisp}「{bdb.Name}」{fieldDisp}: \"{oldName}\" → \"{newName}\"");
                        count++;
                    }

                    if (modified)
                    {
                        bdb.UpdatedAt = DateTime.Now;
                        var updateMethod = service.GetType().GetMethod("UpdateDataAsync");
                        if (updateMethod?.Invoke(service, new[] { item }) is Task t)
                            await t.ConfigureAwait(false);
                    }
                }
            }

            try
            {
                var guideManager = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.GuideManager>();
                var (renameCount, renameDetails) = await PersistContentGuideRenameAsync(guideManager, oldName, newName).ConfigureAwait(false);
                count += renameCount;
                if (renameDetails.Length > 0)
                    changes.Append(renameDetails);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EntityPropagationService] Guide 传播异常: {ex.Message}");
                changes.AppendLine($"  [警告] Guide 传播失败: {ex.Message}");
            }

            var affectedChapterIds = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var (chCount, chDetails) = await PersistChapterContentRenameAsync(oldName, newName, affectedChapterIds).ConfigureAwait(false);
                count += chCount;
                if (chDetails.Length > 0)
                    changes.Append(chDetails);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EntityPropagationService] 章节正文传播异常: {ex.Message}");
                changes.AppendLine($"  [警告] 章节正文传播失败: {ex.Message}");
            }

            try
            {
                var guideManager = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.GuideManager>();
                var (tgCount, tgDetails) = await PersistTrackingGuidesRenameAsync(guideManager, oldName, newName).ConfigureAwait(false);
                count += tgCount;
                if (tgDetails.Length > 0)
                    changes.Append(tgDetails);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EntityPropagationService] 追踪 Guide 传播异常: {ex.Message}");
                changes.AppendLine($"  [警告] 追踪 Guide 传播失败: {ex.Message}");
            }

            if (affectedChapterIds.Count > 0)
            {
                try
                {
                    var keywordIndex = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.KeywordChapterIndexService>();
                    foreach (var chId in affectedChapterIds)
                        await keywordIndex.RemoveChapterAsync(chId).ConfigureAwait(false);
                    changes.AppendLine($"  关键词倒排: 清理 {affectedChapterIds.Count} 章");
                    count += affectedChapterIds.Count;
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[EntityPropagationService] 关键词索引清理异常: {ex.Message}");
                    changes.AppendLine($"  [警告] 关键词索引清理失败: {ex.Message}");
                }
            }

            if (affectedChapterIds.Count > 0)
            {
                try
                {
                    var firstIdx = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.Indexing.EntityFirstChapterIndex>();
                    await firstIdx.LoadAsync().ConfigureAwait(false);
                    int totalRemoved = 0;
                    foreach (var chId in affectedChapterIds)
                        totalRemoved += await firstIdx.InvalidateByChapterAsync(chId).ConfigureAwait(false);
                    if (totalRemoved > 0)
                    {
                        changes.AppendLine($"  首次描写索引: 失效 {totalRemoved} 条（待下次对账重建）");
                        count += totalRemoved;
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[EntityPropagationService] 首次描写索引失效异常: {ex.Message}");
                    changes.AppendLine($"  [警告] 首次描写索引失效失败: {ex.Message}");
                }
            }

            await RefreshCachesAsync().ConfigureAwait(false);

            TM.App.Log($"[EntityPropagationService] PropagateRename 完成: {oldName} → {newName}，{count} 处更新，影响章节 {affectedChapterIds.Count} 个");

            if (count == 0)
                return $"重命名传播完成，未发现需要更新的引用";

            return $"重命名传播完成，更新 {count} 处:\n{changes.ToString().TrimEnd()}";
        }

        public async Task<string> PropagateDeletionAsync(string entityId, string entityType)
        {
            var changes = new StringBuilder();
            int count = 0;

            foreach (var typeKv in EntityServiceMap)
            {
                if (string.Equals(typeKv.Key, entityType, StringComparison.OrdinalIgnoreCase)) continue;

                var service = GetService(typeKv.Key);
                if (service == null) continue;

                var getAllMethod = service.GetType().GetMethod("GetAllData");
                if (getAllMethod?.Invoke(service, null) is not IList allData) continue;

                foreach (var item in allData)
                {
                    if (item is not BusinessDataBase bdb) continue;
                    bool modified = false;

                    foreach (var prop in item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (prop.PropertyType != typeof(string) || !prop.CanWrite) continue;
                        var val = prop.GetValue(item) as string;
                        if (string.IsNullOrEmpty(val) || !val.Contains(entityId, StringComparison.OrdinalIgnoreCase)) continue;

                        var parts = val.Split(',').Select(s => s.Trim())
                            .Where(s => !string.Equals(s, entityId, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        prop.SetValue(item, string.Join(", ", parts));
                        modified = true;
                        var typeDisp = EntityFieldMeta.GetEntityTypeDisplayName(typeKv.Key);
                        var fieldDisp = EntityFieldMeta.GetFieldDisplayName(typeKv.Key, prop.Name);
                        changes.AppendLine($"  {typeDisp}「{bdb.Name}」{fieldDisp}: 移除引用");
                        count++;
                    }

                    if (modified)
                    {
                        bdb.UpdatedAt = DateTime.Now;
                        var updateMethod = service.GetType().GetMethod("UpdateDataAsync");
                        if (updateMethod?.Invoke(service, new[] { item }) is Task t)
                            await t.ConfigureAwait(false);
                    }
                }
            }

            try
            {
                var guideManager = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.GuideManager>();
                var fieldName = GetContextIdFieldName(entityType);
                var (delCount, delDetails) = await PersistContentGuideDeletionAsync(guideManager, entityType, fieldName, entityId).ConfigureAwait(false);
                count += delCount;
                if (delDetails.Length > 0)
                    changes.Append(delDetails);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EntityPropagationService] Guide 删除传播异常: {ex.Message}");
                changes.AppendLine($"  [警告] Guide 传播失败: {ex.Message}");
            }

            await RefreshCachesAsync().ConfigureAwait(false);

            TM.App.Log($"[EntityPropagationService] PropagateDeletion 完成: {entityType}:{entityId}，{count} 处清理");

            if (count == 0)
                return $"删除传播完成，未发现需要清理的引用";

            return $"删除传播完成，清理 {count} 处:\n{changes.ToString().TrimEnd()}";
        }

        #region 内部方法

        private static object? GetService(string entityType)
        {
            if (!EntityServiceMap.TryGetValue(entityType, out var serviceType) || serviceType == null)
                return null;
            try
            {
                return ServiceLocator.GetOrDefault(serviceType);
            }
            catch
            {
                return null;
            }
        }

        private static string GetContextIdFieldName(string entityType)
        {
            return entityType.ToLowerInvariant() switch
            {
                "characters" => "Characters",
                "factions" => "Factions",
                "locations" => "Locations",
                "plotrules" => "PlotRules",
                "worldrules" => "WorldRuleIds",
                "templates" => "TemplateIds",
                _ => string.Empty
            };
        }

        private static async Task RefreshCachesAsync()
        {
            try
            {
                var relationService = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.RelationStrengthService>();
                relationService.InvalidateCache();

                var indexService = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.DataIndexService>();
                await indexService.InitializeAsync().ConfigureAwait(false);

                TM.App.Log("[EntityPropagationService] 缓存刷新完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EntityPropagationService] 缓存刷新部分失败: {ex.Message}");
            }
        }

        private static async Task<(int Count, StringBuilder Details)> PersistContentGuideRenameAsync(
            TM.Services.Modules.ProjectData.Implementations.GuideManager guideManager,
            string oldName,
            string newName)
        {
            var details = new StringBuilder();
            int count = 0;
            TM.Services.Modules.ProjectData.Implementations.ChapterSummaryStore? summaryStore = null;

            var volumes = guideManager.GetExistingVolumeNumbers("content_guide.json");
            var files = volumes.Count > 0
                ? volumes.Select(v => TM.Services.Modules.ProjectData.Implementations.GuideManager.GetVolumeFileName("content_guide.json", v)).ToList()
                : new List<string> { "content_guide.json" };

            bool anyDirty = false;
            foreach (var file in files)
            {
                var guide = await guideManager.GetGuideAsync<TM.Services.Modules.ProjectData.Models.Guides.ContentGuide>(file).ConfigureAwait(false);
                bool modified = false;
                foreach (var kv in guide.Chapters)
                {
                    var entry = kv.Value;
                    if (entry == null) continue;
                    if (!string.IsNullOrEmpty(entry.Summary) && entry.Summary.Contains(oldName, StringComparison.Ordinal))
                    {
                        entry.Summary = entry.Summary.Replace(oldName, newName, StringComparison.Ordinal);
                        modified = true;
                        details.AppendLine($"  内容导引 摘要: \"{oldName}\" → \"{newName}\"");
                        count++;

                        try
                        {
                            summaryStore ??= ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.ChapterSummaryStore>();
                            var summary = await summaryStore.GetSummaryAsync(kv.Key).ConfigureAwait(false);
                            if (!string.IsNullOrEmpty(summary) && summary.Contains(oldName, StringComparison.Ordinal))
                            {
                                var updated = summary.Replace(oldName, newName, StringComparison.Ordinal);
                                await summaryStore.SetSummaryAsync(kv.Key, updated).ConfigureAwait(false);
                                details.AppendLine($"  章节摘要: \"{oldName}\" → \"{newName}\"");
                                count++;
                            }
                        }
                        catch (Exception ex)
                        {
                            TM.App.Log($"[EntityPropagationService] 摘要传播异常: {ex.Message}");
                        }
                    }
                }

                if (modified)
                {
                    guideManager.MarkDirty(file);
                    anyDirty = true;
                }
            }

            if (anyDirty)
            {
                await guideManager.FlushAllAsync().ConfigureAwait(false);

                try
                {
                    var guideContextService = ServiceLocator.Get<TM.Services.Modules.ProjectData.Interfaces.IGuideContextService>();
                    guideContextService.InvalidateContentGuideCache();
                }
                catch
                {
                }
            }

            return (count, details);
        }

        private static async Task<(int Count, StringBuilder Details)> PersistContentGuideDeletionAsync(
            TM.Services.Modules.ProjectData.Implementations.GuideManager guideManager,
            string entityType,
            string fieldName,
            string entityId)
        {
            var details = new StringBuilder();
            int count = 0;
            if (string.IsNullOrEmpty(fieldName)) return (0, details);

            var volumes = guideManager.GetExistingVolumeNumbers("content_guide.json");
            var files = volumes.Count > 0
                ? volumes.Select(v => TM.Services.Modules.ProjectData.Implementations.GuideManager.GetVolumeFileName("content_guide.json", v)).ToList()
                : new List<string> { "content_guide.json" };

            bool anyDirty = false;
            foreach (var file in files)
            {
                var guide = await guideManager.GetGuideAsync<TM.Services.Modules.ProjectData.Models.Guides.ContentGuide>(file).ConfigureAwait(false);
                bool modified = false;

                foreach (var kv in guide.Chapters)
                {
                    var ctx = kv.Value?.ContextIds;
                    if (ctx == null) continue;

                    var prop = ctx.GetType().GetProperty(fieldName);
                    if (prop?.GetValue(ctx) is List<string> ids && ids.RemoveAll(id =>
                        string.Equals(id, entityId, StringComparison.OrdinalIgnoreCase)) > 0)
                    {
                        modified = true;
                        var typeDisp = EntityFieldMeta.GetEntityTypeDisplayName(entityType);
                        details.AppendLine($"  内容导引 上下文: 移除{typeDisp}引用");
                        count++;
                    }
                }

                if (modified)
                {
                    guideManager.MarkDirty(file);
                    anyDirty = true;
                }
            }

            if (anyDirty)
            {
                await guideManager.FlushAllAsync().ConfigureAwait(false);

                try
                {
                    var guideContextService = ServiceLocator.Get<TM.Services.Modules.ProjectData.Interfaces.IGuideContextService>();
                    guideContextService.InvalidateContentGuideCache();
                }
                catch
                {
                }
            }

            return (count, details);
        }

        private static async Task<(int Count, StringBuilder Details)> PersistChapterContentRenameAsync(
            string oldName,
            string newName,
            HashSet<string> affectedChapterIds)
        {
            var details = new StringBuilder();
            int count = 0;

            TM.Services.Modules.ProjectData.Implementations.GeneratedContentService contentService;
            try
            {
                contentService = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.GeneratedContentService>();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EntityPropagationService] 未能解析 GeneratedContentService: {ex.Message}");
                return (0, details);
            }

            List<TM.Services.Modules.ProjectData.Models.Generated.ChapterInfo> chapters;
            try
            {
                chapters = await contentService.GetGeneratedChaptersAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[EntityPropagationService] 枚举章节失败: {ex.Message}");
                return (0, details);
            }

            foreach (var info in chapters)
            {
                var chapterId = info?.Id;
                if (string.IsNullOrWhiteSpace(chapterId)) continue;

                string? content;
                try
                {
                    content = await contentService.GetChapterAsync(chapterId!).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[EntityPropagationService] 读取章节 {chapterId} 失败（跳过）: {ex.Message}");
                    continue;
                }

                if (string.IsNullOrEmpty(content)) continue;
                if (!content.Contains(oldName, StringComparison.Ordinal)) continue;

                var replaced = content.Replace(oldName, newName, StringComparison.Ordinal);
                if (string.Equals(replaced, content, StringComparison.Ordinal)) continue;

                try
                {
                    await contentService.SaveChapterAsync(chapterId!, replaced).ConfigureAwait(false);
                    affectedChapterIds.Add(chapterId!);
                    count++;
                    details.AppendLine($"  章节正文「{chapterId}」: \"{oldName}\" → \"{newName}\"");
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[EntityPropagationService] 写回章节 {chapterId} 失败: {ex.Message}");
                    details.AppendLine($"  [警告] 章节「{chapterId}」写回失败: {ex.Message}");
                }
            }

            return (count, details);
        }

        private static readonly (string FileName, Type GuideType, bool IsVolumeSharded)[] TrackingGuideFiles = new[]
        {
            ("character_state_guide.json", typeof(TM.Services.Modules.ProjectData.Models.Guides.CharacterStateGuide), true),
            ("conflict_progress_guide.json", typeof(TM.Services.Modules.ProjectData.Models.Guides.ConflictProgressGuide), true),
            ("location_state_guide.json", typeof(TM.Services.Modules.ProjectData.Models.Guides.LocationStateGuide), true),
            ("faction_state_guide.json", typeof(TM.Services.Modules.ProjectData.Models.Guides.FactionStateGuide), true),
            ("timeline_guide.json", typeof(TM.Services.Modules.ProjectData.Models.Guides.TimelineGuide), true),
            ("item_state_guide.json", typeof(TM.Services.Modules.ProjectData.Models.Guides.ItemStateGuide), true),
            ("foreshadowing_status_guide.json", typeof(TM.Services.Modules.ProjectData.Models.Guides.ForeshadowingStatusGuide), false),
            ("secret_reveal_guide.json", typeof(TM.Services.Modules.ProjectData.Models.Guides.SecretRevealGuide), true),
            ("pledge_constraint_guide.json", typeof(TM.Services.Modules.ProjectData.Models.Guides.PledgeConstraintGuide), true),
            ("deadline_constraint_guide.json", typeof(TM.Services.Modules.ProjectData.Models.Guides.DeadlineConstraintGuide), true),
        };

        private static async Task<(int Count, StringBuilder Details)> PersistTrackingGuidesRenameAsync(
            TM.Services.Modules.ProjectData.Implementations.GuideManager guideManager,
            string oldName,
            string newName)
        {
            var details = new StringBuilder();
            int totalCount = 0;
            bool anyDirty = false;

            var getGuideAsyncGeneric = typeof(TM.Services.Modules.ProjectData.Implementations.GuideManager)
                .GetMethod("GetGuideAsync", BindingFlags.Public | BindingFlags.Instance);
            if (getGuideAsyncGeneric == null)
            {
                TM.App.Log("[EntityPropagationService] 未能解析 GuideManager.GetGuideAsync 方法，跳过追踪 Guide 传播");
                return (0, details);
            }

            foreach (var (fileName, guideType, isVolumeSharded) in TrackingGuideFiles)
            {
                List<string> files;
                if (isVolumeSharded)
                {
                    var volumes = guideManager.GetExistingVolumeNumbers(fileName);
                    files = volumes.Count > 0
                        ? volumes.Select(v => TM.Services.Modules.ProjectData.Implementations.GuideManager.GetVolumeFileName(fileName, v)).ToList()
                        : new List<string>();
                }
                else
                {
                    files = new List<string> { fileName };
                }

                foreach (var file in files)
                {
                    object? guide;
                    try
                    {
                        var closedMethod = getGuideAsyncGeneric.MakeGenericMethod(guideType);
                        var task = closedMethod.Invoke(guideManager, new object[] { file }) as Task;
                        if (task == null) continue;
                        await task.ConfigureAwait(false);
                        var resultProp = task.GetType().GetProperty("Result");
                        guide = resultProp?.GetValue(task);
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[EntityPropagationService] 加载 {file} 失败（跳过）: {ex.Message}");
                        continue;
                    }

                    if (guide == null) continue;

                    var localCount = 0;
                    var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
                    ReplaceStringFieldsRecursive(guide, oldName, newName, visited, ref localCount);

                    if (localCount > 0)
                    {
                        guideManager.MarkDirty(file);
                        anyDirty = true;
                        totalCount += localCount;
                        details.AppendLine($"  {fileName}「{file}」: 替换 {localCount} 处");
                    }
                }
            }

            if (anyDirty)
                await guideManager.FlushAllAsync().ConfigureAwait(false);

            return (totalCount, details);
        }

        private static void ReplaceStringFieldsRecursive(object obj, string oldName, string newName, HashSet<object> visited, ref int count)
        {
            if (obj == null) return;
            if (!visited.Add(obj)) return;

            var type = obj.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(DateTime)) return;

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0) continue;
                var propType = prop.PropertyType;

                object? value;
                try { value = prop.GetValue(obj); }
                catch { continue; }
                if (value == null) continue;

                if (propType == typeof(string))
                {
                    if (!prop.CanWrite) continue;
                    var s = (string)value;
                    if (string.IsNullOrEmpty(s)) continue;
                    if (s.Contains(oldName, StringComparison.Ordinal))
                    {
                        var replaced = s.Replace(oldName, newName, StringComparison.Ordinal);
                        if (!string.Equals(replaced, s, StringComparison.Ordinal))
                        {
                            try { prop.SetValue(obj, replaced); count++; }
                            catch { }
                        }
                    }
                    continue;
                }

                if (propType.IsGenericType)
                {
                    var genDef = propType.GetGenericTypeDefinition();
                    if (genDef == typeof(Dictionary<,>) || genDef == typeof(IDictionary<,>))
                    {
                        if (value is IDictionary dict)
                        {
                            foreach (var v in dict.Values)
                                if (v != null) ReplaceStringFieldsRecursive(v, oldName, newName, visited, ref count);
                        }
                        continue;
                    }

                    if (genDef == typeof(List<>) || genDef == typeof(IList<>))
                    {
                        var elemType = propType.GetGenericArguments()[0];
                        if (elemType == typeof(string))
                        {
                            if (value is IList strList)
                            {
                                for (int i = 0; i < strList.Count; i++)
                                {
                                    var s = strList[i] as string;
                                    if (string.IsNullOrEmpty(s)) continue;
                                    if (s.Contains(oldName, StringComparison.Ordinal))
                                    {
                                        var replaced = s.Replace(oldName, newName, StringComparison.Ordinal);
                                        if (!string.Equals(replaced, s, StringComparison.Ordinal))
                                        {
                                            strList[i] = replaced;
                                            count++;
                                        }
                                    }
                                }
                            }
                            continue;
                        }

                        if (!elemType.IsPrimitive && elemType != typeof(DateTime) && !elemType.IsEnum)
                        {
                            if (value is IEnumerable enumerable)
                            {
                                foreach (var item in enumerable)
                                    if (item != null) ReplaceStringFieldsRecursive(item, oldName, newName, visited, ref count);
                            }
                            continue;
                        }
                        continue;
                    }
                }

                if (propType.IsClass)
                {
                    ReplaceStringFieldsRecursive(value, oldName, newName, visited, ref count);
                }
            }
        }

        #endregion
    }
}
