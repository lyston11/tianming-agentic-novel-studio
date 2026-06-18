namespace TM.Web.NovelAgentWeb.Services.AgentTools;

public sealed class AgentToolProgressView
{
    public string ExecutionId { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string ResultLocation { get; set; } = string.Empty;
    public string? RunId { get; set; }
    public bool IsRunning { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public static class AgentToolProgressPresenter
{
    public static AgentToolProgressView Describe(AgentToolExecutionSnapshot snapshot)
    {
        var spec = ToolSpec(snapshot.ToolName);
        var isRunning = string.Equals(snapshot.Status, "running", StringComparison.OrdinalIgnoreCase);
        var isFailed = string.Equals(snapshot.Status, "failed", StringComparison.OrdinalIgnoreCase);
        var statusPrefix = isRunning ? "正在" : isFailed ? "未完成" : "已完成";
        var detail = BuildDetail(snapshot, spec, isRunning, isFailed);

        return new AgentToolProgressView
        {
            ExecutionId = snapshot.Id,
            ToolName = snapshot.ToolName,
            Status = snapshot.Status,
            Title = isRunning ? $"正在{spec.Action}" : $"{statusPrefix}{spec.Done}",
            Detail = detail,
            ResultLocation = spec.ResultLocation,
            RunId = snapshot.RunId,
            IsRunning = isRunning,
            StartedAt = snapshot.StartedAt,
            CompletedAt = snapshot.CompletedAt
        };
    }

    public static AgentToolProgressView DescribeRunning(
        string toolName,
        string status,
        string phase,
        string? runId = null,
        string executionId = "")
    {
        return Describe(new AgentToolExecutionSnapshot
        {
            Id = executionId,
            ToolName = toolName,
            Status = string.IsNullOrWhiteSpace(status) ? "running" : status,
            Phase = phase,
            RunId = runId,
            StartedAt = DateTime.UtcNow
        });
    }

    public static string DescribeAction(string toolName) => ToolSpec(toolName).Action;

    public static AgentToolProgressView DescribeHeartbeat(
        string toolName,
        TimeSpan elapsed,
        string phase,
        string? runId = null,
        string executionId = "")
    {
        var running = DescribeRunning(toolName, "running", phase, runId, executionId);
        running.Detail = BuildHeartbeatDetail(toolName, elapsed, running.ResultLocation);
        return running;
    }

    private static string BuildDetail(
        AgentToolExecutionSnapshot snapshot,
        ToolProgressSpec spec,
        bool isRunning,
        bool isFailed)
    {
        if (isRunning)
            return $"Agent 正在处理，过程结果会进入{spec.ProcessLocation}。";
        if (isFailed)
            return string.IsNullOrWhiteSpace(snapshot.ResultMessage)
                ? $"这一步没有完成，可在{spec.ProcessLocation}查看失败记录。"
                : snapshot.ResultMessage;
        if (IsInternalResultMessage(snapshot.ResultMessage))
            return $"结果已进入{spec.ResultLocation}。";
        if (!string.IsNullOrWhiteSpace(snapshot.ResultMessage) &&
            snapshot.ResultMessage.Contains(spec.ResultLocation, StringComparison.OrdinalIgnoreCase))
            return snapshot.ResultMessage;
        return string.IsNullOrWhiteSpace(snapshot.ResultMessage)
            ? $"结果已进入{spec.ResultLocation}。"
            : $"{snapshot.ResultMessage} 结果位置：{spec.ResultLocation}。";
    }

    private static bool IsInternalResultMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var tokens = new[]
        {
            "tool_search",
            "foundation_candidates",
            "volume_arc_candidates",
            "chapter_candidates",
            "mission_updated",
            "step_complete",
            "step_failed",
            "interrupt_received",
            "PlanStoryFoundation",
            "CommitStoryFoundation",
            "ProcessKnowledgeFile",
            "QueryWorkspaceState",
            "QueryProjectStatus",
            "SearchCreativeKnowledge",
            "ResolveNovelProject"
        };
        return tokens.Any(token => message.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildHeartbeatDetail(string toolName, TimeSpan elapsed, string resultLocation)
    {
        var elapsedText = elapsed.TotalSeconds < 90
            ? $"已执行约 {Math.Max(15, (int)Math.Round(elapsed.TotalSeconds))} 秒"
            : $"已执行约 {(int)Math.Round(elapsed.TotalSeconds / 60d)} 分钟";
        var stage = toolName switch
        {
            "GenerateChapterWithChanges" => "正在等待模型生成正文与修订记录",
            "RepairChapterDraft" => "正在按门禁失败项修订章节草稿",
            "CommitValidatedChapter" => "正在写入书城，正文入库后后台会继续刷新索引和记忆",
            _ => "正在执行工具"
        };
        var waiting = elapsed.TotalSeconds >= 90
            ? "仍在等待模型或后台处理，当前无新增产物。"
            : "如有阶段性产物，会在运行进度中更新。";
        return $"{elapsedText}，{stage}。{waiting} 结果位置：{resultLocation}。";
    }

    private static ToolProgressSpec ToolSpec(string toolName) =>
        toolName switch
        {
            "tool_search" => new("准备创作上下文", "创作上下文准备", "运行进度", "运行进度"),
            "ResolveNovelProject" => new("确认小说项目", "项目确认", "小说书城和当前会话", "小说书城"),
            "QueryWorkspaceState" => new("读取工作台状态", "工作台状态读取", "Agent 对话", "工作台状态"),
            "QueryProjectStatus" => new("读取项目工作流", "项目状态读取", "Agent 对话", "工作流"),
            "ProcessKnowledgeFile" => new("处理知识文件", "知识文件处理", "知识库", "知识库"),
            "SearchCreativeKnowledge" => new("检索创意知识库", "知识检索", "Agent 对话", "知识库"),
            "PlanStoryFoundation" => new("生成故事地基候选", "故事地基候选", "创作工作流", "工作流"),
            "CommitStoryFoundation" => new("确认故事地基", "故事地基确认", "创作工作流", "工作流"),
            "PlanVolumeArc" => new("规划分卷大纲", "分卷大纲", "创作工作流", "工作流"),
            "CommitVolumeArc" => new("确认分卷大纲", "分卷大纲确认", "创作工作流", "工作流"),
            "PlanChapter" => new("规划章节候选", "章节候选", "创作工作流", "工作流"),
            "BuildChapterContextPackage" => new("构建章节上下文包", "章节上下文包", "创作工作流", "工作流"),
            "GenerateChapterWithChanges" => new("生成章节草稿", "章节草稿", "创作工作流", "工作流"),
            "ValidateChapterDraft" => new("校验章节草稿", "章节校验", "创作工作流", "工作流"),
            "RepairChapterDraft" => new("修复章节草稿", "章节修复", "创作工作流", "工作流"),
            "ReviewChapter" => new("评审章节质量", "章节评审", "创作工作流", "工作流"),
            "CommitValidatedChapter" => new("提交已校验章节", "章节提交", "小说书城", "小说书城"),
            _ => new("执行工具", "工具执行", "Agent 运行态", "工作流")
        };

    private sealed record ToolProgressSpec(
        string Action,
        string Done,
        string ResultLocation,
        string ProcessLocation);
}
