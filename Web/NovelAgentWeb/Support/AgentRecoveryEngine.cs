using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Web.NovelAgentWeb.Support;

// Note: AgentToolCall and AgentToolExecutionResult are defined in AgentCore.cs

public enum ToolFailureType
{
    MissingPrerequisite = 0,
    InvalidParameters = 1,
    LogicConstraintViolation = 2,
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

// Note: AgentWorkingMemory, AgentSession, and StoryBibleDocument are defined elsewhere

public sealed class AgentRecoveryEngine
{
    private readonly dynamic _toolRegistry;
    private readonly dynamic _guardrails;

    public AgentRecoveryEngine(dynamic toolRegistry, dynamic guardrails)
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

        if (string.Equals(failedCall.Name, "ProduceChapter", StringComparison.OrdinalIgnoreCase))
        {
            if (IsRepairableClosedLoopChapterFailure(failedCall, failedResult, message))
            {
                analysis.Type = ToolFailureType.MissingPrerequisite;
                analysis.IsRecoverable = true;
                analysis.RecommendedChains = BuildProduceChapterRetryChain(failedCall, session);
            }

            return analysis;
        }

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

        }

        return analysis;
    }

    private static bool IsRepairableClosedLoopChapterFailure(
        AgentToolCall failedCall,
        AgentToolExecutionResult failedResult,
        string message)
    {
        if (!string.Equals(failedCall.Name, "ProduceChapter", StringComparison.OrdinalIgnoreCase))
            return false;

        if (failedResult.Failure?.RequiresUserDecision == true ||
            failedResult.Failure?.Recoverable == false)
        {
            return false;
        }

        return message.Contains("硬门禁", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("gate_failed", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("核心连续性失败", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("changes json", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("json 不是可解析", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("未识别到 changes", StringComparison.OrdinalIgnoreCase);
    }

    private static List<PrerequisiteToolChain> BuildProduceChapterRetryChain(
        AgentToolCall failedCall,
        AgentSession session)
    {
        var runId = failedCall.Arguments.TryGetValue("runId", out var rid)
            ? rid
            : session.ActiveRunId ?? string.Empty;
        var arguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(runId))
            arguments["runId"] = runId;
        if (failedCall.Arguments.TryGetValue("commitPolicy", out var commitPolicy) && !string.IsNullOrWhiteSpace(commitPolicy))
            arguments["commitPolicy"] = commitPolicy;
        if (failedCall.Arguments.TryGetValue("maxRepairAttempts", out var maxRepairAttempts) && !string.IsNullOrWhiteSpace(maxRepairAttempts))
            arguments["maxRepairAttempts"] = maxRepairAttempts;

        return new List<PrerequisiteToolChain>
        {
            new()
            {
                Steps = new()
                {
                    new PrerequisiteToolStep
                    {
                        ToolCall = new AgentToolCall
                        {
                            Name = "ProduceChapter",
                            Arguments = arguments,
                        },
                        MissingPrerequisiteTag = "chapter_closed_loop_retry",
                    },
                },
                Priority = 2,
                Description = "章节闭环生产失败，需要由 ProduceChapter 重新生成、校验和修复",
            }
        };
    }

    public async Task<RecoveryResult> RecoverFromFailureAsync(
        AgentToolCall failedCall,
        AgentToolExecutionResult failedResult,
        AgentSession session,
        StoryBibleDocument bible,
        CancellationToken ct)
    {
        // 1. Check guardrails - avoid infinite retry loops
        var guardrailCheck = _guardrails.Check(failedCall.Name, failedCall.Arguments, false);
        if (guardrailCheck.IsBlocked)
        {
            return RecoveryResult.Unrecoverable("工具执行失败次数过多，已被熔断器阻止");
        }

        // 2. Analyze failure type
        var analysis = AnalyzeFailure(failedCall, failedResult, session, bible);

        if (!analysis.IsRecoverable)
        {
            return RecoveryResult.Unrecoverable(analysis.Reason);
        }

        if (analysis.RecommendedChains.Count == 0)
        {
            return RecoveryResult.Unrecoverable("无法推理出有效的前置工具链");
        }

        // 3. Select best chain (highest priority)
        var chain = analysis.RecommendedChains.OrderByDescending(c => c.Priority).First();

        // 4. Execute prerequisite tool chain
        AgentToolExecutionResult? lastStepResult = null;
        foreach (var step in chain.Steps)
        {
            var stepResult = await _toolRegistry.ExecuteAsync(step.ToolCall, session, bible, false, ct).ConfigureAwait(false);
            lastStepResult = stepResult;
            if (!stepResult.Success)
            {
                if (stepResult.IsRepairable && !string.IsNullOrWhiteSpace(stepResult.RecommendedToolName))
                    return RecoveryResult.FromRetry(stepResult);

                return RecoveryResult.ChainFailed(step.ToolCall.Name, stepResult.Message);
            }
        }

        if (lastStepResult != null &&
            chain.Steps.Count == 1 &&
            string.Equals(chain.Steps[0].ToolCall.Name, failedCall.Name, StringComparison.OrdinalIgnoreCase))
        {
            return RecoveryResult.FromRetry(lastStepResult);
        }

        // 5. Retry original tool (最多 1 次完整链路)
        var retryResult = await _toolRegistry.ExecuteAsync(failedCall, session, bible, false, ct).ConfigureAwait(false);
        return RecoveryResult.FromRetry(retryResult);
    }

    private ToolFailureType ClassifyFailure(string message)
    {
        // MissingPrerequisite patterns
        if (message.Contains("必须先") || message.Contains("需要先") ||
            message.Contains("前必须") || message.Contains("还没有") ||
            message.Contains("还没选定") || message.Contains("还没"))
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

        if (message.Contains("尚未固化") || message.Contains("还未") ||
            message.Contains("未知工具"))
            return ToolFailureType.LogicConstraintViolation;

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

        if (string.Equals(failedCall.Name, "ProduceChapter", StringComparison.OrdinalIgnoreCase))
        {
            if (message.Contains("候选还没选定") || message.Contains("选定章节候选"))
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
            else if (message.Contains("上下文包") ||
                     message.Contains("context") ||
                     message.Contains("草稿") ||
                     message.Contains("draft") ||
                     message.Contains("门禁") ||
                     message.Contains("gate") ||
                     message.Contains("校验"))
            {
                chains.Add(new PrerequisiteToolChain
                {
                    Steps = new()
                    {
                        new PrerequisiteToolStep
                        {
                            ToolCall = new AgentToolCall
                            {
                                Name = "ProduceChapter",
                                Arguments = new() { ["runId"] = runId },
                            },
                            MissingPrerequisiteTag = "chapter_closed_loop_retry",
                        },
                    },
                    Priority = 1,
                    Description = "章节闭环产物缺失或校验失败，需要重新运行 ProduceChapter",
                });
            }
        }

        return chains;
    }

}
