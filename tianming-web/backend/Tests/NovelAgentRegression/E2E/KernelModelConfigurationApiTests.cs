using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TM.Web.NovelAgentWeb.Controllers;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Models.Auth;
using TM.Web.NovelAgentWeb.Models.Projects;
using Xunit;

namespace TM.Tests.NovelAgentRegression.E2E;

public sealed class KernelModelConfigurationApiTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public KernelModelConfigurationApiTests(TestWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task InvalidKernelModelConfiguration_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");
        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Username = $"kernel{suffix}",
            Email = $"kernel-{suffix}@test.com",
            Password = "SecurePass123!"
        });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        var auth = await registerResponse.Content.ReadEnvelopeDataAsync<AuthResponse>();
        Assert.NotNull(auth);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var projectResponse = await client.PostAsJsonAsync("/api/projects", new CreateProjectRequest
        {
            Title = "Kernel model validation project"
        });
        Assert.Equal(HttpStatusCode.Created, projectResponse.StatusCode);
        var project = await projectResponse.Content.ReadEnvelopeDataAsync<ProjectResponse>();
        Assert.NotNull(project);

        var unknownGet = await client.GetAsync(
            $"/api/settings/projects/{project.Id}/kernel-models/unknown-kernel");
        await AssertInvalidConfigurationAsync(unknownGet);

        var unknownPut = await client.PutAsJsonAsync(
            $"/api/settings/projects/{project.Id}/kernel-models/unknown-kernel",
            ValidConfiguration());
        await AssertInvalidConfigurationAsync(unknownPut);

        var missingBaseUrl = await client.PutAsJsonAsync(
            $"/api/settings/projects/{project.Id}/kernel-models/setting",
            ValidConfiguration() with { BaseUrl = null });
        await AssertInvalidConfigurationAsync(missingBaseUrl);
    }

    private static KernelModelConfigurationDto ValidConfiguration() => new(
        Provider: "openai",
        BaseUrl: "https://models.example/v1",
        Model: "model-a");

    private static async Task AssertInvalidConfigurationAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<object>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.Success);
        Assert.Equal("KERNEL_MODEL_CONFIGURATION_INVALID", envelope.Error?.Code);
        Assert.True(envelope.Error?.Recoverable);
    }
}
