using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TM.Web.NovelAgentWeb.Controllers;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.StoryBible;
using Xunit;

namespace Tests.Unit.Controllers;

public sealed class StoryBibleControllerTests
{
    [Fact]
    public async Task CreateConstitution_ReadsIdempotencyKeyHeaderIntoRequest()
    {
        CreateStoryConstitutionRequest? capturedRequest = null;
        var service = new Mock<IStoryBibleService>();
        service
            .Setup(item => item.CreateConstitutionAsync(
                It.IsAny<CreateStoryConstitutionRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<CreateStoryConstitutionRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new StoryConstitutionResponse
            {
                Id = "constitution-1",
                UserId = "user-1",
                ProjectId = "project-1",
                Genre = "末世",
                CoreHook = "旧邮路每开启一次都会暴露坐标。",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        var controller = CreateController(service);
        controller.ControllerContext.HttpContext.Request.Headers["Idempotency-Key"] = "constitution-key-001";

        var result = await controller.CreateConstitution(new CreateStoryConstitutionRequest
        {
            ProjectId = "project-1",
            Genre = "末世",
            CoreHook = "旧邮路每开启一次都会暴露坐标。"
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(capturedRequest);
        Assert.Equal("constitution-key-001", capturedRequest!.IdempotencyKey);
    }

    [Fact]
    public async Task CreateCharacter_ReadsIdempotencyKeyHeaderIntoRequest()
    {
        CreateCharacterRequest? capturedRequest = null;
        var service = new Mock<IStoryBibleService>();
        service
            .Setup(item => item.CreateCharacterAsync(
                It.IsAny<CreateCharacterRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<CreateCharacterRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(new CharacterResponse
            {
                Id = "character-1",
                UserId = "user-1",
                ProjectId = "project-1",
                Name = "林昼",
                Role = "protagonist",
                Status = "active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        var controller = CreateController(service);
        controller.ControllerContext.HttpContext.Request.Headers["Idempotency-Key"] = "character-key-001";

        var result = await controller.CreateCharacter(new CreateCharacterRequest
        {
            ProjectId = "project-1",
            Name = "林昼",
            Role = "protagonist"
        }, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(capturedRequest);
        Assert.Equal("character-key-001", capturedRequest!.IdempotencyKey);
    }

    private static StoryBibleController CreateController(Mock<IStoryBibleService> service)
    {
        return new StoryBibleController(
            service.Object,
            NullLogger<StoryBibleController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }
}
