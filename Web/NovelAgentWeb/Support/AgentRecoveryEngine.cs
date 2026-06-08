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
}

public sealed class AgentSession
{
    public string SessionId { get; set; } = string.Empty;
    public string? ActiveRunId { get; set; }
    public AgentWorkingMemory WorkingMemory { get; set; } = new();
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
        var message = failedResult.Message;
        var toolName = failedCall.Name;

        // Pattern matching for failure types
        var failureType = ClassifyFailure(message);

        var analysis = new ToolFailureAnalysis
        {
            Type = failureType,
            Reason = message,
        };

        // Determine if recoverable and build chains
        switch (failureType)
        {
            case ToolFailureType.MissingPrerequisite:
                analysis.IsRecoverable = true;
                analysis.RecommendedChains = BuildPrerequisiteChains(toolName, session, bible);
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
                analysis.RecommendedChains = BuildResourceInitChains(toolName, session, bible);
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
        string failedToolName,
        AgentSession session,
        StoryBibleDocument bible)
    {
        var chains = new List<PrerequisiteToolChain>();

        switch (failedToolName)
        {
            case "BuildChapterContextPackage":
                // Chain 1: PlanChapter (if no candidate selected)
                if (string.IsNullOrEmpty(session.WorkingMemory.SelectedChapterCandidateId))
                {
                    chains.Add(new PrerequisiteToolChain
                    {
                        Priority = 1,
                        Description = "规划新章节并选择候选",
                        Steps = new()
                        {
                            new PrerequisiteToolStep
                            {
                                ToolCall = new AgentToolCall
                                {
                                    Name = "PlanChapter",
                                    Arguments = new()
                                    {
                                        ["runId"] = session.ActiveRunId ?? "",
                                    }
                                },
                                MissingPrerequisiteTag = "ChapterPlan",
                            }
                        }
                    });
                }

                // Chain 2: SelectChapterCandidate (if already planned)
                chains.Add(new PrerequisiteToolChain
                {
                    Priority = 2,
                    Description = "选择已规划的章节候选",
                    Steps = new()
                    {
                        new PrerequisiteToolStep
                        {
                            ToolCall = new AgentToolCall
                            {
                                Name = "SelectChapterCandidate",
                                Arguments = new()
                                {
                                    ["runId"] = session.ActiveRunId ?? "",
                                }
                            },
                            MissingPrerequisiteTag = "ChapterCandidateSelection",
                        }
                    }
                });
                break;

            case "GenerateChapterWithChanges":
            case "ValidateChapterDraft":
                // Need context package first
                chains.Add(new PrerequisiteToolChain
                {
                    Priority = 1,
                    Description = "构建章节上下文包",
                    Steps = new()
                    {
                        new PrerequisiteToolStep
                        {
                            ToolCall = new AgentToolCall
                            {
                                Name = "BuildChapterContextPackage",
                                Arguments = new()
                                {
                                    ["runId"] = session.ActiveRunId ?? "",
                                }
                            },
                            MissingPrerequisiteTag = "ChapterContextPackage",
                        }
                    }
                });

                // Or regenerate if context already built
                if (failedToolName == "ValidateChapterDraft")
                {
                    chains.Add(new PrerequisiteToolChain
                    {
                        Priority = 2,
                        Description = "重新生成章节草稿",
                        Steps = new()
                        {
                            new PrerequisiteToolStep
                            {
                                ToolCall = new AgentToolCall
                                {
                                    Name = "GenerateChapterWithChanges",
                                    Arguments = new()
                                    {
                                        ["runId"] = session.ActiveRunId ?? "",
                                    }
                                },
                                MissingPrerequisiteTag = "ChapterDraft",
                            }
                        }
                    });
                }
                break;

            case "CommitValidatedChapter":
                // Need validated draft first
                chains.Add(new PrerequisiteToolChain
                {
                    Priority = 1,
                    Description = "验证章节草稿",
                    Steps = new()
                    {
                        new PrerequisiteToolStep
                        {
                            ToolCall = new AgentToolCall
                            {
                                Name = "ValidateChapterDraft",
                                Arguments = new()
                                {
                                    ["runId"] = session.ActiveRunId ?? "",
                                }
                            },
                            MissingPrerequisiteTag = "ValidatedDraft",
                        }
                    }
                });
                break;
        }

        return chains;
    }

    private List<PrerequisiteToolChain> BuildResourceInitChains(
        string failedToolName,
        AgentSession session,
        StoryBibleDocument bible)
    {
        var chains = new List<PrerequisiteToolChain>();

        // Check if Story Bible needs initialization
        if (bible.AgentRuns.Count == 0)
        {
            chains.Add(new PrerequisiteToolChain
            {
                Priority = 1,
                Description = "初始化故事圣经",
                Steps = new()
                {
                    new PrerequisiteToolStep
                    {
                        ToolCall = new AgentToolCall
                        {
                            Name = "InitializeStoryBible",
                            Arguments = new()
                            {
                                ["sessionId"] = session.SessionId,
                            }
                        },
                        MissingPrerequisiteTag = "StoryBibleInitialization",
                    }
                }
            });
        }

        return chains;
    }
}
