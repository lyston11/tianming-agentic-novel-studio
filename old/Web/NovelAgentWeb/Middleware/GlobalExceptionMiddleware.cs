using System.Net.Mime;
using System.Text.Json;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.AgentSessions;

namespace TM.Web.NovelAgentWeb.Middleware;

public sealed class GlobalExceptionMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly RequestDelegate _next;

    public GlobalExceptionMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (KeyNotFoundException ex)
        {
            await WriteFailureAsync(
                context,
                StatusCodes.Status404NotFound,
                "HTTP_404",
                ex.Message,
                recoverable: true);
        }
        catch (AgentChatRequestInProgressException ex)
        {
            context.Response.Headers.RetryAfter = "1";
            await WriteFailureAsync(
                context,
                StatusCodes.Status409Conflict,
                "AGENT_CHAT_IN_PROGRESS",
                ex.Message,
                recoverable: true);
        }
        catch (AgentChatIdempotencyConflictException ex)
        {
            await WriteFailureAsync(
                context,
                StatusCodes.Status409Conflict,
                "AGENT_CHAT_IDEMPOTENCY_CONFLICT",
                ex.Message,
                recoverable: false);
        }
        catch (ArgumentException ex)
        {
            await WriteFailureAsync(
                context,
                StatusCodes.Status400BadRequest,
                "HTTP_400",
                ex.Message,
                recoverable: true);
        }
        catch (InvalidOperationException ex)
        {
            await WriteFailureAsync(
                context,
                StatusCodes.Status400BadRequest,
                "HTTP_400",
                ex.Message,
                recoverable: true);
        }
        catch (Exception ex)
        {
            await WriteFailureAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "HTTP_500",
                ex.Message,
                recoverable: false);
        }
    }

    private static Task WriteFailureAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message,
        bool recoverable)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = MediaTypeNames.Application.Json;
        var envelope = ApiEnvelope<object>.Fail(new ApiErrorDto
        {
            Code = code,
            Message = message,
            Recoverable = recoverable,
            RecommendedAction = recoverable
                ? "请检查请求参数后重试。"
                : "请稍后重试或查看服务日志。"
        }, context.TraceIdentifier);
        return context.Response.WriteAsync(JsonSerializer.Serialize(envelope, JsonOptions));
    }
}
