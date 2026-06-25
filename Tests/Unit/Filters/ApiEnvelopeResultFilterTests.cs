using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Filters;
using Xunit;

namespace Tests.Unit.Filters;

public class ApiEnvelopeResultFilterTests
{
    [Fact]
    public async Task OnResultExecutionAsync_WrapsSuccessfulObjectResultInEnvelope()
    {
        var filter = new ApiEnvelopeResultFilter();
        var result = new OkObjectResult(new { title = "第一章" });

        await ExecuteAsync(filter, result);

        var envelope = Assert.IsType<ApiEnvelope<object>>(result.Value);
        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Equal("v1", envelope.ApiVersion);
        Assert.Equal("agent-tools-v1", envelope.ToolSchemaVersion);
        Assert.Equal("agent-loop-v1", envelope.AgentLoopVersion);
        Assert.Equal("agentic-tianming-v1", envelope.KernelVersion);
    }

    [Fact]
    public async Task OnResultExecutionAsync_DoesNotWrapExistingEnvelopeAgain()
    {
        var filter = new ApiEnvelopeResultFilter();
        var existing = ApiEnvelope<string>.Ok("已处理", "request-1");
        var result = new OkObjectResult(existing);

        await ExecuteAsync(filter, result);

        Assert.Same(existing, result.Value);
    }

    [Fact]
    public async Task OnResultExecutionAsync_WrapsClientErrorObjectResultInFailureEnvelope()
    {
        var filter = new ApiEnvelopeResultFilter();
        var result = new BadRequestObjectResult(new { error = "ProjectId is required" });

        await ExecuteAsync(filter, result);

        var envelope = Assert.IsType<ApiEnvelope<object>>(result.Value);
        Assert.False(envelope.Success);
        Assert.Null(envelope.Data);
        Assert.Equal("HTTP_400", envelope.Error!.Code);
        Assert.Equal("ProjectId is required", envelope.Error.Message);
        Assert.True(envelope.Error.Recoverable);
    }

    [Fact]
    public async Task OnResultExecutionAsync_PreservesRequiresUserDecisionInFailureEnvelope()
    {
        var filter = new ApiEnvelopeResultFilter();
        var result = new BadRequestObjectResult(new
        {
            code = "GATE_FAILED",
            message = "章节修改会影响后续事实，需要用户确认。",
            stage = "agent_review",
            recoverable = true,
            recommendedAction = "请确认重写范围。",
            requiresUserDecision = true,
            artifactIds = new[] { "chapter-002", "gate-1" }
        });

        await ExecuteAsync(filter, result);

        var envelope = Assert.IsType<ApiEnvelope<object>>(result.Value);
        Assert.False(envelope.Success);
        Assert.Equal("GATE_FAILED", envelope.Error!.Code);
        Assert.Equal("agent_review", envelope.Error.Stage);
        Assert.True(envelope.Error.Recoverable);
        Assert.True(envelope.Error.RequiresUserDecision);
        Assert.Equal("请确认重写范围。", envelope.Error.RecommendedAction);
        Assert.Equal(new[] { "chapter-002", "gate-1" }, envelope.Error.ArtifactIds);
    }

    [Fact]
    public async Task OnResultExecutionAsync_SkipsNoContentFileAndSseResults()
    {
        var filter = new ApiEnvelopeResultFilter();
        var noContent = new NoContentResult();
        var file = new FileContentResult(Array.Empty<byte>(), "text/plain");
        var sse = new OkObjectResult(new { message = "stream" });

        await ExecuteAsync(filter, noContent);
        await ExecuteAsync(filter, file);
        await ExecuteAsync(filter, sse, "text/event-stream");

        Assert.IsType<NoContentResult>(noContent);
        Assert.IsType<FileContentResult>(file);
        Assert.IsNotType<ApiEnvelope<object>>(sse.Value);
    }

    private static Task ExecuteAsync(
        ApiEnvelopeResultFilter filter,
        IActionResult result,
        string? responseContentType = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.TraceIdentifier = "trace-1";
        if (!string.IsNullOrWhiteSpace(responseContentType))
        {
            httpContext.Response.ContentType = responseContentType;
        }

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());
        var executing = new ResultExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            result,
            controller: new object());

        return filter.OnResultExecutionAsync(
            executing,
            () => Task.FromResult(new ResultExecutedContext(
                actionContext,
                new List<IFilterMetadata>(),
                result,
                controller: new object())));
    }
}
