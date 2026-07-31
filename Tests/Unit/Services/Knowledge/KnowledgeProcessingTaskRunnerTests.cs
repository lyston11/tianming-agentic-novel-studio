using TM.Web.NovelAgentWeb.Services.Knowledge;
using TM.Web.NovelAgentWeb.Services.Rag;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests.Unit.Services.Knowledge;

public sealed class KnowledgeProcessingTaskRunnerTests
{
    [Fact]
    public async Task RunAsync_ProcessesClaimedUploadBeforeIndexingItsDatabaseBlob()
    {
        var events = new List<string>();
        await using var db = new NovelAgentDbContext(new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        db.KnowledgeProcessingTasks.Add(new KnowledgeProcessingTask
        {
            Id = "task-1",
            UserId = "user-1",
            ProjectId = "project-1",
            UploadBlobId = "blob-1",
            Status = "claimed",
            ProcessingStage = "extract",
            Attempt = 1
        });
        await db.SaveChangesAsync();
        var processing = new RecordingProcessingService(events);
        var indexing = new RecordingIndexer(events);
        var runner = new KnowledgeProcessingTaskRunner(db, processing, indexing);
        var claim = new KnowledgeProcessingTaskClaim("task-1", "user-1", "blob-1", "extract", 1);

        await runner.RunAsync(claim);

        Assert.Equal(["process:task-1", "index:user-1:blob-1"], events);
        var task = await db.KnowledgeProcessingTasks.SingleAsync();
        Assert.Equal("completed", task.Status);
        Assert.Equal("index", task.ProcessingStage);
    }

    [Fact]
    public async Task RunAsync_IndexFailureRetainsIndexStageForRetry()
    {
        var events = new List<string>();
        await using var db = CreateDb();
        db.KnowledgeProcessingTasks.Add(CreateTask());
        await db.SaveChangesAsync();
        var runner = new KnowledgeProcessingTaskRunner(
            db,
            new RecordingProcessingService(events),
            new RecordingIndexer(events, new InvalidOperationException("qdrant unavailable")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(
            new KnowledgeProcessingTaskClaim("task-1", "user-1", "blob-1", "extract", 1)));

        Assert.Equal("qdrant unavailable", exception.Message);
        Assert.Equal(["process:task-1", "index:user-1:blob-1"], events);
        var task = await db.KnowledgeProcessingTasks.SingleAsync();
        Assert.Equal("retryable_failed", task.Status);
        Assert.Equal("index", task.ProcessingStage);
        Assert.Null(task.ProcessingOwner);
        Assert.Null(task.ProcessingLeaseExpiresAt);
    }

    [Fact]
    public async Task RunAsync_IndexRetryDoesNotRepeatExtraction()
    {
        var events = new List<string>();
        await using var db = CreateDb();
        var task = CreateTask();
        task.ProcessingStage = "index";
        task.Attempt = 2;
        db.KnowledgeProcessingTasks.Add(task);
        await db.SaveChangesAsync();
        var runner = new KnowledgeProcessingTaskRunner(
            db,
            new RecordingProcessingService(events),
            new RecordingIndexer(events));

        await runner.RunAsync(new KnowledgeProcessingTaskClaim(
            "task-1",
            "user-1",
            "blob-1",
            "index",
            2));

        Assert.Equal(["index:user-1:blob-1"], events);
        Assert.Equal("completed", (await db.KnowledgeProcessingTasks.SingleAsync()).Status);
    }

    [Fact]
    public async Task RunAsync_ExpiredExtractClaimSkipsExtractionWhenBlobWasAlreadyFinalized()
    {
        var events = new List<string>();
        await using var db = CreateDb();
        db.KnowledgeProcessingTasks.Add(CreateTask());
        db.KnowledgeDocumentBlobs.Add(new KnowledgeDocumentBlob
        {
            Id = "blob-1",
            UserId = "user-1",
            ProjectId = "project-1",
            FileName = "knowledge.txt",
            ContentHash = "hash",
            KnowledgeVersion = 1,
            Status = "processed"
        });
        await db.SaveChangesAsync();
        var runner = new KnowledgeProcessingTaskRunner(
            db,
            new RecordingProcessingService(events),
            new RecordingIndexer(events));

        await runner.RunAsync(new KnowledgeProcessingTaskClaim(
            "task-1",
            "user-1",
            "blob-1",
            "extract",
            2));

        Assert.Equal(["index:user-1:blob-1"], events);
        var task = await db.KnowledgeProcessingTasks.SingleAsync();
        Assert.Equal("completed", task.Status);
        Assert.Equal("index", task.ProcessingStage);
    }

    private static NovelAgentDbContext CreateDb() => new(
        new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static KnowledgeProcessingTask CreateTask() => new()
    {
        Id = "task-1",
        UserId = "user-1",
        ProjectId = "project-1",
        UploadBlobId = "blob-1",
        Status = "claimed",
        ProcessingStage = "extract",
        Attempt = 1,
        MaxAttempts = 3
    };

    private sealed class RecordingProcessingService(List<string> events) : IKnowledgeProcessingService
    {
        public Task<string> ProcessPendingFileAsync(string taskId, string userId, CancellationToken ct = default, KnowledgeProcessingProgressContext? progress = null) =>
            throw new NotSupportedException();

        public Task<string> ProcessFileAsync(string taskId, CancellationToken ct = default, KnowledgeProcessingProgressContext? progress = null)
        {
            events.Add($"process:{taskId}");
            return Task.FromResult("completed");
        }
    }

    private sealed class RecordingIndexer(
        List<string> events,
        Exception? failure = null) : IMultiScaleVectorIndexer
    {
        public Task<int> IndexDocumentAsync(string userId, string documentBlobId, CancellationToken cancellationToken = default)
        {
            events.Add($"index:{userId}:{documentBlobId}");
            if (failure != null)
                throw failure;
            return Task.FromResult(5);
        }
    }
}
