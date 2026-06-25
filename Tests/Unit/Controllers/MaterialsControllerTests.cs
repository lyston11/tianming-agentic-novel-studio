using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TM.Web.NovelAgentWeb.Controllers;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Materials;
using Xunit;

namespace Tests.Unit.Controllers;

public sealed class MaterialsControllerTests
{
    [Fact]
    public async Task CreateMaterial_ReadsIdempotencyKeyHeaderIntoRequest()
    {
        CreateMaterialRequest? capturedRequest = null;
        var service = new Mock<IMaterialService>();
        service
            .Setup(item => item.CreateMaterialAsync(
                It.IsAny<CreateMaterialRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<CreateMaterialRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new MaterialResponse
            {
                Id = "material-1",
                UserId = "user-1",
                ProjectId = "project-1",
                Title = "废土邮路素材",
                ContentType = "text/plain",
                CreatedAt = DateTime.UtcNow
            });
        var controller = new MaterialsController(
            service.Object,
            NullLogger<MaterialsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.ControllerContext.HttpContext.Request.Headers["Idempotency-Key"] = "material-key-001";

        var result = await controller.CreateMaterial(new CreateMaterialRequest
        {
            ProjectId = "project-1",
            Title = "废土邮路素材",
            Content = "旧邮路每次开启都会暴露坐标。",
            ContentType = "text/plain"
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(capturedRequest);
        Assert.Equal("material-key-001", capturedRequest!.IdempotencyKey);
    }

    [Fact]
    public async Task UploadMaterial_ReadsIdempotencyKeyHeaderIntoRequest()
    {
        UploadMaterialRequest? capturedRequest = null;
        var service = new Mock<IMaterialService>();
        service
            .Setup(item => item.UploadMaterialAsync(
                It.IsAny<UploadMaterialRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<UploadMaterialRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new MaterialResponse
            {
                Id = "material-1",
                UserId = "user-1",
                ProjectId = "project-1",
                Title = "废土邮路素材.txt",
                ContentType = "text/plain",
                CreatedAt = DateTime.UtcNow
            });
        var controller = new MaterialsController(
            service.Object,
            NullLogger<MaterialsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.ControllerContext.HttpContext.Request.Headers["Idempotency-Key"] = "material-upload-key-001";

        var result = await controller.UploadMaterial(new UploadMaterialRequest
        {
            ProjectId = "project-1",
            Title = "废土邮路素材.txt",
            File = BuildTextFile("废土邮路素材.txt", "旧邮路每次开启都会暴露坐标。")
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(capturedRequest);
        Assert.Equal("material-upload-key-001", capturedRequest!.IdempotencyKey);
    }

    private static IFormFile BuildTextFile(string fileName, string content)
    {
        var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        return new FormFile(stream, 0, stream.Length, "File", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };
    }
}
