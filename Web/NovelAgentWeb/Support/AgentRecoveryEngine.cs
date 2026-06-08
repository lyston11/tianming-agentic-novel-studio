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

// Stub types for testing - these will be replaced with real types from AgentCore later
public sealed class AgentWorkingMemory
{
    public string? SelectedChapterCandidateId { get; set; }
    public string? LastBuiltContextRunId { get; set; }
    public string? CurrentGoal { get; set; }
}

public sealed class ChatTurn
{
    public string TurnId { get; set; } = string.Empty;
}

public sealed class AgentSession
{
    public string SessionId { get; set; } = string.Empty;
    public string? ActiveRunId { get; set; }
    public AgentWorkingMemory WorkingMemory { get; set; } = new();
    public List<ChatTurn>? ChatHistory { get; set; }
}

public sealed class StoryBibleDocument
{
    public List<object> AgentRuns { get; set; } = new();
}

// Stub interfaces for dependencies
public interface IAgentToolRegistry { }
public interface IAgentToolGuardrails { }

public sealed class AgentRecoveryEngine
{
    private readonly IAgentToolRegistry _toolRegistry;
    private readonly IAgentToolGuardrails _guardrails;

    public AgentRecoveryEngine(IAgentToolRegistry toolRegistry, IAgentToolGuardrails guardrails)
    {
        _toolRegistry = toolRegistry;
        _guardrails = guardrails;
    }

    public ToolFailureAnalysis AnalyzeFailure(
        AgentToolCall failedCall,
        AgentToolExecutionResult failedResult,
        AgentSession session,
        StoryBibleDocument bible)
    {
        var message = failedResult.Message.ToLowerInvariant();

        // Pattern matching for failure types
        var failureType = ClassifyFailure(message);

        var analysis = new ToolFailureAnalysis
        {
            Type = failureType,
            Reason = failedResult.Message,
        };

        // Determine if recoverable and build chains
        switch (failureType)
        {
            case ToolFailureType.MissingPrerequisite:
                analysis.IsRecoverable = true;
                analysis.RecommendedChains = BuildPrerequisiteChains(failedCall, message, session, bible);
                break;

            case ToolFailureType.InvalidParameters:
                // Could be recoverable if we can infer correct parameters
                analysis.IsRecoverable = false; // Conservative for now
                analysis.RecommendedChains = new();
                break;

            case ToolFailureType.LogicConstraintViolation:
                // Usually not auto-recoverable, needs human decision
                analysis.IsRecoverable = false;
                analysis.RecommendedChains = new();
                break;

            case ToolFailureType.ResourceUnavailable:
                // Could be recoverable if we can initialize the resource
                analysis.IsRecoverable = true;
                analysis.RecommendedChains = BuildResourceInitChains(failedCall, message, session, bible);
                break;
        }

        return analysis;
    }

    private ToolFailureType ClassifyFailure(string message)
    {
        // MissingPrerequisite patterns
        if (message.Contains("必须先") || message.Contains("需要先") ||
            message.Contains("前必须") || message.Contains("还没有"))
        {
            return ToolFailureType.MissingPrerequisite;
        }

        // InvalidParameters patterns
        if (message.Contains("不在当前") || message.Contains("不存在") ||
            message.Contains("无效") || message.Contains("不是有效"))
        {
            return ToolFailureType.InvalidParameters;
        }

        // LogicConstraintViolation patterns
        if (message.Contains("已提交") || message.Contains("不能自动") ||
            message.Contains("只能"))
        {
            return ToolFailureType.LogicConstraintViolation;
        }

        // ResourceUnavailable patterns
        if (message.Contains("尚未固化") || message.Contains("还未") ||
            message.Contains("未知工具"))
        {
            return ToolFailureType.ResourceUnavailable;
        }

        // Default to InvalidParameters if no pattern matches
        return ToolFailureType.InvalidParameters;
    }

