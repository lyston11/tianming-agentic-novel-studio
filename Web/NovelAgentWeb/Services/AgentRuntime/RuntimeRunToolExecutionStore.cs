using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;

namespace TM.Web.NovelAgentWeb.Services.AgentRuntime;

/// <summary>
/// 持久化 RuntimeRun 的工具执行历史，用于跨进程重启的去重保护。
/// 防止进程崩溃重启后，副作用工具（如 ProduceChapter）被重复调用。
/// </summary>
public interface IRuntimeRunToolExecutionStore
{
    /// <summary>加载已执行工具指纹列表</summary>
    Task<HashSet<string>> LoadExecutedToolsAsync(string runtimeRunId, CancellationToken ct = default);

    /// <summary>追加工具指纹到已执行列表</summary>
    Task AppendExecutedToolAsync(string runtimeRunId, string fingerprint, CancellationToken ct = default);
}

public sealed class RuntimeRunToolExecutionStore : IRuntimeRunToolExecutionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NovelAgentDbContext _db;
    private readonly ILogger<RuntimeRunToolExecutionStore> _logger;

    public RuntimeRunToolExecutionStore(NovelAgentDbContext db, ILogger<RuntimeRunToolExecutionStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<HashSet<string>> LoadExecutedToolsAsync(string runtimeRunId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(runtimeRunId))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var json = await _db.AgentRuntimeRuns
            .AsNoTracking()
            .Where(r => r.Id == runtimeRunId)
            .Select(r => r.ExecutedToolsJson)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(json) || json == "[]")
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var list = JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new();
            return new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize executed tools for run {RunId}", runtimeRunId);
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task AppendExecutedToolAsync(string runtimeRunId, string fingerprint, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(runtimeRunId) || string.IsNullOrWhiteSpace(fingerprint))
            return;

        var run = await _db.AgentRuntimeRuns
            .FirstOrDefaultAsync(r => r.Id == runtimeRunId, ct)
            .ConfigureAwait(false);

        if (run == null) return;

        var list = new List<string>();
        try
        {
            if (!string.IsNullOrWhiteSpace(run.ExecutedToolsJson) && run.ExecutedToolsJson != "[]")
                list = JsonSerializer.Deserialize<List<string>>(run.ExecutedToolsJson, JsonOptions) ?? new();
        }
        catch
        {
            list = new List<string>();
        }

        if (!list.Contains(fingerprint, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(fingerprint);
            run.ExecutedToolsJson = JsonSerializer.Serialize(list, JsonOptions);
            run.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }
}
