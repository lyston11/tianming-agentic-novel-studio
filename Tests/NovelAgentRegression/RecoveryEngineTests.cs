// Tests/NovelAgentRegression/RecoveryEngineTests.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

    [Fact]
    public void AnalyzeFailure_CandidateNotSelected_ReturnsRecoverableWithSelectChain()
    {
        var engine = new AgentRecoveryEngine(null!, null!);
        var failedCall = new AgentToolCall { Name = "BuildChapterContextPackage", Arguments = new() { ["runId"] = "run123" } };
        var failedResult = new AgentToolExecutionResult { Success = false, Message = "章节候选还没选定。" };
        var session = new AgentSession { SessionId = "sess1", ActiveRunId = "run123", WorkingMemory = new AgentWorkingMemory() };
        var bible = new StoryBibleDocument { AgentRuns = new() };

        var analysis = engine.AnalyzeFailure(failedCall, failedResult, session, bible);

        Assert.Equal(ToolFailureType.MissingPrerequisite, analysis.Type);
        Assert.True(analysis.IsRecoverable);
        Assert.NotEmpty(analysis.RecommendedChains);
        Assert.Equal("SelectChapterCandidate", analysis.RecommendedChains[0].Steps[0].ToolCall.Name);
    }

    [Fact]
    public async Task RecoverFromFailureAsync_ExecutesPrerequisiteChainAndRetries()
    {
        var toolRegistry = new MockAgentToolRegistry();
        var guardrails = new AgentToolGuardrails();
        var engine = new AgentRecoveryEngine(toolRegistry, guardrails);

        var failedCall = new AgentToolCall { Name = "BuildChapterContextPackage", Arguments = new() { ["runId"] = "run1" } };
        var failedResult = new AgentToolExecutionResult { Success = false, Message = "章节候选还没选定。" };
        var session = new AgentSession { SessionId = "s1", ActiveRunId = "run1", WorkingMemory = new AgentWorkingMemory() };
        var bible = new StoryBibleDocument();

        var result = await engine.RecoverFromFailureAsync(failedCall, failedResult, session, bible, CancellationToken.None);

        Assert.True(result.Recovered, $"Recovery failed: {result.Message}");
        Assert.True(result.Success, $"Recovery not successful: {result.Message}");
        Assert.Single(toolRegistry.ExecutedCalls, call => call.Name == "SelectChapterCandidate");
    }
}
// Mock implementations for testing
public sealed class MockAgentToolRegistry : IAgentToolRegistry
{
    public List<AgentToolCall> ExecutedCalls { get; } = new();

    public Task<AgentToolExecutionResult> ExecuteAsync(
        AgentToolCall toolCall,
        AgentSession session,
        StoryBibleDocument bible,
        bool validate,
        CancellationToken ct)
    {
        ExecutedCalls.Add(toolCall);
        return Task.FromResult(new AgentToolExecutionResult
        {
            Success = true,
            Message = $"Mock execution of {toolCall.Name}",
        });
    }
}

public sealed class AgentToolGuardrails : IAgentToolGuardrails
{
    private readonly Dictionary<string, int> _attemptCounts = new();
    private const int MaxAttempts = 3;

    public bool CanAttempt(string toolName, Dictionary<string, string> arguments)
    {
        var key = $"{toolName}:{string.Join(",", arguments.Values)}";
        if (!_attemptCounts.TryGetValue(key, out var count))
        {
            _attemptCounts[key] = 1;
            return true;
        }

        if (count >= MaxAttempts)
        {
            return false;
        }

        _attemptCounts[key] = count + 1;
        return true;
    }
}
