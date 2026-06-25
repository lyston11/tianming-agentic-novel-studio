using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TM.Web.NovelAgentWeb.Controllers;
using TM.Web.NovelAgentWeb.Models.Projects;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Projects;
using Xunit;

namespace Tests.Unit.Controllers;

public sealed class ProjectControllerTests
{
    [Fact]
    public async Task CreateProject_ReadsIdempotencyKeyHeaderIntoRequest()
    {
        CreateProjectRequest? capturedRequest = null;
        var service = new Mock<IProjectService>();
        service
            .Setup(item => item.CreateProjectAsync(
                It.IsAny<CreateProjectRequest>(),
                "user-1",
                It.IsAny<CancellationToken>()))
            .Callback<CreateProjectRequest, string, CancellationToken>((request, _, _) => capturedRequest = request)
            .ReturnsAsync(new ProjectResponse
            {
                Id = "project-1",
                UserId = "user-1",
                Title = "雾城邮路",
                Status = "draft",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(item => item.GetUserId()).Returns("user-1");
        var controller = new ProjectController(
            service.Object,
            currentUser.Object,
            NullLogger<ProjectController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.ControllerContext.HttpContext.Request.Headers["Idempotency-Key"] = "project-key-001";

        var result = await controller.CreateProject(new CreateProjectRequest
        {
            Title = "雾城邮路"
        });

        Assert.IsType<CreatedAtActionResult>(result);
        Assert.NotNull(capturedRequest);
        Assert.Equal("project-key-001", capturedRequest!.IdempotencyKey);
    }
}