    private List<PrerequisiteToolChain> BuildPrerequisiteChains(
        AgentToolCall failedCall,
        string message,
        AgentSession session,
        StoryBibleDocument bible)
    {
        var chains = new List<PrerequisiteToolChain>();
        var runId = failedCall.Arguments.TryGetValue("runId", out var rid) ? rid : session.ActiveRunId ?? string.Empty;

        // Tool-specific prerequisite chains
        if (failedCall.Name == "BuildChapterContextPackage")
        {
            if (message.Contains("还没有候选") || message.Contains("章节候选"))
            {
                chains.Add(new PrerequisiteToolChain
                {
                    Steps = new()
                    {
                        new PrerequisiteToolStep
                        {
                            ToolCall = new AgentToolCall
                            {
                                Name = "PlanChapter",
                                Arguments = new()
                                {
                                    ["creativeBrief"] = session.WorkingMemory?.CurrentGoal ?? "继续当前章节规划",
                                    ["sourceTurnId"] = session.ChatHistory?.LastOrDefault()?.TurnId ?? string.Empty,
                                },
                            },
                            MissingPrerequisiteTag = "chapter_candidates",
                        },
                    },
                    Priority = 1,
                    Description = "缺少章节候选，需要先规划章节",
                });
            }
            else if (message.Contains("还没选定") || message.Contains("候选还没选定"))
            {
                chains.Add(new PrerequisiteToolChain
                {
                    Steps = new()
                    {
                        new PrerequisiteToolStep
                        {
                            ToolCall = new AgentToolCall
                            {
                                Name = "SelectChapterCandidate",
                                Arguments = new() { ["runId"] = runId },
                            },
                            MissingPrerequisiteTag = "chapter_candidate_selection",
                        },
                    },
                    Priority = 1,
                    Description = "章节候选未选定，需要先选择候选",
                });
            }
        }
        else if (failedCall.Name == "GenerateChapterWithChanges" || failedCall.Name == "ValidateChapterDraft")
        {
            if (message.Contains("上下文包") || message.Contains("context"))
            {
                chains.Add(new PrerequisiteToolChain
                {
                    Steps = new()
                    {
                        new PrerequisiteToolStep
                        {
                            ToolCall = new AgentToolCall
                            {
                                Name = "BuildChapterContextPackage",
                                Arguments = new() { ["runId"] = runId },
                            },
                            MissingPrerequisiteTag = "chapter_context_package",
                        },
                    },
                    Priority = 1,
                    Description = "缺少上下文包，需要先构建",
                });
            }
            else if (message.Contains("草稿") || message.Contains("draft"))
            {
                chains.Add(new PrerequisiteToolChain
                {
                    Steps = new()
                    {
                        new PrerequisiteToolStep
                        {
                            ToolCall = new AgentToolCall
                            {
                                Name = "GenerateChapterWithChanges",
                                Arguments = new() { ["runId"] = runId },
                            },
                            MissingPrerequisiteTag = "chapter_draft",
                        },
                    },
                    Priority = 1,
                    Description = "缺少章节草稿，需要先生成",
                });
            }
        }
        else if (failedCall.Name == "CommitValidatedChapter")
        {
            if (message.Contains("门禁") || message.Contains("gate") || message.Contains("校验"))
            {
                chains.Add(new PrerequisiteToolChain
                {
                    Steps = new()
                    {
                        new PrerequisiteToolStep
                        {
                            ToolCall = new AgentToolCall
                            {
                                Name = "ValidateChapterDraft",
                                Arguments = new() { ["runId"] = runId },
                            },
                            MissingPrerequisiteTag = "generation_gate",
                        },
                    },
                    Priority = 1,
                    Description = "门禁未通过，需要先校验",
                });
            }
        }

        return chains;
    }

    private List<PrerequisiteToolChain> BuildResourceInitChains(
        AgentToolCall failedCall,
        string message,
        AgentSession session,
        StoryBibleDocument bible)
    {
        var chains = new List<PrerequisiteToolChain>();

        if (message.Contains("story bible 尚未固化") || message.Contains("故事地基"))
        {
            chains.Add(new PrerequisiteToolChain
            {
                Steps = new()
                {
                    new PrerequisiteToolStep
                    {
                        ToolCall = new AgentToolCall
                        {
                            Name = "PlanStoryFoundation",
                            Arguments = new()
                            {
                                ["userSeed"] = session.WorkingMemory?.CurrentGoal ?? "生成故事地基",
                                ["genre"] = "通用",
                            },
                        },
                        MissingPrerequisiteTag = "story_foundation",
                    },
                },
                Priority = 1,
                Description = "Story Bible 未初始化，需要先规划故事地基",
            });
        }

        return chains;
    }
}
