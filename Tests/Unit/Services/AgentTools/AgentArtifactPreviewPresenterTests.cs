using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Services.AgentTools;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Services.AgentTools;

public class AgentArtifactPreviewPresenterTests
{
    [Fact]
    public void Build_ReturnsStoryFoundationPreviewWithoutInternalTokens()
    {
        var run = new NovelAgentRun
        {
            RunId = "run-preview-001",
            MacroCandidates =
            {
                new MacroStoryConceptCandidate
                {
                    Title = "废土维修王",
                    CoreHook = "底层机修工从垃圾场拆出旧机甲核心，靠维修、改装和打怪升级一路冲向星海战场。"
                },
                new MacroStoryConceptCandidate
                {
                    Title = "钢铁拾荒者",
                    CoreHook = "主角把每一次战斗残骸都变成下一次碾压强敌的升级素材。"
                }
            }
        };

        var preview = AgentArtifactPreviewPresenter.Build("PlanStoryFoundation", new AgentToolExecutionResult
        {
            Success = true,
            Phase = "foundation_candidates",
            Message = "PlanStoryFoundation foundation_candidates tool_search",
            RunId = run.RunId,
            Data = run,
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "foundation_candidates",
                Summary = "PlanStoryFoundation 已生成 foundation_candidates"
            }
        });

        Assert.NotNull(preview);
        Assert.Contains("故事地基候选", preview!.Title);
        Assert.Contains("废土维修王", string.Join("\n", preview.Items));
        Assert.Contains("旧机甲核心", string.Join("\n", preview.Items));

        var visibleText = $"{preview.Title}\n{preview.Summary}\n{preview.ResultLocation}\n{string.Join("\n", preview.Items)}";
        Assert.DoesNotContain("PlanStoryFoundation", visibleText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("foundation_candidates", visibleText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tool_search", visibleText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_DoesNotCreatePreviewForToolDiscoveryArtifacts()
    {
        var preview = AgentArtifactPreviewPresenter.Build("tool_search", new AgentToolExecutionResult
        {
            Success = true,
            Phase = "tool_search",
            Message = "tool_search_result",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "tool_search_result",
                UserVisibleStatus = "tool_search_result",
                Summary = "已准备 12 项可用创作能力。"
            }
        });

        Assert.Null(preview);
    }

    [Fact]
    public void Build_CompactsReadOnlyWorkspaceSnapshotPreview()
    {
        var preview = AgentArtifactPreviewPresenter.Build("QueryWorkspaceState", new AgentToolExecutionResult
        {
            Success = true,
            Message = "小说书城当前可见项目共 25 本；本次快照列出最近 20 本。",
            Artifact = new AgentToolArtifact
            {
                ArtifactType = "workspace_state",
                Summary = "已读取小说书城、知识库和创作工作流状态；小说书城当前可见项目共 25 本。",
                NextHints = new[] { "项目一 (project-1)", "项目二 (project-2)" }
            }
        });

        Assert.NotNull(preview);
        Assert.Equal("工作台状态已读取", preview!.Title);
        Assert.Equal("Agent 对话", preview.ResultLocation);
        Assert.Empty(preview.Items);
        Assert.DoesNotContain("项目一", $"{preview.Summary}\n{string.Join("\n", preview.Items)}");
    }
}
