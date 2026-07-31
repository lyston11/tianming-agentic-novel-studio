using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Rag;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public sealed record KnowledgeProcessingTaskClaim(
    string TaskId,
    string UserId,
    string DocumentBlobId,
    string ProcessingStage,
    int Attempt,
    string LeaseOwner = "",
    DateTime? LeaseExpiresAt = null);

public sealed class KnowledgeProcessingTaskRunner
{
    private readonly NovelAgentDbContext _db;
    private readonly IKnowledgeProcessingService _processing;
    private readonly IMultiScaleVectorIndexer _indexer;

    public KnowledgeProcessingTaskRunner(
        NovelAgentDbContext db,
        IKnowledgeProcessingService processing,
        IMultiScaleVectorIndexer indexer)
    {
        _db = db;
        _processing = processing;
        _indexer = indexer;
    }

    public async Task RunAsync(
        KnowledgeProcessingTaskClaim claim,
        CancellationToken cancellationToken = default)
    {
        var task = await _db.KnowledgeProcessingTasks.SingleOrDefaultAsync(item =>
            item.Id == claim.TaskId && item.UserId == claim.UserId,
            cancellationToken) ?? throw new InvalidOperationException("已领取的知识处理任务不存在。");
        EnsureClaimStillOwned(task, claim);

        try
        {
            if (claim.ProcessingStage == "extract")
            {
                var extractionAlreadyFinalized = await _db.KnowledgeDocumentBlobs
                    .AsNoTracking()
                    .AnyAsync(blob =>
                            blob.Id == claim.DocumentBlobId &&
                            blob.UserId == claim.UserId &&
                            blob.Status == "processed",
                        cancellationToken);
                if (!extractionAlreadyFinalized)
                    await _processing.ProcessFileAsync(claim.TaskId, cancellationToken);
                task.ProcessingStage = "index";
                task.Status = "processing";
                task.ErrorMessage = null;
                task.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
            }
            else if (claim.ProcessingStage != "index")
            {
                throw new InvalidOperationException($"不支持的知识处理阶段：{claim.ProcessingStage}");
            }

            await _indexer.IndexDocumentAsync(claim.UserId, claim.DocumentBlobId, cancellationToken);
            task.Status = "completed";
            task.ProcessingStage = "index";
            task.Progress = 100;
            task.ErrorMessage = null;
            task.ProcessingOwner = null;
            task.ProcessingLeaseExpiresAt = null;
            task.CompletedAt ??= DateTime.UtcNow;
            task.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            task.Status = task.Attempt < task.MaxAttempts ? "retryable_failed" : "failed";
            task.ErrorMessage = exception.Message;
            task.ProcessingOwner = null;
            task.ProcessingLeaseExpiresAt = null;
            task.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private static void EnsureClaimStillOwned(
        Data.Entities.KnowledgeProcessingTask task,
        KnowledgeProcessingTaskClaim claim)
    {
        if (!string.Equals(task.UploadBlobId, claim.DocumentBlobId, StringComparison.Ordinal))
            throw new InvalidOperationException("知识处理任务的数据库原件与 claim 不匹配。");
        if (!string.IsNullOrWhiteSpace(claim.LeaseOwner) &&
            (!string.Equals(task.ProcessingOwner, claim.LeaseOwner, StringComparison.Ordinal) ||
             task.Status is not ("claimed" or "processing")))
        {
            throw new InvalidOperationException("知识处理任务 lease 已失效或被其他 worker 领取。");
        }
    }
}
