using System.Net.Mime;
using System.Text.Json;

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
        catch (InvalidOperationException ex)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = MediaTypeNames.Application.Json;
            await context.Response.WriteAsync(JsonSerializer.Serialize(
                new { error = ex.Message }, JsonOptions));
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = MediaTypeNames.Application.Json;
            await context.Response.WriteAsync(JsonSerializer.Serialize(
                new { error = ex.Message }, JsonOptions));
        }
    }
}
