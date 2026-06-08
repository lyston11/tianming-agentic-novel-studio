using System;
using System.Collections.Generic;

namespace TM.Web.NovelAgentWeb.Support;

// Simplified types for recovery engine - these match the structure in AgentCore.cs
public sealed class AgentToolCall
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Arguments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AgentToolExecutionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public enum ToolFailureType
{
    MissingPrerequisite = 0,
    InvalidParameters = 1,
    LogicConstraintViolation = 2,
    ResourceUnavailable = 3,
}

public sealed class PrerequisiteToolStep
{
    public AgentToolCall ToolCall { get; set; } = new();
    public string MissingPrerequisiteTag { get; set; } = string.Empty;
}

public sealed class PrerequisiteToolChain
{
    public List<PrerequisiteToolStep> Steps { get; set; } = new();
    public int Priority { get; set; }
    public string Description { get; set; } = string.Empty;
}

public sealed class ToolFailureAnalysis
{
    public ToolFailureType Type { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool IsRecoverable { get; set; }
    public List<PrerequisiteToolChain> RecommendedChains { get; set; } = new();
}

public sealed class RecoveryResult
{
    public bool Success { get; set; }
    public bool Recovered { get; set; }
    public string Message { get; set; } = string.Empty;
    public AgentToolExecutionResult? RetryResult { get; set; }

    public static RecoveryResult Unrecoverable(string reason) => new()
    {
        Success = false,
        Recovered = false,
        Message = $"不可恢复的失败：{reason}",
    };

    public static RecoveryResult ChainFailed(string toolName, string message) => new()
    {
        Success = false,
        Recovered = false,
        Message = $"前置工具链失败，{toolName} 执行出错：{message}",
    };

    public static RecoveryResult FromRetry(AgentToolExecutionResult retryResult) => new()
    {
        Success = retryResult.Success,
        Recovered = true,
        Message = retryResult.Success ? "自动恢复成功" : $"重试失败：{retryResult.Message}",
        RetryResult = retryResult,
    };
}
