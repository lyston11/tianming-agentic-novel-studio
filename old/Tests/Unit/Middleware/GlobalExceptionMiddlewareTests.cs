using System.Text.Json;
using Microsoft.AspNetCore.Http;
using TM.Web.NovelAgentWeb.Middleware;
using Xunit;

namespace Tests.Unit.Middleware;

public class GlobalExceptionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_InvalidOperationException_ReturnsUnifiedFailureEnvelope()
    {
        var middleware = new GlobalExceptionMiddleware(_ =>
            throw new InvalidOperationException("Redis 配置缺失"));
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "trace-400";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        var root = json.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("trace-400", root.GetProperty("requestId").GetString());
        Assert.Equal("HTTP_400", root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("Redis 配置缺失", root.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task InvokeAsync_UnhandledException_ReturnsUnifiedFailureEnvelope()
    {
        var middleware = new GlobalExceptionMiddleware(_ =>
            throw new Exception("未知错误"));
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "trace-500";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(context.Response.Body);
        var root = json.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("trace-500", root.GetProperty("requestId").GetString());
        Assert.Equal("HTTP_500", root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("未知错误", root.GetProperty("error").GetProperty("message").GetString());
    }
}
