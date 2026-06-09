using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Attributes;

/// <summary>
/// Action filter that validates resource ownership based on UserId property.
/// Ensures users can only access their own resources, with admin bypass.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class ValidateUserOwnershipAttribute : ActionFilterAttribute
{
    private readonly string _resourceParameterName;

    /// <summary>
    /// Creates a new ownership validation filter.
    /// </summary>
    /// <param name="resourceParameterName">
    /// The name of the action parameter that contains the resource to validate.
    /// If null, will attempt to find any parameter with a UserId property.
    /// </param>
    public ValidateUserOwnershipAttribute(string? resourceParameterName = null)
    {
        _resourceParameterName = resourceParameterName ?? string.Empty;
    }

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var currentUserService = context.HttpContext.RequestServices.GetRequiredService<ICurrentUserService>();

        // Check if user is authenticated
        if (!currentUserService.IsAuthenticated())
        {
            context.Result = new UnauthorizedObjectResult(new { error = "User is not authenticated" });
            return;
        }

        // Admin users can access any resource
        if (currentUserService.IsAdmin())
        {
            await next();
            return;
        }

        var currentUserId = currentUserService.GetUserId();

        // Find the resource to validate
        object? resource = null;
        if (!string.IsNullOrEmpty(_resourceParameterName))
        {
            context.ActionArguments.TryGetValue(_resourceParameterName, out resource);
        }
        else
        {
            // Try to find any parameter with UserId property
            foreach (var arg in context.ActionArguments.Values)
            {
                if (arg != null && HasUserIdProperty(arg))
                {
                    resource = arg;
                    break;
                }
            }
        }

        // If no resource found, allow the action to proceed
        // (ownership will be validated at service/repository level)
        if (resource == null)
        {
            await next();
            return;
        }

        // Validate ownership
        var resourceUserId = GetUserId(resource);
        if (string.IsNullOrEmpty(resourceUserId))
        {
            context.Result = new BadRequestObjectResult(new { error = "Resource does not have a UserId property" });
            return;
        }

        if (resourceUserId != currentUserId)
        {
            context.Result = new ForbidResult();
            return;
        }

        await next();
    }

    private static bool HasUserIdProperty(object obj)
    {
        var type = obj.GetType();
        var property = type.GetProperty("UserId");
        return property != null && property.PropertyType == typeof(string);
    }

    private static string? GetUserId(object obj)
    {
        var type = obj.GetType();
        var property = type.GetProperty("UserId");
        return property?.GetValue(obj) as string;
    }
}
