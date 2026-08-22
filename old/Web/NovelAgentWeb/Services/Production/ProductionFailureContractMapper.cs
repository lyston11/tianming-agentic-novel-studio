using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Production;

internal sealed record ProductionFailureContract(
    string Code,
    string Stage,
    string Message,
    bool Recoverable,
    string RecommendedAction,
    IReadOnlyList<string> ArtifactIds,
    bool RequiresUserDecision);

internal static class ProductionFailureContractMapper
{
    public static ProductionFailureContract? FromEvent(ProductionEvent evt)
    {
        if (!IsFailureStatus(evt.Status))
            return null;

        var data = ParseJsonObject(evt.DataJson);
        var code = FirstNonEmpty(
            GetJsonString(data, "code"),
            GetJsonString(data, "failureCode"),
            InferFailureCode(evt));
        var stage = FirstNonEmpty(
            GetJsonString(data, "stage"),
            GetJsonString(data, "failedStage"),
            InferOutboxStage(data),
            evt.Stage);
        var message = FirstNonEmpty(
            GetJsonString(data, "message"),
            GetJsonString(data, "error"),
            GetJsonString(data, "reason"),
            evt.Message);
        var recoverable = GetJsonBool(data, "recoverable") ??
                          string.Equals(evt.Status, "retryable_failed", StringComparison.OrdinalIgnoreCase);
        var recommendedAction = FirstNonEmpty(
            GetJsonString(data, "recommendedAction"),
            InferRecommendedAction(evt, recoverable));
        var artifactIds = GetJsonStringArray(data, "artifactIds")
            .Concat(new[] { evt.ArtifactId ?? string.Empty, evt.ChapterId ?? string.Empty, evt.PackageId ?? string.Empty })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        var requiresUserDecision = GetJsonBool(data, "requiresUserDecision") ?? InferRequiresUserDecision(evt);

        return new ProductionFailureContract(
            code,
            stage,
            message,
            recoverable,
            recommendedAction,
            artifactIds,
            requiresUserDecision);
    }

    private static bool IsFailureStatus(string status) =>
        status.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
        status.Contains("blocked", StringComparison.OrdinalIgnoreCase) ||
        status.Contains("invalid", StringComparison.OrdinalIgnoreCase);

    private static string InferFailureCode(ProductionEvent evt)
    {
        var data = ParseJsonObject(evt.DataJson);
        var outboxEventType = GetJsonString(data, "eventType");
        if (string.Equals(outboxEventType, "extract_chapter_continuity_facts", StringComparison.OrdinalIgnoreCase))
            return "CHAPTER_FACT_EXTRACTION_FAILED";

        if (evt.EventType.StartsWith("outbox_", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(evt.Stage, "index_outbox", StringComparison.OrdinalIgnoreCase))
        {
            return "INDEX_FAILED";
        }

        if (string.Equals(evt.Stage, NovelAgentProductionStages.GateValidation, StringComparison.OrdinalIgnoreCase) ||
            evt.EventType.Contains("gate", StringComparison.OrdinalIgnoreCase))
        {
            return "GATE_FAILED";
        }

        if (string.Equals(evt.Stage, NovelAgentProductionStages.QualityReview, StringComparison.OrdinalIgnoreCase) ||
            evt.EventType.Contains("review", StringComparison.OrdinalIgnoreCase) ||
            evt.EventType.Contains("quality", StringComparison.OrdinalIgnoreCase))
        {
            return "AGENT_REVIEW_FAILED";
        }

        return "PRODUCTION_FAILED";
    }

    private static string InferRecommendedAction(ProductionEvent evt, bool recoverable)
    {
        var data = ParseJsonObject(evt.DataJson);
        var outboxEventType = GetJsonString(data, "eventType");
        if (string.Equals(outboxEventType, "extract_chapter_continuity_facts", StringComparison.OrdinalIgnoreCase))
        {
            return recoverable
                ? "章节正文已入书城；等待后台事实沉淀重试，或重试该 outbox 事件。若持续失败，检查模型配置、章节版本映射和 Story Bible 写入。"
                : "章节正文已入书城；检查事实沉淀失败原因后重新触发章节事实抽取。";
        }

        if (evt.EventType.StartsWith("outbox_", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(evt.Stage, "index_outbox", StringComparison.OrdinalIgnoreCase))
        {
            return recoverable
                ? "等待后台重试，或在索引管理中重试 outbox 事件。"
                : "检查索引失败原因后重新构建相关索引。";
        }

        if (string.Equals(evt.Stage, NovelAgentProductionStages.GateValidation, StringComparison.OrdinalIgnoreCase) ||
            evt.EventType.Contains("gate", StringComparison.OrdinalIgnoreCase))
        {
            return "读取门禁报告并重建章节生产包或修订草稿。";
        }

        if (string.Equals(evt.Stage, NovelAgentProductionStages.QualityReview, StringComparison.OrdinalIgnoreCase) ||
            evt.EventType.Contains("review", StringComparison.OrdinalIgnoreCase) ||
            evt.EventType.Contains("quality", StringComparison.OrdinalIgnoreCase))
        {
            return "读取 Agent 总编验收意见并修订章节。";
        }

        return recoverable ? "查询生产状态后继续可恢复动作。" : "查询失败详情后决定是否重试或回滚。";
    }

    private static bool InferRequiresUserDecision(ProductionEvent evt) =>
        evt.EventType.Contains("review", StringComparison.OrdinalIgnoreCase) ||
        evt.EventType.Contains("quality", StringComparison.OrdinalIgnoreCase);

    private static string InferOutboxStage(IReadOnlyDictionary<string, JsonElement> data)
    {
        var outboxEventType = GetJsonString(data, "eventType");
        return string.Equals(outboxEventType, "extract_chapter_continuity_facts", StringComparison.OrdinalIgnoreCase)
            ? "post_commit_facts"
            : string.Empty;
    }

    private static IReadOnlyDictionary<string, JsonElement> ParseJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

            return document.RootElement
                .EnumerateObject()
                .ToDictionary(
                    property => property.Name,
                    property => property.Value.Clone(),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string GetJsonString(IReadOnlyDictionary<string, JsonElement> item, string propertyName)
    {
        if (!item.TryGetValue(propertyName, out var property))
            return string.Empty;

        return property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static bool? GetJsonBool(IReadOnlyDictionary<string, JsonElement> item, string propertyName)
    {
        if (!item.TryGetValue(propertyName, out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static IReadOnlyList<string> GetJsonStringArray(IReadOnlyDictionary<string, JsonElement> item, string propertyName)
    {
        if (!item.TryGetValue(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return property.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString() ?? string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}
