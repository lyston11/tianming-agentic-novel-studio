using Microsoft.EntityFrameworkCore;
using Npgsql;
using TM.Web.NovelAgentWeb.Data;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public interface IKnowledgeProcessingTaskClaimer
{
    Task<KnowledgeProcessingTaskClaim?> ClaimNextAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);
}

public sealed class PostgresKnowledgeProcessingTaskClaimer : IKnowledgeProcessingTaskClaimer
{
    private readonly NovelAgentDbContext _db;
    private readonly IBackgroundClaimConnectionFactory _claimConnections;

    public PostgresKnowledgeProcessingTaskClaimer(
        NovelAgentDbContext db,
        IBackgroundClaimConnectionFactory claimConnections)
    {
        _db = db;
        _claimConnections = claimConnections;
    }

    public async Task<KnowledgeProcessingTaskClaim?> ClaimNextAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("Worker ID 不能为空。", nameof(workerId));
        var leaseSeconds = checked((int)Math.Ceiling(leaseDuration.TotalSeconds));
        if (leaseSeconds is < 5 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease 必须在 5 秒到 1 小时之间。");

        if (_db.Database.GetDbConnection() is not NpgsqlConnection)
            throw new InvalidOperationException("知识处理任务 claim 只支持 PostgreSQL。");
        await using var connection = await _claimConnections.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM claim_knowledge_processing_task(@worker_id, @lease_seconds)";
        command.Parameters.Add(new NpgsqlParameter("worker_id", workerId.Trim()));
        command.Parameters.Add(new NpgsqlParameter("lease_seconds", leaseSeconds));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new KnowledgeProcessingTaskClaim(
            reader.GetString(reader.GetOrdinal("task_id")),
            reader.GetString(reader.GetOrdinal("user_id")),
            reader.GetString(reader.GetOrdinal("document_blob_id")),
            reader.GetString(reader.GetOrdinal("processing_stage")),
            reader.GetInt32(reader.GetOrdinal("attempt")),
            reader.GetString(reader.GetOrdinal("lease_owner")),
            reader.GetFieldValue<DateTime>(reader.GetOrdinal("lease_expires_at")));
    }
}
