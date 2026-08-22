using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TM.Web.NovelAgentWeb.Controllers;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Workflow;
using Xunit;

namespace Tests.Unit.Controllers;

public sealed class WorkflowControllerTests
{
    [Fact]
    public async Task CreateVolumeArc_ReadsIdempotencyKeyHeaderIntoRequest()
    {
        CreateVolumeArcRequest? capturedRequest = null;
        var service = new Mock<IWorkflowService>();
        service
            .Setup(item => item.CreateVolumeArcAsync(
                It.IsAny<CreateVolumeArcRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<CreateVolumeArcRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new VolumeArcResponse
            {
                Id = "volume-arc-1",
                UserId = "user-1",
                ProjectId = "project-1",
                VolumeNumber = 1,
                VolumeTitle = "第一卷 雾城邮路",
                CurrentChapters = 0,
                Status = "planned",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        var controller = new WorkflowController(
            service.Object,
            NullLogger<WorkflowController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.ControllerContext.HttpContext.Request.Headers["Idempotency-Key"] = "volume-arc-key-001";

        var result = await controller.CreateVolumeArc(new CreateVolumeArcRequest
        {
            ProjectId = "project-1",
            VolumeNumber = 1,
            VolumeTitle = "第一卷 雾城邮路"
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(capturedRequest);
        Assert.Equal("volume-arc-key-001", capturedRequest!.IdempotencyKey);
    }
}
