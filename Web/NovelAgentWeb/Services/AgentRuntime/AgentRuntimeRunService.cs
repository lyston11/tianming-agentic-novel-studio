using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Services.AgentRuntime;

public sealed class AgentRuntimeRunService : IAgentRuntimeRunService
{
    private readonly NovelAgentDbContext _db;

    public AgentRuntimeRunService(NovelAgentDbContext db)
    {
        _db = db;
    }

    public async Task<AgentRuntimeRun> CreateQueuedAsync(CreateAgentRuntimeRunRequest request, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var run = new AgentRuntimeRun
        {
            Id = Guid.NewGuid().ToString("N"),
            UserId = request.UserId,
            SessionId = request.SessionId,
            ProjectId = string.IsNullOrWhiteSpace(request.ProjectId) ? null : request.ProjectId,
            Status = AgentRuntimeRunStatus.Queued,
            CurrentPhase = AgentRuntimeRunStatus.Queued,
            UserMessage = request.UserMessage.Trim(),
            LastMessage = "已进入后台执行队列。",
            ResultJson = "{}",
            ErrorMessage = string.Empty,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.AgentRuntimeRuns.Add(run);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return run;
    }

    public Task<AgentRuntimeRun?> TryGetAsync(string runtimeRunId, CancellationToken ct = default) =>
        _db.AgentRuntimeRuns.FirstOrDefaultAsync(r => r.Id == runtimeRunId, ct);

    public Task<AgentRuntimeRun?> TryGetActiveAsync(string userId, string sessionId, CancellationToken ct = default) =>
        _db.AgentRuntimeRuns
            .Where(r => r.UserId == userId &&
                        r.SessionId == sessionId &&
                        AgentRuntimeRunStatus.Active.Contains(r.Status))
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<AgentRuntimeRun> MarkRunningAsync(string runtimeRunId, CancellationToken ct = default)
    {
        var run = await RequireRunAsync(runtimeRunId, ct).ConfigureAwait(false);
        run.Status = AgentRuntimeRunStatus.Running;
        run.CurrentPhase = AgentRuntimeRunStatus.Running;
        run.StartedAt ??= DateTime.UtcNow;
        run.UpdatedAt = DateTime.UtcNow;
        run.LastMessage = "Agent 已开始后台执行。";
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return run;
    }

    public async Task<AgentRuntimeRun> UpdateProgressAsync(
        string runtimeRunId,
        string phase,
        string message,
        string? activeTool = null,
        int? currentStep = null,
        CancellationToken ct = default)
    {
        var run = await RequireRunAsync(runtimeRunId, ct).ConfigureAwait(false);
        run.CurrentPhase = string.IsNullOrWhiteSpace(phase) ? run.CurrentPhase : phase;
        run.LastMessage = string.IsNullOrWhiteSpace(message) ? run.LastMessage : message.Trim();
        run.ActiveTool = activeTool ?? run.ActiveTool;
        if (currentStep.HasValue)
            run.CurrentStep = currentStep.Value;
        run.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return run;
    }

    public async Task<AgentRuntimeRun> MarkCompletedAsync(string runtimeRunId, object? result, CancellationToken ct = default)
    {
        var run = await RequireRunAsync(runtimeRunId, ct).ConfigureAwait(false);
        run.Status = AgentRuntimeRunStatus.Completed;
        run.CurrentPhase = AgentRuntimeRunStatus.Completed;
        run.ResultJson = Serialize(result);
        run.LastMessage = BuildCompletedMessage(result);
        run.CompletedAt = DateTime.UtcNow;
        run.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return run;
    }

    public async Task<AgentRuntimeRun> MarkFailedAsync(string runtimeRunId, string errorMessage, CancellationToken ct = default)
    {
        var run = await RequireRunAsync(runtimeRunId, ct).ConfigureAwait(false);
        run.Status = AgentRuntimeRunStatus.Failed;
        run.CurrentPhase = AgentRuntimeRunStatus.Failed;
        run.ErrorMessage = errorMessage;
        run.LastMessage = "后台执行失败。";
        run.CompletedAt = DateTime.UtcNow;
        run.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return run;
    }

    public async Task<AgentRuntimeRun> RequestCancelAsync(string runtimeRunId, CancellationToken ct = default)
    {
        var run = await RequireRunAsync(runtimeRunId, ct).ConfigureAwait(false);
        run.CancelRequested = true;
        run.LastMessage = "已收到暂停/取消请求，Agent 会在安全边界处理。";
        run.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return run;
    }

    private async Task<AgentRuntimeRun> RequireRunAsync(string runtimeRunId, CancellationToken ct)
    {
        var run = await TryGetAsync(runtimeRunId, ct).ConfigureAwait(false);
        return run ?? throw new KeyNotFoundException($"Runtime run {runtimeRunId} not found.");
    }

    private static string Serialize(object? value) =>
        value == null
            ? "{}"
            : JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    private static string BuildCompletedMessage(object? result)
    {
        if (result is AgentChatResponse response)
        {
            var reply = string.IsNullOrWhiteSpace(response.Reply) ? string.Empty : response.Reply.Trim();
            if (reply.Length > 120)
                reply = reply[..120] + "...";
            return string.IsNullOrWhiteSpace(reply)
                ? "本轮执行已完成。"
                : $"本轮执行已完成：{reply}";
        }

        return "后台执行已完成。";
    }
}
