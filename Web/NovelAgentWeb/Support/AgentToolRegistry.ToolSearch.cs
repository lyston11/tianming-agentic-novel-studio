using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using TM.Web.NovelAgentWeb.Services.AgentTools;

namespace TM.Web.NovelAgentWeb.Support;

public sealed partial class AgentToolRegistry
{
    private async Task<AgentToolExecutionResult> ToolSearchAsync(AgentToolCall call, AgentSession session, CancellationToken ct)
    {
        var phaseArg = NormalizeToolSearchPhase(Arg(call, "phase", session.Phase));
        var query = FirstNonEmpty(Arg(call, "query"), Arg(call, "intent"), Arg(call, "context"), session.WorkingMemory.CurrentGoal);
        var intent = Arg(call, "intent");
        var context = Arg(call, "context");
        var includeAll = ArgBool(call, "includeAll");
        var requestedLimit = ArgInt(call, "limit", 12);
        var limit = includeAll
            ? int.MaxValue
            : Math.Clamp(requestedLimit <= 0 ? 12 : requestedLimit, 1, 50);

        var rankedTools = SearchToolDefinitions(query, intent, context, phaseArg, includeAll, limit);
        var toolNames = rankedTools.Select(def => def.Name).ToArray();
        var matches = rankedTools
            .Select(def => new ToolSearchMatch(
                def.Name,
                def.Semantic.DisplayName,
                BuildToolRelevanceReason(def, query, intent, context)))
            .ToArray();

        var toolList = toolNames
            .Select(name => _entries.TryGetValue(name, out var entry) ? entry.Definition : null)
            .Where(def => def != null)
            .Select(def => $"• {def!.Name} / {def.Semantic.DisplayName}（{def.Description}；空间={def.Semantic.DomainSurface}；产物={def.Semantic.OutputKind}；需要项目={def.Semantic.RequiresProject}；无项目可用={def.Semantic.SupportsNoProjectSession}；耗时={def.Semantic.AverageDuration}；可见位置={def.Semantic.UserVisibleWhere}；可搭配={string.Join("/", def.Semantic.NextPossibleTools.Take(4))}；非限制，模型仍应按当前目标自主选择工具）")
            .ToList();

        var message = toolList.Count == 0
            ? "已读取当前创作上下文，正在重新判断下一步。"
            : $"已准备 {toolList.Count} 项可用创作能力，正在选择最适合当前目标的下一步。";

        var discoveredTools = rankedTools
            .Select(def => new ToolSchema
            {
                Name = def.Name,
                Description = def.Description,
                Risk = def.Risk,
                RequiresConfirmation = def.RequiresConfirmation,
                Parameters = def.Arguments?.ToDictionary(p => p, _ => "string") ?? new Dictionary<string, string>(),
                SideEffects = def.SideEffects,
                Semantic = def.Semantic
            })
            .ToList();

        using var scope = _serviceProvider.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IToolSearchCacheService>();
        var searchScope = BuildToolSearchScopeKey(query, intent, context, phaseArg, includeAll);
        await cache.SaveAsync(
                session,
                searchScope,
                discoveredTools,
                ToolCatalogSignature.Compute(ListToolSchemas()),
                ct)
            .ConfigureAwait(false);

        return new AgentToolExecutionResult
        {
            Success = true,
            Message = message,
            Phase = session.Phase,
            Data = new { Query = query, PhaseHint = phaseArg, ScopeKey = searchScope, Tools = toolNames, Matches = matches, ToolDetails = toolList },
            Artifact = BuildArtifact("tool_search_result", searchScope, session.ActiveProjectId ?? string.Empty, string.Empty, $"已准备 {toolList.Count} 项可用创作能力。", Array.Empty<string>()),
            Suggestions = Array.Empty<string>(),
        };
    }

    private static string BuildToolRelevanceReason(AgentToolDefinition tool, string query, string intent, string context)
    {
        var related = new List<string>();
        var searchText = string.Join(' ', new[] { query, intent, context }.Where(x => !string.IsNullOrWhiteSpace(x)));

        AddIfMatches(related, searchText, tool.Semantic.DomainSurface, $"相关空间：{tool.Semantic.DomainSurface}");
        AddIfMatches(related, searchText, tool.Semantic.OutputKind, $"相关产物：{tool.Semantic.OutputKind}");
        AddIfMatches(related, searchText, tool.Description, "工具描述与当前请求相关");
        AddIfMatches(related, searchText, string.Join(' ', tool.Semantic.ReadsFrom), $"读取：{string.Join("/", tool.Semantic.ReadsFrom.Take(3))}");
        AddIfMatches(related, searchText, string.Join(' ', tool.Semantic.ProgressEventContract), "进度事件可解释当前阶段");
        AddIfMatches(related, searchText, string.Join(' ', tool.Semantic.NextPossibleTools), $"可搭配能力：{string.Join("/", tool.Semantic.NextPossibleTools.Take(3))}");

        if (related.Count == 0)
        {
            related.Add(tool.Semantic.RequiresProject
                ? "该工具属于项目上下文能力，模型需确认当前任务是否需要项目状态。"
                : "该工具支持无项目会话，适合先读取或发现上下文。");
        }

        return string.Join("；", related.Distinct(StringComparer.OrdinalIgnoreCase).Take(3));
    }

