using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Support;

/// <summary>
/// Partial class for AgentToolRegistry - DesignRules and ChapterBlueprint tools.
/// </summary>
public sealed partial class AgentToolRegistry
{
    private async Task<AgentToolExecutionResult> AggregateDesignRulesAsync(
        AgentToolCall call,
        AgentSession session,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "当前会话尚未绑定小说项目，无法聚合设计规则。",
                Phase = session.Phase,
                Suggestions = new[] { "先绑定或创建小说项目", "查询工作台状态" }
            };
        }

        using var scope = _serviceProvider.CreateScope();
        var designRuleService = scope.ServiceProvider
            .GetRequiredService<TM.Web.NovelAgentWeb.Services.DesignRules.IDesignRuleAggregationService>();

        var rules = await designRuleService
            .AggregateFromKnowledgeAsync(session.UserId, session.ActiveProjectId, ct)
            .ConfigureAwait(false);

        var message = rules.Count == 0
            ? "当前项目没有已分类的知识，无法聚合设计规则。可以先对知识进行分类（ClassifyProjectKnowledge）。"
            : $"已聚合 {rules.Count} 条设计规则到项目 {session.ActiveProjectId}：\n" +
              string.Join("\n", rules.GroupBy(r => r.RuleType)
                  .Select(g => $"- {g.Key}: {g.Count()} 条"));

        return new AgentToolExecutionResult
        {
            Success = true,
            Message = message,
            RunId = session.ActiveRunId ?? string.Empty,
            Phase = session.Phase,
            Data = new { rules = rules.Select(r => new { r.Id, r.RuleType, r.ConstraintLevel, r.Priority, r.Version }) },
            Artifact = BuildArtifact("design_rules_aggregated", session.ActiveProjectId, session.ActiveProjectId,
                session.ActiveRunId ?? string.Empty, message, new[] { "查询章节蓝图", "构建章节生产包" }),
            Suggestions = new[] { "查询章节蓝图", "构建章节生产包", "继续规划章节" }
        };
    }

    private async Task<AgentToolExecutionResult> CreateChapterBlueprintAsync(
        AgentToolCall call,
        AgentSession session,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "当前会话尚未绑定小说项目，无法创建章节蓝图。",
                Phase = session.Phase,
                Suggestions = new[] { "先绑定或创建小说项目", "查询工作台状态" }
            };
        }

        var chapterId = Arg(call, "chapterId");
        var chapterIndexStr = Arg(call, "chapterIndex");
        var title = Arg(call, "title");
        var intent = Arg(call, "intent");

        if (string.IsNullOrWhiteSpace(chapterId) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(intent))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "缺少必填字段：chapterId、title、intent。",
                Phase = session.Phase,
                Suggestions = new[] { "补充章节 ID、标题和意图", "先规划章节" }
            };
        }

        if (!int.TryParse(chapterIndexStr, out var chapterIndex))
            chapterIndex = 0;

        var keyEventsJson = Arg(call, "keyEvents", "[]");
        var charactersJson = Arg(call, "characters", "[]");
        var requiredKnowledgeIdsJson = Arg(call, "requiredKnowledgeIds", "[]");
        var appliedDesignRuleIdsJson = Arg(call, "appliedDesignRuleIds", "[]");
        var dependencyChapterIdsJson = Arg(call, "dependencyChapterIds", "[]");

        var keyEvents = TryDeserializeStringList(keyEventsJson);
        var characters = TryDeserializeStringList(charactersJson);
        var requiredKnowledgeIds = TryDeserializeStringList(requiredKnowledgeIdsJson);
        var appliedDesignRuleIds = TryDeserializeStringList(appliedDesignRuleIdsJson);
        var dependencyChapterIds = TryDeserializeStringList(dependencyChapterIdsJson);

        var conflictNote = Arg(call, "conflictNote");
        var endingNote = Arg(call, "endingNote");
        var targetWordCountStr = Arg(call, "targetWordCount");
        int? targetWordCount = int.TryParse(targetWordCountStr, out var wc) ? wc : null;

        using var scope = _serviceProvider.CreateScope();
        var blueprintService = scope.ServiceProvider
            .GetRequiredService<TM.Web.NovelAgentWeb.Services.ChapterBlueprints.IChapterBlueprintService>();

        var request = new TM.Web.NovelAgentWeb.Services.ChapterBlueprints.ChapterBlueprintCreateRequest(
            UserId: session.UserId,
            ProjectId: session.ActiveProjectId,
            ChapterId: chapterId,
            ChapterIndex: chapterIndex,
            Title: title,
            Intent: intent,
            KeyEvents: keyEvents,
            Characters: characters,
            ConflictNote: conflictNote,
            EndingNote: endingNote,
            RequiredKnowledgeIds: requiredKnowledgeIds,
            AppliedDesignRuleIds: appliedDesignRuleIds,
            DependencyChapterIds: dependencyChapterIds,
            TargetWordCount: targetWordCount);

        var blueprint = await blueprintService.CreateOrUpdateAsync(request, ct).ConfigureAwait(false);

        var message = $"已为章节 {chapterId} 创建蓝图 v{blueprint.Version}：\n" +
                      $"标题：{blueprint.Title}\n" +
                      $"意图：{blueprint.Intent}\n" +
                      $"关键事件：{blueprint.KeyEventsJson}\n" +
                      $"人物：{blueprint.CharactersJson}\n" +
                      $"依赖章节：{blueprint.DependencyChapterIdsJson}";

        return new AgentToolExecutionResult
        {
            Success = true,
            Message = message,
            RunId = session.ActiveRunId ?? string.Empty,
            Phase = session.Phase,
            Data = new { blueprintId = blueprint.Id, version = blueprint.Version, chapterId = blueprint.ChapterId },
            Artifact = BuildArtifact("chapter_blueprint_created", chapterId, session.ActiveProjectId,
                session.ActiveRunId ?? string.Empty, message, new[] { "聚合设计规则", "构建章节生产包" }),
            Suggestions = new[] { "聚合设计规则", "构建章节生产包", "继续规划章节" }
        };
    }

    private async Task<AgentToolExecutionResult> QueryChapterBlueprintsAsync(
        AgentToolCall call,
        AgentSession session,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) || string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return new AgentToolExecutionResult
            {
                Success = false,
                Message = "当前会话尚未绑定小说项目，无法查询章节蓝图。",
                Phase = session.Phase,
                Suggestions = new[] { "先绑定或创建小说项目", "查询工作台状态" }
            };
        }

        var chapterId = Arg(call, "chapterId");
        var includeHistoryStr = Arg(call, "includeHistory", "false");
        var includeHistory = string.Equals(includeHistoryStr, "true", StringComparison.OrdinalIgnoreCase);

        using var scope = _serviceProvider.CreateScope();
        var blueprintService = scope.ServiceProvider
            .GetRequiredService<TM.Web.NovelAgentWeb.Services.ChapterBlueprints.IChapterBlueprintService>();

        IReadOnlyList<TM.Web.NovelAgentWeb.Data.Entities.ChapterBlueprint> blueprints;

        if (!string.IsNullOrWhiteSpace(chapterId) && includeHistory)
        {
            blueprints = await blueprintService
                .GetBlueprintHistoryAsync(session.UserId, session.ActiveProjectId, chapterId, ct)
                .ConfigureAwait(false);
        }
        else if (!string.IsNullOrWhiteSpace(chapterId))
        {
            var single = await blueprintService
                .GetActiveBlueprintAsync(session.UserId, session.ActiveProjectId, chapterId, ct)
                .ConfigureAwait(false);
            blueprints = single != null ? new[] { single } : Array.Empty<TM.Web.NovelAgentWeb.Data.Entities.ChapterBlueprint>();
        }
        else
        {
            blueprints = await blueprintService
                .GetProjectBlueprintsAsync(session.UserId, session.ActiveProjectId, ct)
                .ConfigureAwait(false);
        }

        var message = blueprints.Count == 0
            ? "当前项目没有章节蓝图。"
            : $"当前项目有 {blueprints.Count} 个章节蓝图：\n" +
              string.Join("\n", blueprints.Select(b =>
                  $"- 第{b.ChapterIndex}章 ({b.ChapterId}) v{b.Version}: {b.Title} [{b.Status}]"));

        return new AgentToolExecutionResult
        {
            Success = true,
            Message = message,
            RunId = session.ActiveRunId ?? string.Empty,
            Phase = session.Phase,
            Data = new { blueprints = blueprints.Select(b => new { b.Id, b.ChapterId, b.ChapterIndex, b.Title, b.Intent, b.Version, b.Status }) },
            Artifact = BuildArtifact("chapter_blueprints_query", session.ActiveProjectId, session.ActiveProjectId,
                session.ActiveRunId ?? string.Empty, message, new[] { "创建章节蓝图", "构建章节生产包" }),
            Suggestions = new[] { "创建章节蓝图", "聚合设计规则", "继续规划章节" }
        };
    }

    private static List<string> TryDeserializeStringList(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }
}
