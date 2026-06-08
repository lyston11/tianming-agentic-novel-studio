using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
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
using TM.Framework.Common.Models;
using TM.Services.Modules.ProjectData.Models.Common;
using TM.Services.Modules.ProjectData.Metadata;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public class DataEditPlugin
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

        private static readonly JsonSerializerOptions EntityChangeJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        [KernelFunction, Description("预览变更（不落盘）。返回 previewId + diff。")]
        public async Task<string> PreviewChange(
            [Description("变更操作数组 JSON。格式：[{\"type\":\"characters|locations|factions|plotrules|worldrules|templates|outline|volumedesign|chapter|blueprint\",\"id\":\"实体Id\",\"op\":\"Create|UpdateField|Rename|Delete\",\"payload\":{}}]。字段名必须是 type/id/op/payload（小写），不要写成 entityType/entityId。")] string changesJson)
        {
            try
            {
                var operations = JsonSerializer.Deserialize<List<EntityChangeOperation>>(changesJson, EntityChangeJsonOptions);
                if (operations == null || operations.Count == 0)
                    return "[错误] changesJson 解析为空";

                for (int i = 0; i < operations.Count; i++)
                {
                    var op = operations[i];
                    if (string.IsNullOrWhiteSpace(op.EntityType))
                        return $"[错误] 第 {i + 1} 个操作缺少 type 字段。请使用 type（不是 entityType），可选值：characters/locations/factions/plotrules/worldrules/templates/outline/volumedesign/chapter/blueprint";
                    if (string.IsNullOrWhiteSpace(op.EntityId) && !string.Equals(op.Op, "Create", StringComparison.OrdinalIgnoreCase))
                        return $"[错误] 第 {i + 1} 个操作缺少 id 字段（Create 操作除外）。请使用 id（不是 entityId）";
                    if (string.IsNullOrWhiteSpace(op.Op))
                        return $"[错误] 第 {i + 1} 个操作缺少 op 字段。可选值：Create / UpdateField / Rename / Delete";
                    if (!EntityServiceMap.ContainsKey(op.EntityType))
                        return $"[错误] 第 {i + 1} 个操作的 type=\"{op.EntityType}\" 不被支持。可选值：characters/locations/factions/plotrules/worldrules/templates/outline/volumedesign/chapter/blueprint";
                }

                var snapshots = new Dictionary<string, string>();
                var diffSummary = new StringBuilder();
                var idToName = BuildIdToNameIndex();
                var entityNames = new Dictionary<string, string>();
                var missingEntities = new List<string>();

                foreach (var op in operations)
                {
                    var isCreate = string.Equals(op.Op, "Create", StringComparison.OrdinalIgnoreCase);
                    var snapshotKey = $"{op.EntityType}:{op.EntityId}";
                    if (!isCreate && !snapshots.ContainsKey(snapshotKey))
                    {
                        var entity = await GetEntityByIdAsync(op.EntityType, op.EntityId).ConfigureAwait(false);
                        if (entity == null)
                        {
                            var typeDisp = EntityFieldMeta.GetEntityTypeDisplayName(op.EntityType);
                            missingEntities.Add($"{typeDisp}:{op.EntityId}");
                            diffSummary.AppendLine($"[警告] {typeDisp} 未找到对应实体: {op.EntityId}");
                            continue;
                        }
                        snapshots[snapshotKey] = JsonSerializer.Serialize(entity, entity.GetType());
                        if (entity is IDataItem di0)
                            entityNames[snapshotKey] = di0.Name;
                    }

                    var entityName = isCreate ? "(新建)" : entityNames.GetValueOrDefault(snapshotKey, "(未知)");
                    diffSummary.AppendLine(BuildDiffLine(op, entityName, idToName));
                }

                if (snapshots.Count == 0 && !operations.Any(o => string.Equals(o.Op, "Create", StringComparison.OrdinalIgnoreCase)))
                {
                    var missing = string.Join("、", missingEntities.Take(5));
                    return $"[错误] 所有目标实体均不存在，未创建预览。请使用 ListAvailableIds / SearchTextInAllEntities 确认 id 后重试。缺失: {missing}";
                }

                var hasDelete = operations.Any(o => string.Equals(o.Op, "Delete", StringComparison.OrdinalIgnoreCase));
                if (hasDelete)
                {
                    diffSummary.Insert(0, "[警告] 包含删除操作，此操作不可撤销（回滚仅能恢复快照，已传播的关联清理无法自动逆向）。请仔细确认。\n");
                }

                var previewId = PendingChangeStore.CreatePreview(operations, snapshots);

                return JsonSerializer.Serialize(new
                {
                    previewId,
                    operationCount = operations.Count,
                    diff = diffSummary.ToString().TrimEnd(),
                    message = hasDelete
                        ? "[警告] 包含删除操作，请仔细确认。调用 ConfirmChange 确认执行，或 RollbackChange 取消。"
                        : "请确认以上变更。调用 ConfirmChange 确认执行，或 RollbackChange 取消。"
                }, JsonHelper.Default);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataEditPlugin] PreviewChange 异常: {ex.Message}");
                return $"[错误] 预览失败: {ex.Message}";
            }
        }

        [KernelFunction, Description("确认执行预览的变更（落盘 + 刷新缓存）。")]
        public async Task<string> ConfirmChange(
            [Description("PreviewChange 返回的 previewId")] string previewId)
        {
            try
            {
                var entry = PendingChangeStore.GetPreview(previewId);
                if (entry == null)
                    return "[错误] 预览不存在或已过期，请重新 PreviewChange";

                var results = new StringBuilder();
                var propagationService = new TM.Services.Framework.AI.EntityPropagationService();
                var idToName = BuildIdToNameIndex();

                foreach (var op in entry.Operations)
                {
                    var result = await ExecuteOperationAsync(op, idToName).ConfigureAwait(false);
                    results.AppendLine(result);

                    if (string.Equals(op.Op, "Rename", StringComparison.OrdinalIgnoreCase)
                        && op.Payload.TryGetProperty("newName", out var newNameEl))
                    {
                        var snapshotKey = $"{op.EntityType}:{op.EntityId}";
                        var oldName = string.Empty;
                        if (entry.Snapshots.TryGetValue(snapshotKey, out var snap))
                        {
                            var snapDoc = JsonDocument.Parse(snap);
                            if (snapDoc.RootElement.TryGetProperty("Name", out var nameEl))
                                oldName = nameEl.GetString() ?? string.Empty;
                        }
                        if (!string.IsNullOrEmpty(oldName))
                        {
                            results.AppendLine($"  [传播] Rename '{oldName}' → '{newNameEl.GetString()}' 已由 Service 层触发，后台执行（详情见日志）");
                        }
                    }
                    else if (string.Equals(op.Op, "Delete", StringComparison.OrdinalIgnoreCase))
                    {
                        var propResult = await propagationService.PropagateDeletionAsync(op.EntityId, op.EntityType).ConfigureAwait(false);
                        results.AppendLine($"  [传播] {propResult}");
                    }
                }

                await RefreshCachesAsync().ConfigureAwait(false);
                PendingChangeStore.Remove(previewId);

                try
                {
                    TM.Framework.Common.Helpers.GlobalToast.Success("变更已保存",
                        $"已成功执行 {entry.Operations.Count} 个变更，数据已落盘。");
                }
                catch { }

                var resultText = results.ToString().TrimEnd();

                EditHistoryLog.Append(entry, resultText);

                TM.App.Log($"[DataEditPlugin] ConfirmChange 完成: {previewId}，{entry.Operations.Count} 个操作");
                return $"[完成] 已确认执行 {entry.Operations.Count} 个变更:\n{resultText}";
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataEditPlugin] ConfirmChange 异常: {ex.Message}");
                return $"[错误] 确认执行失败: {ex.Message}";
            }
        }

        [KernelFunction, Description("直接执行变更（预览+确认一步完成，无需用户确认）。Agent/Plan 模式专用。")]
        public async Task<string> ExecuteChange(
            [Description("变更操作数组 JSON。格式同 PreviewChange：[{\"type\":\"characters|locations|factions|plotrules|worldrules|templates|outline|volumedesign|chapter|blueprint\",\"id\":\"实体Id\",\"op\":\"Create|UpdateField|Rename|Delete\",\"payload\":{}}]。字段名必须是 type/id/op/payload，不要写成 entityType/entityId。")] string changesJson)
        {
            try
            {
                var previewResult = await PreviewChange(changesJson).ConfigureAwait(false);
                if (previewResult.StartsWith("[错误]", StringComparison.Ordinal))
                    return previewResult;

                string? previewId = null;
                try
                {
                    using var doc = JsonDocument.Parse(previewResult);
                    if (doc.RootElement.TryGetProperty("previewId", out var pidEl))
                        previewId = pidEl.GetString();
                }
                catch
                {
                    return $"[错误] 预览结果解析失败，无法自动确认";
                }

                if (string.IsNullOrEmpty(previewId))
                    return "[错误] 预览未生成 previewId，无法自动确认";

                var confirmResult = await ConfirmChange(previewId).ConfigureAwait(false);

                TM.App.Log($"[DataEditPlugin] ExecuteChange 完成: {previewId}");
                return confirmResult;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataEditPlugin] ExecuteChange 异常: {ex.Message}");
                return $"[错误] 执行失败: {ex.Message}";
            }
        }

        [KernelFunction, Description("回滚预览的变更（从快照恢复原始数据）。")]
        public async Task<string> RollbackChange(
            [Description("PreviewChange 返回的 previewId")] string previewId)
        {
            try
            {
                var entry = PendingChangeStore.GetPreview(previewId);
                if (entry == null)
                    return "[错误] 预览不存在或已过期，无需回滚";

                var results = new StringBuilder();
                foreach (var kv in entry.Snapshots)
                {
                    var parts = kv.Key.Split(':', 2);
                    if (parts.Length != 2) continue;

                    var entityType = parts[0];
                    var result = await RestoreFromSnapshotAsync(entityType, kv.Value).ConfigureAwait(false);
                    results.AppendLine(result);
                }

                if (entry.Snapshots.Count > 0)
                {
                    await RefreshCachesAsync().ConfigureAwait(false);
                }
                PendingChangeStore.Remove(previewId);

                TM.App.Log($"[DataEditPlugin] RollbackChange 完成: {previewId}");
                return entry.Snapshots.Count == 0
                    ? "[完成] 预览已取消（无需回滚，未对实体做任何修改）"
                    : $"[完成] 已回滚，恢复 {entry.Snapshots.Count} 个实体:\n{results.ToString().TrimEnd()}";
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataEditPlugin] RollbackChange 异常: {ex.Message}");
                return $"[错误] 回滚失败: {ex.Message}";
            }
        }

        [KernelFunction, Description("全量数据对账。仅在用户要求对账时调用。")]
        public async Task<string> ReconcileAllData()
        {
            try
            {
                var reconciler = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.ConsistencyReconciler>();
                var result = await reconciler.ReconcileAsync().ConfigureAwait(false);
                TM.App.Log($"[DataEditPlugin] ReconcileAllData 完成");
                return $"对账完成: {result}";
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataEditPlugin] ReconcileAllData 异常: {ex.Message}");
                return $"[错误] 对账失败: {ex.Message}";
            }
        }

        [KernelFunction, Description("全局文本搜索（覆盖 10 类实体字段 + 章节正文 + 章节摘要）。用于改名前评估影响范围。")]
        public async Task<string> SearchTextInAllEntities(
            [Description("要搜索的文本")] string searchText,
            [Description("是否仅搜索（true=只搜不改，false=返回匹配列表供后续替换）")] bool searchOnly = true)
        {
            try
            {
                if (string.IsNullOrEmpty(searchText))
                    return "[错误] 搜索文本不能为空";

                var results = new StringBuilder();
                int entityMatches = 0;
                int chapterMatches = 0;
                int summaryMatches = 0;

                foreach (var typeKv in EntityServiceMap)
                {
                    var service = GetService(typeKv.Key);
                    if (service == null) continue;

                    var getAllMethod = service.GetType().GetMethod("GetAllData");
                    if (getAllMethod?.Invoke(service, null) is not System.Collections.IList allData) continue;

                    foreach (var item in allData)
                    {
                        if (item is not IDataItem di) continue;

                        var props = item.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .Where(p => p.PropertyType == typeof(string) && p.CanRead);

                        foreach (var prop in props)
                        {
                            var val = prop.GetValue(item) as string;
                            if (!string.IsNullOrEmpty(val) && val.Contains(searchText, StringComparison.Ordinal))
                            {
                                var typeDisp = EntityFieldMeta.GetEntityTypeDisplayName(typeKv.Key);
                                var fieldDisp = EntityFieldMeta.GetFieldDisplayName(typeKv.Key, prop.Name);
                                if (string.Equals(fieldDisp, prop.Name, StringComparison.Ordinal)) fieldDisp = "字段";
                                results.AppendLine($"  {typeDisp}「{di.Name}」{fieldDisp}");
                                entityMatches++;
                            }
                        }
                    }
                }

                try
                {
                    var contentService = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.GeneratedContentService>();
                    var chapters = await contentService.GetGeneratedChaptersAsync().ConfigureAwait(false);
                    foreach (var info in chapters)
                    {
                        var chapterId = info?.Id;
                        if (string.IsNullOrWhiteSpace(chapterId)) continue;
                        string? content;
                        try { content = await contentService.GetChapterAsync(chapterId!).ConfigureAwait(false); }
                        catch { continue; }
                        if (string.IsNullOrEmpty(content)) continue;
                        if (content.Contains(searchText, StringComparison.Ordinal))
                        {
                            int occ = 0, idx = 0;
                            while ((idx = content.IndexOf(searchText, idx, StringComparison.Ordinal)) >= 0)
                            {
                                occ++;
                                idx += searchText.Length;
                            }
                            results.AppendLine($"  章节正文「{chapterId}」: {occ} 处");
                            chapterMatches += occ;
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[DataEditPlugin] 章节正文搜索异常: {ex.Message}");
                }

                try
                {
                    var summaryStore = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.ChapterSummaryStore>();
                    var contentService = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.GeneratedContentService>();
                    var chapters = await contentService.GetGeneratedChaptersAsync().ConfigureAwait(false);
                    foreach (var info in chapters)
                    {
                        var chapterId = info?.Id;
                        if (string.IsNullOrWhiteSpace(chapterId)) continue;
                        string? summary;
                        try { summary = await summaryStore.GetSummaryAsync(chapterId!).ConfigureAwait(false); }
                        catch { continue; }
                        if (string.IsNullOrEmpty(summary)) continue;
                        if (summary.Contains(searchText, StringComparison.Ordinal))
                        {
                            results.AppendLine($"  章节摘要「{chapterId}」");
                            summaryMatches++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[DataEditPlugin] 章节摘要搜索异常: {ex.Message}");
                }

                var totalMatches = entityMatches + chapterMatches + summaryMatches;
                if (totalMatches == 0)
                    return $"未找到包含 \"{searchText}\" 的字段";

                return $"共 {totalMatches} 处匹配 \"{searchText}\"：实体 {entityMatches}，章节正文 {chapterMatches}，章节摘要 {summaryMatches}\n{results.ToString().TrimEnd()}\n\n改名建议：直接 Rename 对应实体（Service 层会自动传播到正文/向量/追踪 Guide/关键词索引），无需逐字段修改。";
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataEditPlugin] SearchTextInAllEntities 异常: {ex.Message}");
                return $"[错误] 搜索失败: {ex.Message}";
            }
        }

        [KernelFunction, Description("修改前影响分析：查找实体被引用的位置。")]
        public async Task<string> FindEntityReferences(
            [Description("实体 ID")] string entityId,
            [Description("实体类型（characters/locations/factions/plotrules/worldrules/templates/outline/volumedesign/chapter/blueprint）")] string entityType)
        {
            try
            {
                var results = new StringBuilder();
                var entityIdLower = entityId.ToLowerInvariant();

                var typeDisplay = EntityFieldMeta.GetEntityTypeDisplayName(entityType);
                var entity = await GetEntityByIdAsync(entityType, entityId).ConfigureAwait(false);
                var entityName = (entity as IDataItem)?.Name ?? "(未知)";

                var guideContextService = ServiceLocator.Get<TM.Services.Modules.ProjectData.Interfaces.IGuideContextService>();
                var contentGuide = await guideContextService.GetContentGuideAsync().ConfigureAwait(false);
                int guideRefCount = 0;
                if (contentGuide?.Chapters != null)
                {
                    foreach (var kv in contentGuide.Chapters)
                    {
                        var ctx = kv.Value?.ContextIds;
                        if (ctx == null) continue;

                        var listFieldName = GetContextIdFieldName(entityType);
                        if (!string.IsNullOrEmpty(listFieldName))
                        {
                            var prop = ctx.GetType().GetProperty(listFieldName);
                            if (prop?.GetValue(ctx) is List<string> ids && ids.Contains(entityId, StringComparer.OrdinalIgnoreCase))
                            {
                                guideRefCount++;
                            }
                        }
                    }
                }

                if (guideRefCount > 0)
                    results.AppendLine($"  内容导引: {guideRefCount} 处引用");

                foreach (var typeKv in EntityServiceMap)
                {
                    if (string.Equals(typeKv.Key, entityType, StringComparison.OrdinalIgnoreCase)) continue;

                    var service = GetService(typeKv.Key);
                    if (service == null) continue;

                    var getAllMethod = service.GetType().GetMethod("GetAllData");
                    if (getAllMethod?.Invoke(service, null) is not System.Collections.IList allData) continue;

                    foreach (var item in allData)
                    {
                        if (item is not IDataItem di) continue;
                        var itemJson = JsonSerializer.Serialize(item, item.GetType());
                        if (itemJson.Contains(entityId, StringComparison.OrdinalIgnoreCase)
                            || (!string.IsNullOrEmpty(entityName) && itemJson.Contains(entityName, StringComparison.Ordinal)))
                        {
                            var refTypeDisplay = EntityFieldMeta.GetEntityTypeDisplayName(typeKv.Key);
                            results.AppendLine($"  {refTypeDisplay}「{di.Name}」引用了此实体");
                        }
                    }
                }

                if (results.Length == 0)
                    return $"未找到 {typeDisplay}「{entityName}」的引用";

                return $"{typeDisplay}「{entityName}」被以下位置引用:\n{results.ToString().TrimEnd()}";
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataEditPlugin] FindEntityReferences 异常: {ex.Message}");
                return $"[错误] 查找引用失败: {ex.Message}";
            }
        }

        [KernelFunction, Description("列出实体可编辑字段，供构造 PreviewChange 参数用。")]
        public Task<string> ListEntityFields(
            [Description("实体类型（characters/locations/factions/plotrules/worldrules/templates/outline/volumedesign/chapter/blueprint）")] string entityType)
        {
            try
            {
                var service = GetService(entityType);
                if (service == null)
                    return Task.FromResult($"[错误] 未知实体类型: {entityType}");

                var getAllMethod = service.GetType().GetMethod("GetAllData");
                if (getAllMethod == null)
                    return Task.FromResult($"[错误] {entityType} 服务无 GetAllData 方法");

                var returnType = getAllMethod.ReturnType;
                var dataType = returnType.IsGenericType ? returnType.GetGenericArguments()[0] : null;
                if (dataType == null)
                    return Task.FromResult($"[错误] 无法推断 {entityType} 数据类型");

                var sb = new StringBuilder();
                sb.AppendLine($"{EntityFieldMeta.GetEntityTypeDisplayName(entityType)} 可编辑字段:");

                var baseFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "Id", "CreatedAt", "UpdatedAt" };

                var props = dataType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead && p.CanWrite)
                    .OrderBy(p => baseFields.Contains(p.Name) ? 1 : 0)
                    .ThenBy(p => p.Name);

                foreach (var prop in props)
                {
                    var typeName = prop.PropertyType == typeof(List<string>) ? "List<string>"
                        : prop.PropertyType == typeof(string) ? "string"
                        : prop.PropertyType == typeof(bool) ? "bool"
                        : prop.PropertyType == typeof(int) ? "int"
                        : prop.PropertyType == typeof(DateTime) ? "DateTime"
                        : prop.PropertyType.Name;

                    var readOnly = baseFields.Contains(prop.Name) ? " [自动管理]" : "";
                    var displayName = EntityFieldMeta.GetFieldDisplayName(entityType, prop.Name);
                    sb.AppendLine($"  {displayName} ({typeName}){readOnly}");
                }

                return Task.FromResult(sb.ToString().TrimEnd());
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataEditPlugin] ListEntityFields 异常: {ex.Message}");
                return Task.FromResult($"[错误] 列出字段失败: {ex.Message}");
            }
        }

        [KernelFunction, Description("修改前影响分析：查找与指定实体关联的其他实体。")]
        public Task<string> FindRelatedEntities(
            [Description("实体 ID")] string entityId)
        {
            try
            {
                var results = new StringBuilder();
                var idToName = BuildIdToNameIndex();
                var selfName = idToName.TryGetValue(entityId, out var n) ? n : "(未知)";

                foreach (var typeKv in EntityServiceMap)
                {
                    var service = GetService(typeKv.Key);
                    if (service == null) continue;

                    var getAllMethod = service.GetType().GetMethod("GetAllData");
                    if (getAllMethod?.Invoke(service, null) is not System.Collections.IList allData) continue;

                    foreach (var item in allData)
                    {
                        if (item is not IDataItem di) continue;
                        if (string.Equals(di.Id, entityId, StringComparison.OrdinalIgnoreCase)) continue;

                        var itemJson = JsonSerializer.Serialize(item, item.GetType());
                        if (itemJson.Contains(entityId, StringComparison.OrdinalIgnoreCase))
                        {
                            var typeDisplay = EntityFieldMeta.GetEntityTypeDisplayName(typeKv.Key);
                            results.AppendLine($"  {typeDisplay}「{di.Name}」直接引用");
                        }
                    }
                }

                if (results.Length == 0)
                    return Task.FromResult($"未找到与「{selfName}」关联的实体");

                return Task.FromResult($"与「{selfName}」关联的实体:\n{results.ToString().TrimEnd()}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataEditPlugin] FindRelatedEntities 异常: {ex.Message}");
                return Task.FromResult($"[错误] 查找关联失败: {ex.Message}");
            }
        }

        #region 内部方法

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
                "outline" => string.Empty,
                "volumedesign" => string.Empty,
                "chapter" => string.Empty,
                "blueprint" => string.Empty,
                _ => string.Empty
            };
        }

        private Task<object?> GetEntityByIdAsync(string entityType, string entityId)
        {
            var service = GetService(entityType);
            if (service == null) return Task.FromResult<object?>(null);

            var getAllMethod = service.GetType().GetMethod("GetAllData");
            if (getAllMethod == null) return Task.FromResult<object?>(null);

            var allData = getAllMethod.Invoke(service, null) as System.Collections.IList;
            if (allData == null) return Task.FromResult<object?>(null);

            foreach (var item in allData)
            {
                if (item is IDataItem di && string.Equals(di.Id, entityId, StringComparison.OrdinalIgnoreCase))
                    return Task.FromResult<object?>(item);
            }

            return Task.FromResult<object?>(null);
        }

        private object? GetService(string entityType)
        {
            if (!EntityServiceMap.TryGetValue(entityType, out var serviceType) || serviceType == null)
            {
                TM.App.Log($"[DataEditPlugin] 未知实体类型: {entityType}");
                return null;
            }
            try
            {
                return ServiceLocator.GetOrDefault(serviceType);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataEditPlugin] 获取服务失败 {entityType}: {ex.Message}");
                return null;
            }
        }

        private async Task<string> ExecuteOperationAsync(EntityChangeOperation op, Dictionary<string, string> idToName)
        {
            if (string.Equals(op.Op, "Create", StringComparison.OrdinalIgnoreCase))
                return await CreateEntityAsync(op).ConfigureAwait(false);

            var entity = await GetEntityByIdAsync(op.EntityType, op.EntityId).ConfigureAwait(false);
            if (entity == null)
                return $"[跳过] {EntityFieldMeta.GetEntityTypeDisplayName(op.EntityType)} 未找到对应实体";

            switch (op.Op.ToLowerInvariant())
            {
                case "updatefield":
                    return await UpdateFieldAsync(op, entity, idToName).ConfigureAwait(false);
                case "rename":
                    return await RenameEntityAsync(op, entity).ConfigureAwait(false);
                case "delete":
                    return await DeleteEntityAsync(op).ConfigureAwait(false);
                default:
                    return $"[跳过] 不支持的操作: {op.Op}";
            }
        }

        private async Task<string> UpdateFieldAsync(EntityChangeOperation op, object entity, Dictionary<string, string> idToName)
        {
            if (!op.Payload.TryGetProperty("field", out var fieldEl))
                return $"[错误] UpdateField 缺少 field 参数";

            var fieldName = EntityFieldMeta.ResolvePropertyName(op.EntityType, fieldEl.GetString() ?? string.Empty);
            var prop = entity.GetType().GetProperty(fieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null)
                return $"[错误] {EntityFieldMeta.GetEntityTypeDisplayName(op.EntityType)} 不存在字段 {EntityFieldMeta.GetFieldDisplayName(op.EntityType, fieldName)}";

            var oldValue = prop.GetValue(entity)?.ToString() ?? "(null)";

            var isListType = prop.PropertyType == typeof(List<string>);

            if (op.Payload.TryGetProperty("append", out var appendEl))
            {
                if (isListType)
                {
                    var list = prop.GetValue(entity) as List<string> ?? new List<string>();
                    var appendValue = appendEl.GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(appendValue) && !list.Contains(appendValue, StringComparer.OrdinalIgnoreCase))
                        list.Add(appendValue);
                    prop.SetValue(entity, list);
                }
                else
                {
                    var appendValue = appendEl.GetString() ?? string.Empty;
                    var currentStr = prop.GetValue(entity)?.ToString() ?? string.Empty;
                    var newValue = string.IsNullOrEmpty(currentStr) ? appendValue : $"{currentStr}, {appendValue}";
                    prop.SetValue(entity, Convert.ChangeType(newValue, prop.PropertyType));
                }
            }
            else if (op.Payload.TryGetProperty("remove", out var removeEl) && isListType)
            {
                var list = prop.GetValue(entity) as List<string> ?? new List<string>();
                var removeValue = removeEl.GetString() ?? string.Empty;
                list.RemoveAll(x => string.Equals(x, removeValue, StringComparison.OrdinalIgnoreCase));
                prop.SetValue(entity, list);
            }
            else if (op.Payload.TryGetProperty("value", out var valueEl))
            {
                if (isListType)
                {
                    var items = valueEl.ValueKind == JsonValueKind.Array
                        ? valueEl.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList()
                        : new List<string> { valueEl.GetString() ?? string.Empty };
                    prop.SetValue(entity, items);
                }
                else
                {
                    var newValue = valueEl.GetString() ?? string.Empty;
                    prop.SetValue(entity, Convert.ChangeType(newValue, prop.PropertyType));
                }
            }
            else
            {
                return $"[错误] UpdateField 缺少 value、append 或 remove 参数";
            }

            var updatedValue = prop.GetValue(entity)?.ToString() ?? "(null)";

            SetTimestamp(entity);

            await UpdateEntityAsync(op.EntityType, entity).ConfigureAwait(false);
            var typeDisplay = EntityFieldMeta.GetEntityTypeDisplayName(op.EntityType);
            var fieldDisplay = EntityFieldMeta.GetFieldDisplayName(op.EntityType, fieldName);
            var entityNameStr = (entity as IDataItem)?.Name ?? "";
            return $"✓ {typeDisplay}「{entityNameStr}」{fieldDisplay}: \"{ResolveValueForDisplay(oldValue, idToName)}\" → \"{ResolveValueForDisplay(updatedValue, idToName)}\"";
        }

        private async Task<string> RenameEntityAsync(EntityChangeOperation op, object entity)
        {
            if (!op.Payload.TryGetProperty("newName", out var nameEl))
                return $"[错误] Rename 缺少 newName 参数";

            var newName = nameEl.GetString() ?? string.Empty;
            if (entity is IDataItem di)
            {
                var oldName = di.Name;
                di.Name = newName;
                SetTimestamp(entity);
                await UpdateEntityAsync(op.EntityType, entity).ConfigureAwait(false);
                return $"✓ 重命名 {EntityFieldMeta.GetEntityTypeDisplayName(op.EntityType)}:「{oldName}」→「{newName}」";
            }

            return $"[错误] 实体不支持重命名";
        }

        private async Task<string> CreateEntityAsync(EntityChangeOperation op)
        {
            var service = GetService(op.EntityType);
            if (service == null)
                return $"[错误] 未找到 {op.EntityType} 服务";

            var getAllMethod = service.GetType().GetMethod("GetAllData");
            if (getAllMethod == null)
                return $"[错误] {op.EntityType} 服务无 GetAllData 方法";

            var returnType = getAllMethod.ReturnType;
            var dataType = returnType.IsGenericType ? returnType.GetGenericArguments()[0] : null;
            if (dataType == null)
                return $"[错误] 无法推断 {op.EntityType} 数据类型";

            var entity = Activator.CreateInstance(dataType);
            if (entity == null)
                return $"[错误] 无法创建 {op.EntityType} 实例";

            if (entity is IDataItem di)
            {
                di.Id = Guid.NewGuid().ToString();
                SetTimestamp(entity, setCreated: true);

                if (op.Payload.TryGetProperty("name", out var nameEl) || op.Payload.TryGetProperty("Name", out nameEl))
                    di.Name = nameEl.GetString() ?? string.Empty;
            }

            foreach (var kvp in op.Payload.EnumerateObject())
            {
                if (string.Equals(kvp.Name, "name", StringComparison.OrdinalIgnoreCase)) continue;
                var resolvedName = EntityFieldMeta.ResolvePropertyName(op.EntityType, kvp.Name);
                var prop = dataType.GetProperty(resolvedName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop == null || !prop.CanWrite) continue;

                try
                {
                    if (prop.PropertyType == typeof(List<string>) && kvp.Value.ValueKind == JsonValueKind.Array)
                    {
                        var list = kvp.Value.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList();
                        prop.SetValue(entity, list);
                    }
                    else if (prop.PropertyType == typeof(string))
                    {
                        prop.SetValue(entity, kvp.Value.GetString() ?? string.Empty);
                    }
                    else
                    {
                        prop.SetValue(entity, Convert.ChangeType(kvp.Value.GetString(), prop.PropertyType));
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[DataEditPlugin] Create 设置字段 {kvp.Name} 失败: {ex.Message}");
                }
            }

            var addMethod = service.GetType().GetMethod("AddDataAsync");
            if (addMethod == null)
                return $"[错误] {op.EntityType} 服务无 AddDataAsync 方法";

            var task = addMethod.Invoke(service, new[] { entity }) as Task;
            if (task != null) await task.ConfigureAwait(false);

            var entityName = (entity as IDataItem)?.Name ?? "(unnamed)";
            return $"✓ 创建 {EntityFieldMeta.GetEntityTypeDisplayName(op.EntityType)}:「{entityName}」";
        }

        private async Task<string> DeleteEntityAsync(EntityChangeOperation op)
        {
            var entityForName = await GetEntityByIdAsync(op.EntityType, op.EntityId).ConfigureAwait(false);
            var entityName = (entityForName as IDataItem)?.Name ?? "(未知)";
            var typeDisplay = EntityFieldMeta.GetEntityTypeDisplayName(op.EntityType);

            var service = GetService(op.EntityType);
            if (service == null)
                return $"[错误] 未找到 {typeDisplay} 服务";

            var deleteMethod = service.GetType().GetMethod("DeleteDataAsync");
            if (deleteMethod == null)
                return $"[错误] {typeDisplay} 服务无 DeleteDataAsync 方法";

            var task = deleteMethod.Invoke(service, new object[] { op.EntityId }) as Task;
            if (task != null) await task.ConfigureAwait(false);

            return $"✓ 删除 {typeDisplay}:「{entityName}」";
        }

        private async Task UpdateEntityAsync(string entityType, object entity)
        {
            var service = GetService(entityType);
            if (service == null) return;

            var updateMethod = service.GetType().GetMethod("UpdateDataAsync");
            if (updateMethod == null) return;

            var task = updateMethod.Invoke(service, new[] { entity }) as Task;
            if (task != null) await task.ConfigureAwait(false);
        }

        private async Task<string> RestoreFromSnapshotAsync(string entityType, string snapshotJson)
        {
            var service = GetService(entityType);
            if (service == null)
                return $"[错误] 未找到 {entityType} 服务";

            var getAllMethod = service.GetType().GetMethod("GetAllData");
            if (getAllMethod == null)
                return $"[错误] {entityType} 无 GetAllData 方法";

            var returnType = getAllMethod.ReturnType;
            var dataType = returnType.IsGenericType ? returnType.GetGenericArguments()[0] : typeof(object);
            var entity = JsonSerializer.Deserialize(snapshotJson, dataType);
            if (entity == null)
                return $"[错误] 快照反序列化失败";

            await UpdateEntityAsync(entityType, entity).ConfigureAwait(false);

            var name = (entity as IDataItem)?.Name ?? "(unknown)";
            return $"✓ 回滚 {EntityFieldMeta.GetEntityTypeDisplayName(entityType)}:「{name}」";
        }

        private async Task RefreshCachesAsync()
        {
            try
            {
                InvalidateIdToNameIndex();

                var relationService = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.RelationStrengthService>();
                relationService.InvalidateCache();

                var indexService = ServiceLocator.Get<TM.Services.Modules.ProjectData.Implementations.DataIndexService>();
                await indexService.InitializeAsync().ConfigureAwait(false);

                TM.App.Log("[DataEditPlugin] 缓存刷新完成");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[DataEditPlugin] 缓存刷新部分失败: {ex.Message}");
            }
        }

        private string BuildDiffLine(EntityChangeOperation op, string entityName, Dictionary<string, string> idToName)
        {
            var typeDisplay = EntityFieldMeta.GetEntityTypeDisplayName(op.EntityType);
            var header = $"[{typeDisplay}「{entityName}」]";

            switch (op.Op.ToLowerInvariant())
            {
                case "updatefield":
                    var field = op.Payload.TryGetProperty("field", out var f) ? f.GetString() : "?";
                    var fieldDisplay = EntityFieldMeta.GetFieldDisplayName(op.EntityType, field ?? "?");
                    var value = op.Payload.TryGetProperty("value", out var v) ? ResolveValueForDisplay(v.GetString(), idToName) :
                                op.Payload.TryGetProperty("append", out var a) ? $"+= {ResolveValueForDisplay(a.GetString(), idToName)}" : "?";
                    return $"  {header} {fieldDisplay} → {value}";
                case "rename":
                    var newName = op.Payload.TryGetProperty("newName", out var n) ? n.GetString() : "?";
                    return $"  {header} 重命名 → {newName}";
                case "delete":
                    return $"  {header} 删除";
                default:
                    return $"  {header} {op.Op}";
            }
        }

        private static readonly object _idIndexLock = new();
        private static Dictionary<string, string>? _cachedIdToNameIndex;
        private static DateTime _idIndexExpiresAt = DateTime.MinValue;
        private static readonly TimeSpan _idIndexTtl = TimeSpan.FromSeconds(5);

        private Dictionary<string, string> BuildIdToNameIndex()
        {
            lock (_idIndexLock)
            {
                if (_cachedIdToNameIndex != null && DateTime.Now < _idIndexExpiresAt)
                {
                    return _cachedIdToNameIndex;
                }
            }

            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entityType in EntityServiceMap.Keys)
            {
                var service = GetService(entityType);
                var getAllMethod = service?.GetType().GetMethod("GetAllData");
                if (getAllMethod?.Invoke(service, null) is not System.Collections.IList allData) continue;
                foreach (var item in allData)
                {
                    if (item is IDataItem di && !string.IsNullOrEmpty(di.Id))
                        index[di.Id] = di.Name;
                }
            }

            lock (_idIndexLock)
            {
                _cachedIdToNameIndex = index;
                _idIndexExpiresAt = DateTime.Now.Add(_idIndexTtl);
            }
            return index;
        }

        private static void InvalidateIdToNameIndex()
        {
            lock (_idIndexLock)
            {
                _cachedIdToNameIndex = null;
                _idIndexExpiresAt = DateTime.MinValue;
            }
        }

        private static string ResolveValueForDisplay(string? value, Dictionary<string, string> idToName)
        {
            if (string.IsNullOrWhiteSpace(value)) return value ?? string.Empty;
            if (idToName.TryGetValue(value, out var name)) return name;
            if (value.Contains(',') || value.Contains('、'))
            {
                var parts = value.Split(new[] { ',', '、' }, StringSplitOptions.RemoveEmptyEntries);
                var resolved = parts.Select(p =>
                {
                    var trimmed = p.Trim();
                    return idToName.TryGetValue(trimmed, out var n) ? n : trimmed;
                });
                return string.Join("、", resolved);
            }
            return value;
        }

        private static void SetTimestamp(object entity, bool setCreated = false)
        {
            if (entity is BusinessDataBase bdb)
            {
                bdb.UpdatedAt = DateTime.Now;
                if (setCreated) bdb.CreatedAt = DateTime.Now;
                return;
            }
            var type = entity.GetType();
            var updatedProp = type.GetProperty("ModifiedTime") ?? type.GetProperty("UpdatedAt");
            updatedProp?.SetValue(entity, DateTime.Now);
            if (setCreated)
            {
                var createdProp = type.GetProperty("CreatedTime") ?? type.GetProperty("CreatedAt");
                createdProp?.SetValue(entity, DateTime.Now);
            }
        }

        #endregion
    }
}