    private static void AddIfMatches(List<string> reasons, string searchText, string haystack, string reason)
    {
        if (string.IsNullOrWhiteSpace(searchText) || string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(reason))
            return;

        var terms = TokenizeToolSearchText(searchText);
        if (terms.Count == 0 ||
            terms.Any(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
            haystack.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
            searchText.Contains(haystack, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add(reason);
        }
    }

    private sealed record ToolSearchMatch(string Tool, string DisplayName, string RelevanceReason);

    private IReadOnlyList<AgentToolDefinition> SearchToolDefinitions(
        string query,
        string intent,
        string context,
        string phaseHint,
        bool includeAll,
        int limit)
    {
        var phase = phaseHint switch
        {
            "Planning" => ConversationPhase.Planning,
            "Creation" => ConversationPhase.Creation,
            "Review" => ConversationPhase.Review,
            _ => ConversationPhase.Conversation,
        };
        var searchText = string.Join(' ', new[] { query, intent, context }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var terms = TokenizeToolSearchText(searchText);

        return _entries.Values
            .Where(entry => !string.Equals(entry.Definition.Name, "tool_search", StringComparison.OrdinalIgnoreCase))
            .Select((entry, index) => new
            {
                Definition = entry.Definition,
                Index = index,
                Score = includeAll ? 0 : ScoreTool(entry.Definition, searchText, terms),
                HintRank = CategoryHintRank(entry.Category, phase)
            })
            .Where(x => includeAll || terms.Count == 0 || x.Score > 0 || x.HintRank >= 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.HintRank < 0 ? int.MaxValue : x.HintRank)
            .ThenBy(x => x.Index)
            .Take(limit)
            .Select(x => x.Definition)
            .ToList();
    }

    private static int ScoreTool(AgentToolDefinition tool, string searchText, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
            return 0;

        var haystack = string.Join(' ', new[]
        {
            tool.Name,
            tool.Semantic.DisplayName,
            tool.Description,
            tool.Risk,
            tool.Semantic.DomainSurface,
            tool.Semantic.OutputKind,
            tool.Semantic.UserVisibleWhere,
            tool.Semantic.ResultSemantics,
            tool.Semantic.AverageDuration,
            tool.Semantic.ImpactScope,
            tool.Semantic.FailureContract,
            string.Join(' ', tool.Arguments),
            string.Join(' ', tool.Semantic.ProgressEventContract),
            string.Join(' ', tool.Semantic.NextPossibleTools),
            string.Join(' ', tool.Semantic.ReadsFrom),
            string.Join(' ', tool.Semantic.WritesTo),
        }).ToLowerInvariant();

        var score = 0;
        foreach (var term in terms)
        {
            if (haystack.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += tool.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ? 6 : 2;
        }
        foreach (var alias in BuildToolSearchAliases(tool.Name))
        {
            if (searchText.Contains(alias, StringComparison.OrdinalIgnoreCase))
                score += 6;
        }
        return score;
    }

    private static IReadOnlyList<string> BuildToolSearchAliases(string toolName) =>
        toolName switch
        {
            "ProduceChapter" => new[] { "继续写", "写第", "写章", "写正文", "生成正文", "生产章节", "提交书城" },
            "SearchCreativeKnowledge" => new[] { "知识库", "知识", "素材", "设定" },
            "QueryProjectContent" => new[] { "已有正文", "查看正文", "读取正文", "第几章", "所属卷" },
            "QueryNovelProductionState" => new[] { "执行到哪", "卡在哪", "生产阶段", "后台进度" },
            "CreateCreativeIntent" => new[] { "新想法", "创意", "补充要求", "改方向" },
            "CreateRevisionPlan" => new[] { "重写", "修订计划", "改写", "返工" },
            _ => Array.Empty<string>()
        };

    private static IReadOnlyList<string> TokenizeToolSearchText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        return text
            .Split(new[] { ' ', '\t', '\r', '\n', ',', '.', ';', ':', '，', '。', '；', '：', '、', '(', ')', '（', '）', '[', ']', '【', '】', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToList();
    }

    private static int CategoryHintRank(string category, ConversationPhase phase)
    {
        return phase switch
        {
            ConversationPhase.Planning => category switch
            {
                "planning" => 0,
                "project" => 1,
                "content" or "knowledge" or "rag" => 2,
                "blackboard" or "workspace" => 3,
                "commit" => 4,
                _ => 10,
            },
            ConversationPhase.Creation => category switch
            {
                "writing" => 0,
                "gate" => 1,
                "planning" => 2,
                "content" or "blackboard" or "workspace" => 3,
                _ => 10,
            },
            ConversationPhase.Review => category switch
            {
                "review" or "gate" => 0,
                "commit" => 1,
                "maintenance" => 2,
                "content" or "blackboard" or "workspace" => 3,
                _ => 10,
            },
            _ => category switch
            {
                "workspace" or "blackboard" => 0,
                "project" or "content" => 1,
                "knowledge" or "rag" => 2,
                _ => 10,
            },
        };
    }

    private static string BuildToolSearchScopeKey(string query, string intent, string context, string phaseHint, bool includeAll)
    {
        var normalized = string.Join("|", new[]
        {
            $"phaseHint={phaseHint}",
            $"includeAll={includeAll}",
            $"query={query}",
            $"intent={intent}",
            $"context={context}",
        }).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return "global";
        var hash = ComputeStableHash(normalized);
        return $"global:{phaseHint}:{hash}";
    }

    private static string ComputeStableHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    private static string NormalizeToolSearchPhase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Conversation";

        return value.Trim().ToLowerInvariant() switch
        {
            "planning" => "Planning",
            "creation" => "Creation",
            "review" => "Review",
            "all" => "All",
            "conversation" => "Conversation",
            _ => "Conversation"
        };
    }
}
