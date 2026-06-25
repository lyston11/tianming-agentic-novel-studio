using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Support;

public class AgentRecoveryEngineTests
{
    [Fact]
    public void AnalyzeFailure_DoesNotRetryTerminalProduceChapterGateFailure()
    {
        var recovery = new AgentRecoveryEngine(new object(), new object());

        var analysis = recovery.AnalyzeFailure(
            new AgentToolCall
            {
                Name = "ProduceChapter",
                Arguments = new Dictionary<string, string>
                {
                    ["runId"] = "run-003",
                    ["commitPolicy"] = "auto_commit"
                }
            },
            new AgentToolExecutionResult
            {
                Success = false,
                IsRepairable = false,
                Phase = "gate_failed",
                Message = "章节生产闭环在 校验/修复章节草稿 阶段停止。CHANGES JSON 不是可解析对象。",
                Failure = new AgentToolFailure
                {
                    FailedStage = "gate_validation_or_repair",
                    Reason = "章节草稿修复后仍未通过硬门禁：CHANGES JSON 不是可解析对象。",
                    RequiresUserDecision = true,
                    Recoverable = false
                }
            },
            new AgentSession
            {
                ActiveRunId = "run-003"
            },
            new StoryBibleDocument());

        Assert.False(analysis.IsRecoverable);
        Assert.Empty(analysis.RecommendedChains);
    }

    [Fact]
    public void AnalyzeFailure_RetriesRecoverableProduceChapterGateFailure()
    {
        var recovery = new AgentRecoveryEngine(new object(), new object());

        var analysis = recovery.AnalyzeFailure(
            new AgentToolCall
            {
                Name = "ProduceChapter",
                Arguments = new Dictionary<string, string>
                {
                    ["runId"] = "run-003",
                    ["commitPolicy"] = "auto_commit"
                }
            },
            new AgentToolExecutionResult
            {
                Success = false,
                IsRepairable = true,
                RecommendedToolName = "ProduceChapter",
                Phase = "gate_failed",
                Message = "章节生产闭环在 校验/修复章节草稿 阶段停止。已执行阶段：构建上下文包 -> 生成章节正文 -> 硬门禁校验 -> 自动修复草稿#1 -> 自动修复草稿#2。失败原因：CHANGES JSON 不是可解析对象。",
                Failure = new AgentToolFailure
                {
                    FailedStage = "RepairChapterDraft",
                    Reason = "章节草稿修复后仍未通过硬门禁。",
                    RequiresUserDecision = false,
                    Recoverable = true,
                    RecommendedAction = "ProduceChapter"
                }
            },
            new AgentSession
            {
                ActiveRunId = "run-003"
            },
            new StoryBibleDocument());

        Assert.True(analysis.IsRecoverable);
        var chain = Assert.Single(analysis.RecommendedChains);
        var step = Assert.Single(chain.Steps);
        Assert.Equal("ProduceChapter", step.ToolCall.Name);
        Assert.Equal("run-003", step.ToolCall.Arguments["runId"]);
        Assert.Equal("auto_commit", step.ToolCall.Arguments["commitPolicy"]);
    }

    [Fact]
    public void AnalyzeFailure_DoesNotRetryProduceChapterQualityReviewFailure()
    {
        var recovery = new AgentRecoveryEngine(new object(), new object());

        var analysis = recovery.AnalyzeFailure(
            new AgentToolCall
            {
                Name = "ProduceChapter",
                Arguments = new Dictionary<string, string>
                {
                    ["runId"] = "run-004",
                    ["commitPolicy"] = "auto_commit"
                }
            },
            new AgentToolExecutionResult
            {
                Success = false,
                IsRepairable = true,
                Phase = "validated",
                Message = "章节生产闭环在 Agent 质量评审 阶段停止。失败原因：章节质量评审未通过，生产闭环需要停在 AgentReview 阶段：项目一致性校验=Fail，核心创意落地=Warning。",
                Failure = new AgentToolFailure
                {
                    FailedStage = "ReviewChapter",
                    Reason = "章节质量评审未通过。",
                    RequiresUserDecision = false,
                    Recoverable = true,
                    RecommendedAction = "ProduceChapter"
                }
            },
            new AgentSession
            {
                ActiveRunId = "run-004"
            },
            new StoryBibleDocument());

        Assert.False(analysis.IsRecoverable);
        Assert.Empty(analysis.RecommendedChains);
    }

    [Fact]
    public void AnalyzeFailure_DoesNotRecoverObsoleteLowLevelChapterTools()
    {
        var recovery = new AgentRecoveryEngine(new object(), new object());

        var analysis = recovery.AnalyzeFailure(
            new AgentToolCall
            {
                Name = "RepairChapterDraft",
                Arguments = new Dictionary<string, string> { ["runId"] = "run-003" }
            },
            new AgentToolExecutionResult
            {
                Success = false,
                Phase = "gate_failed",
                Message = "章节草稿修复后仍未通过硬门禁：核心连续性失败：未承接「逆潮夜临近，威胁增加」。"
            },
            new AgentSession
            {
                ActiveRunId = "run-003"
            },
            new StoryBibleDocument());

        Assert.False(analysis.IsRecoverable);
        Assert.Empty(analysis.RecommendedChains);
    }

    [Fact]
    public void AnalyzeFailure_DoesNotAutoCreateStoryFoundation()
    {
        var recovery = new AgentRecoveryEngine(new object(), new object());

        var analysis = recovery.AnalyzeFailure(
            new AgentToolCall
            {
                Name = "PlanVolumeArc",
                Arguments = new Dictionary<string, string> { ["creativeBrief"] = "规划第一卷" }
            },
            new AgentToolExecutionResult
            {
                Success = false,
                Phase = "blocked",
                Message = "Story Bible 尚未固化，不能规划卷纲。应先补齐并确认故事地基。"
            },
            new AgentSession
            {
                WorkingMemory = { CurrentGoal = "写一本末世打怪升级小说" }
            },
            new StoryBibleDocument());

        Assert.False(analysis.IsRecoverable);
        Assert.Empty(analysis.RecommendedChains);
    }
}
