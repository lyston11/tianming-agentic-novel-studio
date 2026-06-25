using System.Net.Mime;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Filters;

public sealed class ApiEnvelopeResultFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (ShouldSkip(context))
        {
            await next();
            return;
        }

        switch (context.Result)
        {
            case ObjectResult objectResult:
                WrapObjectResult(context, objectResult);
                break;
            case JsonResult jsonResult:
                WrapJsonResult(context, jsonResult);
                break;
        }

        await next();
    }

    private static void WrapObjectResult(ResultExecutingContext context, ObjectResult result)
    {
        if (IsEnvelope(result.Value))
        {
            return;
        }

        var statusCode = result.StatusCode ?? StatusCodeFromResult(result) ?? StatusCodes.Status200OK;
        result.StatusCode = statusCode;
        result.Value = BuildEnvelope(result.Value, statusCode, context.HttpContext.TraceIdentifier);
    }

    private static void WrapJsonResult(ResultExecutingContext context, JsonResult result)
    {
        if (IsEnvelope(result.Value))
        {
            return;
        }

        var statusCode = result.StatusCode ?? StatusCodes.Status200OK;
        result.StatusCode = statusCode;
        result.Value = BuildEnvelope(result.Value, statusCode, context.HttpContext.TraceIdentifier);
    }

    private static object BuildEnvelope(object? value, int statusCode, string requestId)
    {
        if (statusCode >= StatusCodes.Status400BadRequest)
        {
            return ApiEnvelope<object>.Fail(new ApiErrorDto
            {
                Code = TryReadStringProperty(value, "code") ?? $"HTTP_{statusCode}",
                Message = ExtractErrorMessage(value, statusCode),
                Stage = TryReadStringProperty(value, "stage") ?? string.Empty,
                Recoverable = TryReadBoolProperty(value, "recoverable")
                    ?? statusCode < StatusCodes.Status500InternalServerError,
                RecommendedAction = TryReadStringProperty(value, "recommendedAction")
                    ?? (statusCode < StatusCodes.Status500InternalServerError
                    ? "请检查请求参数后重试。"
                    : "请稍后重试或查看服务日志。"),
                RequiresUserDecision = TryReadBoolProperty(value, "requiresUserDecision") ?? false,
                ArtifactIds = TryReadStringListProperty(value, "artifactIds") ?? Array.Empty<string>()
            }, requestId);
        }

        return ApiEnvelope<object>.Ok(value!, requestId);
    }

    private static string ExtractErrorMessage(object? value, int statusCode)
    {
        if (value is null)
        {
            return $"HTTP {statusCode}";
        }

        if (value is string message)
        {
            return message;
        }

        if (value is ProblemDetails problem)
        {
            return problem.Detail ?? problem.Title ?? $"HTTP {statusCode}";
        }

        return TryReadStringProperty(value, "message")
               ?? TryReadStringProperty(value, "error")
               ?? $"HTTP {statusCode}";
    }

    private static string? TryReadStringProperty(object? value, string propertyName)
    {
        if (value is null)
        {
            return null;
        }

        var property = value.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));
        return property?.GetValue(value) as string;
    }

    private static IReadOnlyList<string>? TryReadStringListProperty(object? value, string propertyName)
    {
        if (value is null)
        {
            return null;
        }

        var property = value.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));
        return property?.GetValue(value) switch
        {
            IReadOnlyList<string> list => list,
            IEnumerable<string> values => values.ToArray(),
            _ => null
        };
    }

    private static bool? TryReadBoolProperty(object? value, string propertyName)
    {
        if (value is null)
        {
            return null;
        }

        var property = value.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));
        return property?.GetValue(value) as bool?;
    }

    private static bool IsEnvelope(object? value)
    {
        if (value is null)
        {
            return false;
        }

        var type = value.GetType();
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ApiEnvelope<>);
    }

    private static bool ShouldSkip(ResultExecutingContext context)
    {
        if (context.HttpContext.Response.ContentType?.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return context.Result is EmptyResult
            or NoContentResult
            or FileResult
            or ChallengeResult
            or ForbidResult
            or RedirectResult
            or RedirectToActionResult
            or RedirectToRouteResult
            or SignInResult
            or SignOutResult;
    }

    private static int? StatusCodeFromResult(IStatusCodeActionResult result) => result.StatusCode;
}
