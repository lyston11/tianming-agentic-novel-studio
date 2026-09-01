using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ProductionOutboxAdminServiceTests
{
    [Fact]
    public async Task ListAsync_FiltersByProjectStatusAndClampsLimit()
    {
        await using var db = CreateDb();
        await SeedOutboxAsync(db, 8);
        var service = new ProductionOutboxAdminService(db, new NoopOutboxDispatcher());

        var result = await service.ListAsync("project-1", "retryable_failed", 500);

        Assert.Equal(4, result.Count);
        Assert.All(result.Items, item =>
        {
            Assert.Equal("project-1", item.ProjectId);
            Assert.Equal("retryable_failed", item.Status);
        });
    }

    [Fact]
    public async Task RetryAsync_ThrowsWhenOutboxEventDoesNotExist()
    {
        await using var db = CreateDb();
        var service = new ProductionOutboxAdminService(db, new NoopOutboxDispatcher());

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.RetryAsync("missing-outbox"));
    }

    [Fact]
    public async Task RetryAsync_ReturnsCompletedStatusAfterDispatcherFinishesEvent()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var db = CreateDb(dbName);
        await SeedSingleOutboxAsync(db, "retryable_failed");
        var service = new ProductionOutboxAdminService(
            db,
            new MutatingOutboxDispatcher(dbName, "completed", attempts: 1, lastError: null));

        var result = await service.RetryAsync("outbox-1");

        Assert.Equal(1, result.DispatchAttempted);
        Assert.Equal("completed", result.Status);
        Assert.Equal(1, result.Attempts);
        Assert.Equal(string.Empty, result.LastError);
        Assert.Null(result.NextAttemptAt);
        Assert.Equal(0, result.PendingCount);
    }

    [Fact]
    public async Task RetryAsync_ReturnsRetryableFailureStatusAfterDispatcherFailsAgain()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var db = CreateDb(dbName);
        await SeedSingleOutboxAsync(db, "retryable_failed");
        var service = new ProductionOutboxAdminService(
            db,
            new MutatingOutboxDispatcher(dbName, "retryable_failed", attempts: 1, lastError: "Qdrant timeout"));

        var result = await service.RetryAsync("outbox-1");

        Assert.Equal(0, result.DispatchAttempted);
        Assert.Equal("retryable_failed", result.Status);
        Assert.Equal(1, result.Attempts);
        Assert.Equal("Qdrant timeout", result.LastError);
        Assert.NotNull(result.NextAttemptAt);
        Assert.Equal(1, result.PendingCount);
    }

    private static NovelAgentDbContext CreateDb(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static async Task SeedOutboxAsync(NovelAgentDbContext db, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            db.OutboxEvents.Add(new OutboxEvent
            {
                Id = $"outbox-{i}",
                UserId = "user-1",
                ProjectId = i % 2 == 0 ? "project-1" : "project-2",
                EventType = "index_chapter_content",
                AggregateType = "chapter_version",
                AggregateId = $"version-{i}",
                PayloadJson = "{}",
                Status = i % 2 == 0 ? "retryable_failed" : "pending",
                CreatedAt = DateTime.UtcNow.AddMinutes(-i),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-i)
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task SeedSingleOutboxAsync(NovelAgentDbContext db, string status)
    {
        db.OutboxEvents.Add(new OutboxEvent
        {
            Id = "outbox-1",
            UserId = "user-1",
            ProjectId = "project-1",
            EventType = "index_chapter_content",
            AggregateType = "chapter_version",
            AggregateId = "version-1",
            PayloadJson = "{}",
            Status = status,
            Attempts = 3,
            LastError = "previous failure",
            NextAttemptAt = DateTime.UtcNow.AddMinutes(-10),
            CreatedAt = DateTime.UtcNow.AddMinutes(-30),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-10)
        });

        await db.SaveChangesAsync();
    }

    private sealed class NoopOutboxDispatcher : IProductionOutboxDispatcher
    {
        public Task<int> DispatchPendingAsync(int maxItems = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class MutatingOutboxDispatcher : IProductionOutboxDispatcher
    {
        private readonly string _dbName;
        private readonly string _status;
        private readonly int _attempts;
        private readonly string? _lastError;

        public MutatingOutboxDispatcher(string dbName, string status, int attempts, string? lastError)
        {
            _dbName = dbName;
            _status = status;
            _attempts = attempts;
            _lastError = lastError;
        }

        public async Task<int> DispatchPendingAsync(int maxItems = 20, CancellationToken cancellationToken = default)
        {
            var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
                .UseInMemoryDatabase(_dbName)
                .Options;
            await using var db = new NovelAgentDbContext(options);
            var evt = await db.OutboxEvents.SingleAsync(e => e.Id == "outbox-1", cancellationToken);
            evt.Status = _status;
            evt.Attempts = _attempts;
            evt.LastError = _lastError;
            evt.NextAttemptAt = _status == "retryable_failed" ? DateTime.UtcNow.AddMinutes(1) : null;
            evt.CompletedAt = _status == "completed" ? DateTime.UtcNow : null;
            evt.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return _status == "completed" ? 1 : 0;
        }
    }
}
