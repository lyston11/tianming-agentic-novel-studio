using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TM.Web.NovelAgentWeb.Controllers;
using TM.Web.NovelAgentWeb.Models.Chapters;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Chapters;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Controllers;

public sealed class ChaptersControllerTests
{
    [Fact]
    public async Task CreateChapter_ReadsIdempotencyKeyHeaderIntoRequest()
    {
        CreateChapterRequest? capturedRequest = null;
        var service = new Mock<IChapterService>();
        service
            .Setup(item => item.CreateChapterAsync(
                It.IsAny<CreateChapterRequest>(),
                "user-1",
                false,
                It.IsAny<CancellationToken>()))
            .Callback<CreateChapterRequest, string, bool, CancellationToken>((request, _, _, _) => capturedRequest = request)
            .ReturnsAsync(new ChapterResponse
            {
                Id = "chapter-1",
                ProjectId = "project-1",
                Title = "第一章 旧邮路开站",
                ChapterNumber = 1,
                Status = "published",
                WordCount = 15,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(item => item.GetUserId()).Returns("user-1");
        currentUser.Setup(item => item.IsAdmin()).Returns(false);
        var controller = new ChaptersController(
            service.Object,
            currentUser.Object,
            NullLogger<ChaptersController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.ControllerContext.HttpContext.Request.Headers["Idempotency-Key"] = "chapter-key-001";

        var result = await controller.CreateChapter(new CreateChapterRequest
        {
            ProjectId = "project-1",
            Title = "第一章 旧邮路开站",
            ChapterNumber = 1,
            Content = "沈砚在雾城站台捡起银蓝邮徽。",
            Status = "published"
        });

        Assert.IsType<CreatedAtActionResult>(result);
        Assert.NotNull(capturedRequest);
        Assert.Equal("chapter-key-001", capturedRequest!.IdempotencyKey);
    }

    [Fact]
    public async Task GetChapterVersions_ReturnsVersionListFromService()
    {
        var service = new Mock<IChapterService>();
        service
            .Setup(item => item.GetChapterVersionsAsync(
                "chapter-1",
                "user-1",
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ChapterVersionResponse>
            {
                new()
                {
                    Id = "version-2",
                    ChapterId = "chapter-1",
                    ContentDocumentId = "doc-2",
                    VersionNumber = 2,
                    Title = "第一章 旧邮路开站",
                    WordCount = 42,
                    Status = "published",
                    RuntimeRunId = "run-2",
                    PackageId = "pkg-2",
                    KernelVersion = "tianming-kernel-v1",
                    RebuiltFromPackageIds = { "pkg-1" },
                    IsCurrent = true,
                    ContentPreview = "沈砚看见旧邮车重新亮灯。",
                    CreatedAt = DateTime.UtcNow
                }
            });
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(item => item.GetUserId()).Returns("user-1");
        currentUser.Setup(item => item.IsAdmin()).Returns(false);
        var controller = new ChaptersController(
            service.Object,
            currentUser.Object,
            NullLogger<ChaptersController>.Instance);

        var result = await controller.GetChapterVersions("chapter-1");

        var ok = Assert.IsType<OkObjectResult>(result);
        var versions = Assert.IsAssignableFrom<List<ChapterVersionResponse>>(ok.Value);
        var version = Assert.Single(versions);
        Assert.Equal(2, version.VersionNumber);
        Assert.True(version.IsCurrent);
        Assert.Equal("pkg-2", version.PackageId);
        Assert.Equal(new[] { "pkg-1" }, version.RebuiltFromPackageIds);
    }

    [Fact]
    public async Task CompareChapterVersions_ReturnsDiffFromService()
    {
        var service = new Mock<IChapterService>();
        service
            .Setup(item => item.CompareChapterVersionsAsync(
                "chapter-1",
                "version-1",
                "version-2",
                "user-1",
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChapterVersionCompareResponse
            {
                ChapterId = "chapter-1",
                Left = new ChapterVersionResponse
                {
                    Id = "version-1",
                    ChapterId = "chapter-1",
                    ContentDocumentId = "doc-1",
                    VersionNumber = 1,
                    Title = "第一章",
                    WordCount = 18,
                    Status = "published",
                    ContentPreview = "黑雨刚刚落下。",
                    CreatedAt = DateTime.UtcNow
                },
                Right = new ChapterVersionResponse
                {
                    Id = "version-2",
                    ChapterId = "chapter-1",
                    ContentDocumentId = "doc-2",
                    VersionNumber = 2,
                    Title = "第一章",
                    WordCount = 25,
                    Status = "published",
                    PackageId = "pkg-2",
                    RebuiltFromPackageIds = { "pkg-1" },
                    IsCurrent = true,
                    ContentPreview = "旧邮车重新亮灯。",
                    CreatedAt = DateTime.UtcNow
                },
                Summary = "v1 -> v2：1 处变更。",
                WordCountDelta = 7,
                DiffBlocks =
                {
                    new ChapterVersionDiffBlock
                    {
                        Kind = "changed",
                        LeftText = "黑雨刚刚落下。",
                        RightText = "旧邮车重新亮灯。"
                    }
                }
            });
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(item => item.GetUserId()).Returns("user-1");
        currentUser.Setup(item => item.IsAdmin()).Returns(false);
        var controller = new ChaptersController(
            service.Object,
            currentUser.Object,
            NullLogger<ChaptersController>.Instance);

        var result = await controller.CompareChapterVersions(
            "chapter-1",
            "version-1",
            "version-2");

        var ok = Assert.IsType<OkObjectResult>(result);
        var comparison = Assert.IsType<ChapterVersionCompareResponse>(ok.Value);
        Assert.Equal(7, comparison.WordCountDelta);
        Assert.Equal("pkg-2", comparison.Right.PackageId);
        Assert.Equal("changed", Assert.Single(comparison.DiffBlocks).Kind);
    }

    [Fact]
    public async Task RollbackChapterVersion_ReturnsRollbackResultFromService()
    {
        var chapters = new Mock<IChapterService>();
        chapters
            .Setup(item => item.GetChapterByIdAsync(
                "chapter-1",
                "user-1",
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChapterResponse
            {
                Id = "chapter-1",
                ProjectId = "project-1",
                Title = "第一章",
                ChapterNumber = 1,
                Status = "committed",
                WordCount = 25,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        var rollback = new Mock<IChapterVersionRollbackService>();
        rollback
            .Setup(item => item.RollbackAsync(
                It.Is<RollbackChapterVersionRequest>(request =>
                    request.UserId == "user-1" &&
                    request.ProjectId == "project-1" &&
                    request.ChapterId == "chapter-1" &&
                    request.TargetVersionId == "version-1" &&
                    request.RuntimeRunId.StartsWith("library-rollback:", StringComparison.Ordinal) &&
                    request.Reason == "用户在书城确认回滚。" &&
                    request.IdempotencyKey == "rollback-key-001"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RollbackChapterVersionResult
            {
                Success = true,
                Message = "已回滚到 v1。",
                ProjectId = "project-1",
                ChapterId = "chapter-1",
                CurrentVersionId = "version-1",
                CurrentVersionNumber = 1,
                CurrentDocumentId = "doc-1",
                RuntimeRunId = "library-rollback:version-1",
                InvalidatedPackageIds = { "pkg-2" }
            });
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(item => item.GetUserId()).Returns("user-1");
        currentUser.Setup(item => item.IsAdmin()).Returns(false);
        var controller = new ChaptersController(
            chapters.Object,
            currentUser.Object,
            NullLogger<ChaptersController>.Instance,
            rollback.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.ControllerContext.HttpContext.Request.Headers["Idempotency-Key"] = "rollback-key-001";

        var result = await controller.RollbackChapterVersion(
            "chapter-1",
            "version-1",
            new RollbackChapterVersionApiRequest { Reason = "用户在书城确认回滚。" });

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<RollbackChapterVersionResult>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("version-1", response.CurrentVersionId);
        Assert.Equal(new[] { "pkg-2" }, response.InvalidatedPackageIds);
    }
}
