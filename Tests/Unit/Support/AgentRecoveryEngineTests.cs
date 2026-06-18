using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace Tests.Unit.Support;

public class AgentRecoveryEngineTests
{
    [Fact]
    public void AnalyzeFailure_RecommendsRepairForValidationGateJsonFailure()
    {
        var recovery = new AgentRecoveryEngine(new object(), new object());

        var analysis = recovery.AnalyzeFailure(
            new AgentToolCall
            {
                Name = "ValidateChapterDraft",
                Arguments = new Dictionary<string, string> { ["runId"] = "run-003" }
            },
            new AgentToolExecutionResult
            {
                Success = false,
                Phase = "gate_failed",
                Message = "章节草稿未通过硬门禁：CHANGES JSON 不是可解析对象。"
            },
            new AgentSession
            {
                ActiveRunId = "run-003"
            },
            new StoryBibleDocument());

        Assert.True(analysis.IsRecoverable);
        var chain = Assert.Single(analysis.RecommendedChains);
        var step = Assert.Single(chain.Steps);
        Assert.Equal("RepairChapterDraft", step.ToolCall.Name);
        Assert.Equal("run-003", step.ToolCall.Arguments["runId"]);
    }

    [Fact]
    public void AnalyzeFailure_RecommendsRepairForRepairDraftGateFailure()
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

        Assert.True(analysis.IsRecoverable);
        var chain = Assert.Single(analysis.RecommendedChains);
        var step = Assert.Single(chain.Steps);
        Assert.Equal("RepairChapterDraft", step.ToolCall.Name);
        Assert.Equal("run-003", step.ToolCall.Arguments["runId"]);
    }
}
