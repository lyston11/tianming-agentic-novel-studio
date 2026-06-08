using System.Collections.Concurrent;

namespace TM.Web.NovelAgentWeb.Support;

public sealed class AgentToolGuardrails
{
    private readonly ConcurrentDictionary<string, ToolFailureTracker> _trackers = new();

    public GuardrailResult Check(string toolName, Dictionary<string, string> args, bool lastSucceeded)
    {
        var key = toolName;
        var tracker = _trackers.GetOrAdd(key, _ => new ToolFailureTracker());

        if (lastSucceeded)
        {
            tracker.Reset();
            return GuardrailResult.Allow;
        }

        tracker.RecordFailure(args);

        // Exact same args failing repeatedly
        var argsKey = BuildArgsKey(args);
        if (tracker.GetExactFailures(argsKey) >= 3)
            return GuardrailResult.Block($"工具 {toolName} 使用相同参数连续失败 3 次，请换一种方式或询问用户。");

        if (tracker.GetExactFailures(argsKey) >= 2)
            return GuardrailResult.Warn($"工具 {toolName} 已经用相同参数失败 2 次，可能需要换个策略。");

        // Same tool failing with different args
        if (tracker.GetTotalFailures() >= 5)
            return GuardrailResult.Block($"工具 {toolName} 已经连续失败 5 次，请停止并询问用户。");

        if (tracker.GetTotalFailures() >= 3)
            return GuardrailResult.Warn($"工具 {toolName} 已经失败 {tracker.GetTotalFailures()} 次。");

        return GuardrailResult.Allow;
    }

    public void RecordSuccess(string toolName)
    {
        if (_trackers.TryGetValue(toolName, out var tracker))
            tracker.Reset();
    }

    public void ResetAll() => _trackers.Clear();

    private static string BuildArgsKey(Dictionary<string, string> args)
    {
        var sorted = args.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => $"{p.Key}={p.Value}");
        return string.Join("&", sorted);
    }
}

public sealed class ToolFailureTracker
{
    private readonly Dictionary<string, int> _exactFailures = new(StringComparer.OrdinalIgnoreCase);
    private int _totalFailures;
    private DateTime _lastFailure = DateTime.MinValue;

    public void RecordFailure(Dictionary<string, string> args)
    {
        var key = BuildArgsKey(args);
        _exactFailures[key] = _exactFailures.GetValueOrDefault(key) + 1;
        _totalFailures++;
        _lastFailure = DateTime.UtcNow;
    }

    public void Reset()
    {
        _exactFailures.Clear();
        _totalFailures = 0;
        _lastFailure = DateTime.MinValue;
    }

    public int GetExactFailures(string argsKey) =>
        _exactFailures.GetValueOrDefault(argsKey);

    public int GetTotalFailures() => _totalFailures;

    private static string BuildArgsKey(Dictionary<string, string> args)
    {
        var sorted = args.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => $"{p.Key}={p.Value}");
        return string.Join("&", sorted);
    }
}

public sealed class GuardrailResult
{
    public bool IsAllowed { get; init; }
    public bool IsWarning { get; init; }
    public bool IsBlocked { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool IsRepairable { get; init; }
    public string RecommendedToolName { get; init; } = string.Empty;
    public Dictionary<string, string> RecommendedArguments { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string MissingPrerequisite { get; init; } = string.Empty;
    public string UserFacingMessage { get; init; } = string.Empty;

    public static GuardrailResult Allow => new() { IsAllowed = true };
    public static GuardrailResult Warn(string message) => new() { IsWarning = true, Message = message };
    public static GuardrailResult Block(string message) => new() { IsBlocked = true, Message = message };
    public static GuardrailResult RepairableBlock(
        string message,
        string recommendedToolName,
        Dictionary<string, string>? recommendedArguments = null,
        string missingPrerequisite = "",
        string userFacingMessage = "") =>
        new()
        {
            IsBlocked = true,
            Message = message,
            IsRepairable = true,
            RecommendedToolName = recommendedToolName,
            RecommendedArguments = recommendedArguments ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            MissingPrerequisite = missingPrerequisite,
            UserFacingMessage = string.IsNullOrWhiteSpace(userFacingMessage) ? message : userFacingMessage,
        };
}
