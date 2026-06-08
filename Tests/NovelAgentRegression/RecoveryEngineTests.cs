// Tests/NovelAgentRegression/RecoveryEngineTests.cs
using TM.Web.NovelAgentWeb.Support;
using Xunit;

namespace TM.Tests.NovelAgentRegression;

public sealed class RecoveryEngineTests
{
    [Fact]
    public void ToolFailureType_HasExpectedValues()
    {
        var missingPrereq = ToolFailureType.MissingPrerequisite;
        var invalidParams = ToolFailureType.InvalidParameters;
        var logicViolation = ToolFailureType.LogicConstraintViolation;
        var resourceUnavail = ToolFailureType.ResourceUnavailable;

        Assert.Equal(0, (int)missingPrereq);
        Assert.Equal(1, (int)invalidParams);
        Assert.Equal(2, (int)logicViolation);
        Assert.Equal(3, (int)resourceUnavail);
    }

    [Fact]
    public void AnalyzeFailure_MissingPrerequisite_ReturnsRecoverableWithChain()
    {
        var engine = new AgentRecoveryEngine(null!, null!);
        var failedCall = new AgentToolCall { Name = "BuildChapterContextPackage", Arguments = new() { ["runId"] = "run123" } };
        var failedResult = new AgentToolExecutionResult { Success = false, Message = "构建上下文包前必须先选定章节候选。" };
        var session = new AgentSession { SessionId = "sess1", ActiveRunId = "run123", WorkingMemory = new AgentWorkingMemory() };
        var bible = new StoryBibleDocument { AgentRuns = new() };

        var analysis = engine.AnalyzeFailure(failedCall, failedResult, session, bible);

        Assert.Equal(ToolFailureType.MissingPrerequisite, analysis.Type);
        Assert.True(analysis.IsRecoverable);
        Assert.NotEmpty(analysis.RecommendedChains);
    }
}
