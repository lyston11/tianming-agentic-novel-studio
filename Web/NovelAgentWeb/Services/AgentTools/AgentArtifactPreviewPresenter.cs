using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.AgentTools;

public sealed class AgentArtifactPreviewView
{
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string ResultLocation { get; set; } = string.Empty;
    public string? RunId { get; set; }
    public IReadOnlyList<string> Items { get; set; } = Array.Empty<string>();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class AgentArtifactPreviewPresenter
{
    public static AgentArtifactPreviewView? Build(string toolName, AgentToolExecutionResult result)
    {
        if (!result.Success || result.Artifact == null)
            return null;
        if (IsToolDiscoveryArtifact(toolName, result.Artifact))
            return null;
        if (IsReadOnlyStateSnapshotArtifact(result.Artifact))
            return BuildReadOnlySnapshotPreview(result);

        return result.Data switch
        {
            NovelAgentRun run when run.MacroCandidates.Count > 0 => BuildStoryFoundationPreview(run, result),
            NovelAgentRun run when run.VolumeArcPlan != null => BuildVolumePreview(run, result),
            NovelAgentRun run when run.ChapterBrief?.Candidates.Count > 0 => BuildChapterCandidatesPreview(run, result),
            _ => BuildGenericPreview(result)
        };
    }

    private static AgentArtifactPreviewView BuildStoryFoundationPreview(NovelAgentRun run, AgentToolExecutionResult result)
    {
        var items = run.MacroCandidates
            .Take(4)
            .Select((candidate, index) => $"【{index + 1}】{Clean(candidate.Title)}：{Clean(candidate.CoreHook, 120)}")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        return new AgentArtifactPreviewView
        {
            Title = "故事地基候选已生成",
            Summary = $"已生成 {run.MacroCandidates.Count} 个候选，先给你预览标题和核心钩子；完整内容会继续整理到聊天记录和工作流。",
            ResultLocation = "创作工作流",
            RunId = result.RunId ?? run.RunId,
            Items = items
        };
    }

    private static AgentArtifactPreviewView BuildVolumePreview(NovelAgentRun run, AgentToolExecutionResult result)
    {
        var plan = run.VolumeArcPlan!;
        return new AgentArtifactPreviewView
        {
            Title = "分卷规划已生成",
            Summary = Clean($"《{plan.Title}》：{plan.VolumePromise}", 160),
            ResultLocation = "创作工作流",
            RunId = result.RunId ?? run.RunId,
            Items = new[]
            {
                $"核心问题：{Clean(plan.CoreQuestion, 100)}",
                $"中点变化：{Clean(plan.MidpointReversal, 100)}",
                $"高潮：{Clean(plan.Climax, 100)}"
            }.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
        };
    }

    private static AgentArtifactPreviewView BuildChapterCandidatesPreview(NovelAgentRun run, AgentToolExecutionResult result)
    {
        var brief = run.ChapterBrief!;
        var items = brief.Candidates
            .Take(4)
            .Select((candidate, index) => $"【{index + 1}】{Clean(candidate.Title)}：{Clean(candidate.CoreTwist, 120)}")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        return new AgentArtifactPreviewView
        {
            Title = "章节候选已生成",
            Summary = $"已为 {brief.ChapterId} 生成 {brief.Candidates.Count} 个剧情候选。",
            ResultLocation = "创作工作流",
            RunId = result.RunId ?? run.RunId,
            Items = items
        };
    }

    private static AgentArtifactPreviewView? BuildGenericPreview(AgentToolExecutionResult result)
    {
        var artifact = result.Artifact;
        if (artifact == null)
            return null;

        var summary = Clean(artifact.Summary, 180);
        if (string.IsNullOrWhiteSpace(summary))
            summary = Clean(result.Message, 180);

        return new AgentArtifactPreviewView
        {
            Title = string.IsNullOrWhiteSpace(artifact.UserVisibleStatus) ? "阶段性产物已生成" : Clean(artifact.UserVisibleStatus),
            Summary = summary,
            ResultLocation = artifact.VisibleInLibrary ? "小说书城" : "创作工作流",
            RunId = result.RunId ?? artifact.RunId,
            Items = artifact.NextHints.Select(x => Clean(x, 120)).Where(x => !string.IsNullOrWhiteSpace(x)).Take(4).ToArray()
        };
    }

    private static bool IsToolDiscoveryArtifact(string toolName, AgentToolArtifact artifact) =>
        string.Equals(toolName, "tool_search", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(artifact.ArtifactType, "tool_search_result", StringComparison.OrdinalIgnoreCase);

    private static bool IsReadOnlyStateSnapshotArtifact(AgentToolArtifact artifact) =>
        artifact.ArtifactType is "workspace_state" or "project_status" or "knowledge_search_result" or "knowledge_hits" or "workflow_state";

    private static AgentArtifactPreviewView BuildReadOnlySnapshotPreview(AgentToolExecutionResult result)
    {
        var artifact = result.Artifact!;
        var title = artifact.ArtifactType switch
        {
            "workspace_state" => "工作台状态已读取",
            "project_status" => "项目状态已读取",
            "knowledge_search_result" or "knowledge_hits" => "知识库检索已完成",
            "workflow_state" => "工作流状态已读取",
            _ => "状态已读取"
        };

        return new AgentArtifactPreviewView
        {
            Title = title,
            Summary = Clean(artifact.Summary, 120),
            ResultLocation = "Agent 对话",
            RunId = result.RunId ?? artifact.RunId,
            Items = Array.Empty<string>()
        };
    }

    private static string Clean(string? value, int maxLength = 80)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var clean = value
            .Replace("tool_search", "", StringComparison.OrdinalIgnoreCase)
            .Replace("PlanStoryFoundation", "", StringComparison.OrdinalIgnoreCase)
            .Replace("foundation_candidates", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return clean.Length <= maxLength ? clean : clean[..maxLength].TrimEnd() + "...";
    }
}
