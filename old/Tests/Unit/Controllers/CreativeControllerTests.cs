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
using TM.Web.NovelAgentWeb.Services.Creative;
using Xunit;

namespace Tests.Unit.Controllers;

public sealed class CreativeControllerTests
{
    [Fact]
    public async Task CreateQueryAndDecideAsync_ManageCreativeIntentsThroughApiEnvelope()
    {
        await using var db = CreateDb();
        await SeedProjectsAsync(db);
        var controller = CreateController(db, "user-1");

        var createResult = await controller.CreateIntent(new CreateCreativeIntentApiRequest
        {
            ProjectId = "project-1",
            SessionId = "session-1",
            RunId = "run-1",
            RawContent = "第二章太平了，改成怪物围攻。",
            NormalizedIntent = "第二章主冲突改为怪物围攻，男主用银蓝邮徽识别逃生路线。",
            Source = "chat",
            TargetScope = "chapter",
            TargetChapterId = "chapter-002",
            ImpactLevel = "chapter_rewrite",
            ConflictStatus = "none"
        }, CancellationToken.None);

        var createEnvelope = await ApplyEnvelopeAsync(createResult);
        Assert.True(createEnvelope.Success);
        var created = Assert.IsType<CreativeIntentItem>(createEnvelope.Data);
        Assert.Equal("project-1", created.ProjectId);
        Assert.Equal("candidate", created.Status);
        Assert.Equal("chapter-002", created.TargetChapterId);

        var queryResult = await controller.ListIntents(
            projectId: "project-1",
            status: "candidate",
            targetChapterId: "chapter-002",
            limit: 20,
            CancellationToken.None);

        var queryEnvelope = await ApplyEnvelopeAsync(queryResult);
        Assert.True(queryEnvelope.Success);
        var query = Assert.IsType<CreativeIntentQueryResult>(queryEnvelope.Data);
        var queried = Assert.Single(query.Items);
        Assert.Equal(created.Id, queried.Id);
        Assert.Contains("怪物围攻", queried.NormalizedIntent);

        var decideResult = await controller.DecideIntent(
            created.Id,
            new DecideCreativeIntentApiRequest
            {
                ProjectId = "project-1",
                Status = "accepted",
                DecisionReason = "用户明确要求当前章节重写",
                ConflictStatus = "none"
            },
            CancellationToken.None);

        var decideEnvelope = await ApplyEnvelopeAsync(decideResult);
        Assert.True(decideEnvelope.Success);
        var decided = Assert.IsType<CreativeIntentItem>(decideEnvelope.Data);
        Assert.Equal("accepted", decided.Status);
        Assert.Contains("章节重写", decided.DecisionReason);
        Assert.NotNull(decided.DecidedAt);
    }

    [Fact]
    public async Task CreateIntent_ReturnsNotFoundWhenProjectDoesNotBelongToCurrentUser()
    {
        await using var db = CreateDb();
        await SeedProjectsAsync(db);
        var controller = CreateController(db, "user-2");

        var result = await controller.CreateIntent(new CreateCreativeIntentApiRequest
        {
            ProjectId = "project-1",
            RawContent = "尝试写入别人的创意收件箱",
            NormalizedIntent = "尝试写入别人的创意收件箱",
            TargetScope = "project"
        }, CancellationToken.None);

        var envelope = await ApplyEnvelopeAsync(result);
        Assert.False(envelope.Success);
        Assert.Equal("PROJECT_NOT_FOUND", envelope.Error!.Code);
        Assert.Empty(await db.CreativeIntents.ToListAsync());
    }

    [Fact]
    public async Task CreateIntent_WithSameIdempotencyKey_ReturnsExistingIntent()
    {
        await using var db = CreateDb();
        await SeedProjectsAsync(db);
        var controller = CreateController(db, "user-1");
        controller.ControllerContext.HttpContext.Request.Headers["Idempotency-Key"] = "creative-key-001";
        var request = new CreateCreativeIntentApiRequest
        {
            ProjectId = "project-1",
            SessionId = "session-1",
            RunId = "run-1",
            RawContent = "第二章改成怪物围攻。",
            NormalizedIntent = "第二章主冲突改为怪物围攻。",
            Source = "chat",
            TargetScope = "chapter",
            TargetChapterId = "chapter-002",
            ImpactLevel = "chapter_rewrite",
            ConflictStatus = "none"
        };

        var firstEnvelope = await ApplyEnvelopeAsync(await controller.CreateIntent(request, CancellationToken.None));
        var secondEnvelope = await ApplyEnvelopeAsync(await controller.CreateIntent(request, CancellationToken.None));

        var first = Assert.IsType<CreativeIntentItem>(firstEnvelope.Data);
        var second = Assert.IsType<CreativeIntentItem>(secondEnvelope.Data);
        Assert.Equal(first.Id, second.Id);
        var intent = await db.CreativeIntents.SingleAsync();
        Assert.Equal("creative-key-001", intent.IdempotencyKey);
    }

    private static CreativeController CreateController(NovelAgentDbContext db, string userId)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(x => x.GetUserId()).Returns(userId);
        currentUser.Setup(x => x.TryGetUserId()).Returns(userId);
        currentUser.Setup(x => x.IsAuthenticated()).Returns(true);
        currentUser.Setup(x => x.IsAdmin()).Returns(false);

        return new CreativeController(new CreativeIntentService(db), currentUser.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static NovelAgentDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<NovelAgentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new NovelAgentDbContext(options);
    }

    private static async Task SeedProjectsAsync(NovelAgentDbContext db)
    {
        db.Users.AddRange(
            new User
            {
                Id = "user-1",
                Username = "author",
                Email = "author@example.com",
                PasswordHash = "hash",
                Role = "author"
            },
            new User
            {
                Id = "user-2",
                Username = "other",
                Email = "other@example.com",
                PasswordHash = "hash",
                Role = "author"
            });
        db.NovelProjects.Add(new NovelProject
        {
            Id = "project-1",
            UserId = "user-1",
            Title = "创意 API 测试书",
            Status = "Writing",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task<ApiEnvelope<object>> ApplyEnvelopeAsync(IActionResult result)
    {
        var filter = new ApiEnvelopeResultFilter();
        var httpContext = new DefaultHttpContext { TraceIdentifier = "creative-test-trace" };
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
}
