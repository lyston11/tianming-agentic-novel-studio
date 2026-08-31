using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Moq;
using TM.Web.NovelAgentWeb.Controllers;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Filters;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Controllers;

public sealed class IndexControllerTests
{
    [Fact]
    public async Task GetOutbox_AdminListsFilteredOutboxEvents()
    {
        await using var db = CreateDb();
        await SeedOutboxAsync(db);
        var controller = new IndexController(
            new ProductionOutboxAdminService(db, new RecordingOutboxDispatcher()),
            CurrentUser("admin", isAdmin: true));

        var result = await controller.GetOutbox("project-1", "retryable_failed", 20, CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.True(envelope.Success);
        var data = Assert.IsType<OutboxAdminListResponse>(envelope.Data);
        var item = Assert.Single(data.Items);
        Assert.Equal("outbox-2", item.Id);
        Assert.Equal("index_chapter_content", item.EventType);
        Assert.Equal("retryable_failed", item.Status);
        Assert.Equal("Qdrant timeout", item.LastError);
    }

    [Fact]
    public async Task RetryOutbox_AdminResetsEventAndDispatchesOnce()
    {
        await using var db = CreateDb();
        await SeedOutboxAsync(db);
        var dispatcher = new RecordingOutboxDispatcher();
        var controller = new IndexController(
            new ProductionOutboxAdminService(db, dispatcher),
            CurrentUser("admin", isAdmin: true));

        var result = await controller.RetryOutbox("outbox-2", CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.True(envelope.Success);
        var data = Assert.IsType<OutboxAdminRetryResponse>(envelope.Data);
        var evt = await db.OutboxEvents.SingleAsync(e => e.Id == "outbox-2");
        Assert.Equal("pending", evt.Status);
        Assert.Equal(0, evt.Attempts);
        Assert.Null(evt.LastError);
        Assert.Null(evt.NextAttemptAt);
        Assert.Equal(1, dispatcher.Calls);
        Assert.Equal(1, data.DispatchAttempted);
        Assert.Equal(2, data.PendingCount);
    }

    [Fact]
    public async Task RetryOutbox_PassesIdempotencyKeyToAdminService()
    {
        var outbox = new Mock<IProductionOutboxAdminService>();
        outbox
            .Setup(item => item.RetryAsync(
                "outbox-2",
                "retry-key-001",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OutboxAdminRetryResponse
            {
                EventId = "outbox-2",
                Status = "pending",
                DispatchAttempted = 1
            });
        var controller = new IndexController(
            outbox.Object,
            CurrentUser("admin", isAdmin: true))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.ControllerContext.HttpContext.Request.Headers["Idempotency-Key"] = "retry-key-001";

        var result = await controller.RetryOutbox("outbox-2", CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.True(envelope.Success);
        outbox.VerifyAll();
    }

    [Fact]
    public async Task RetryOutbox_WithSameIdempotencyKeyDoesNotDispatchTwice()
    {
        await using var db = CreateDb();
        await SeedOutboxAsync(db);
        var dispatcher = new RecordingOutboxDispatcher();
        var service = new ProductionOutboxAdminService(db, dispatcher);

        var first = await service.RetryAsync("outbox-2", "retry-key-001", CancellationToken.None);
        var second = await service.RetryAsync("outbox-2", "retry-key-001", CancellationToken.None);

        Assert.Equal(1, dispatcher.Calls);
        Assert.Equal(1, first.DispatchAttempted);
        Assert.Equal(0, second.DispatchAttempted);
        var evt = await db.OutboxEvents.SingleAsync(e => e.Id == "outbox-2");
        Assert.Contains("retry-key-001", evt.PayloadJson);
    }

    [Fact]
    public async Task GetOutbox_NonAdminIsRejected()
    {
        await using var db = CreateDb();
        await SeedOutboxAsync(db);
        var controller = new IndexController(
            new ProductionOutboxAdminService(db, new RecordingOutboxDispatcher()),
            CurrentUser("user-1"));

        var result = await controller.GetOutbox(null, null, 20, CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.False(envelope.Success);
        Assert.Equal("ADMIN_REQUIRED", envelope.Error!.Code);
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static async Task SeedOutboxAsync(NovelAgentDbContext db)
    {
        db.OutboxEvents.AddRange(
            new OutboxEvent
            {
                Id = "outbox-1",
                UserId = "user-1",
                ProjectId = "project-1",
                RuntimeRunId = "run-1",
                EventType = "index_chapter_content",
                AggregateType = "chapter_version",
                AggregateId = "version-1",
                PayloadJson = "{}",
                Status = "pending",
                CreatedAt = DateTime.UtcNow.AddMinutes(-3),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-3)
            },
            new OutboxEvent
            {
                Id = "outbox-2",
                UserId = "user-1",
                ProjectId = "project-1",
                RuntimeRunId = "run-1",
                EventType = "index_chapter_content",
                AggregateType = "chapter_version",
                AggregateId = "version-2",
                PayloadJson = "{}",
                Status = "retryable_failed",
                Attempts = 2,
                LastError = "Qdrant timeout",
                NextAttemptAt = DateTime.UtcNow.AddMinutes(5),
                CreatedAt = DateTime.UtcNow.AddMinutes(-2),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-1)
            },
            new OutboxEvent
            {
                Id = "outbox-3",
                UserId = "user-2",
                ProjectId = "project-2",
                EventType = "index_knowledge_content",
                AggregateType = "knowledge",
                AggregateId = "knowledge-1",
                PayloadJson = "{}",
                Status = "completed",
                CreatedAt = DateTime.UtcNow.AddMinutes(-1),
                UpdatedAt = DateTime.UtcNow
            });
        await db.SaveChangesAsync();
    }

    private static ICurrentUserService CurrentUser(string userId, bool isAdmin = false)
    {
        var current = new Mock<ICurrentUserService>();
        current.Setup(x => x.GetUserId()).Returns(userId);
        current.Setup(x => x.TryGetUserId()).Returns(userId);
        current.Setup(x => x.IsAdmin()).Returns(isAdmin);
        current.Setup(x => x.IsAuthenticated()).Returns(true);
        return current.Object;
    }

    private static async Task<ApiEnvelope<object>> ApplyEnvelopeAsync(IActionResult result)
    {
        var filter = new ApiEnvelopeResultFilter();
        var httpContext = new DefaultHttpContext();
        httpContext.TraceIdentifier = "test-trace";
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var executing = new ResultExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            result,
            controller: new object());

        await filter.OnResultExecutionAsync(
            executing,
            () => Task.FromResult(new ResultExecutedContext(
                actionContext,
                new List<IFilterMetadata>(),
                result,
                controller: new object())));

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        return Assert.IsType<ApiEnvelope<object>>(objectResult.Value);
    }

    private sealed class RecordingOutboxDispatcher : IProductionOutboxDispatcher
    {
        public int Calls { get; private set; }

        public Task<int> DispatchPendingAsync(int maxItems = 20, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(1);
        }
    }
}
