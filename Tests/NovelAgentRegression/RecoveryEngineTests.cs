// Tests/NovelAgentRegression/RecoveryEngineTests.cs
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.NovelAgent.Models;
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

        Assert.Equal(0, (int)missingPrereq);
        Assert.Equal(1, (int)invalidParams);
        Assert.Equal(2, (int)logicViolation);
    }

    [Fact]
    public void AnalyzeFailure_ProduceChapterMissingPrerequisite_DoesNotBuildOuterRecoveryChain()
    {
        var engine = new AgentRecoveryEngine(null!, null!);
        var failedCall = new AgentToolCall { Name = "ProduceChapter", Arguments = new() { ["runId"] = "run123" } };
        var failedResult = new AgentToolExecutionResult { Success = false, Message = "章节生产前必须先选定章节候选。" };
        var session = new AgentSession { SessionId = "sess1", ActiveRunId = "run123", WorkingMemory = new AgentWorkingMemory() };
        var bible = new StoryBibleDocument { AgentRuns = new() };

        var analysis = engine.AnalyzeFailure(failedCall, failedResult, session, bible);

        Assert.Equal(ToolFailureType.MissingPrerequisite, analysis.Type);
        Assert.False(analysis.IsRecoverable);
        Assert.Empty(analysis.RecommendedChains);
    }

    [Fact]
    public void AnalyzeFailure_CandidateNotSelected_DoesNotRouteToObsoleteSelectionTool()
    {
        var engine = new AgentRecoveryEngine(null!, null!);
        var failedCall = new AgentToolCall { Name = "ProduceChapter", Arguments = new() { ["runId"] = "run123" } };
        var failedResult = new AgentToolExecutionResult { Success = false, Message = "章节候选还没选定。" };
        var session = new AgentSession { SessionId = "sess1", ActiveRunId = "run123", WorkingMemory = new AgentWorkingMemory() };
        var bible = new StoryBibleDocument { AgentRuns = new() };

        var analysis = engine.AnalyzeFailure(failedCall, failedResult, session, bible);

        Assert.Equal(ToolFailureType.MissingPrerequisite, analysis.Type);
        Assert.False(analysis.IsRecoverable);
        Assert.Empty(analysis.RecommendedChains);
    }

    [Fact]
    public async Task RecoverFromFailureAsync_DoesNotRecoverProduceChapterByObsoletePrerequisiteChain()
    {
        var toolRegistry = new MockAgentToolRegistry();
        var guardrails = new MockAgentToolGuardrails();
        var engine = new AgentRecoveryEngine(toolRegistry, guardrails);

        var failedCall = new AgentToolCall { Name = "ProduceChapter", Arguments = new() { ["runId"] = "run1" } };
        var failedResult = new AgentToolExecutionResult { Success = false, Message = "章节候选还没选定。" };
        var session = new AgentSession { SessionId = "s1", ActiveRunId = "run1", WorkingMemory = new AgentWorkingMemory() };
        var bible = new StoryBibleDocument();

        var result = await engine.RecoverFromFailureAsync(failedCall, failedResult, session, bible, CancellationToken.None);

        Assert.False(result.Recovered);
        Assert.False(result.Success);
        Assert.Empty(toolRegistry.ExecutedCalls);
        Assert.Contains("不可恢复", result.Message);
    }
}
// Mock implementations for testing
public sealed class MockAgentToolRegistry
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

public sealed class MockAgentToolGuardrails
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

    public dynamic Check(string toolName, Dictionary<string, string> arguments, bool lastSuccess)
    {
        return new MockGuardrailCheckResult();
    }

    public void RecordSuccess(string toolName)
    {
        // Mock implementation - does nothing
    }
}

public sealed class MockGuardrailCheckResult
{
    public bool IsBlocked { get; set; }
    public bool IsWarning { get; set; }
    public string Message { get; set; } = string.Empty;
}
