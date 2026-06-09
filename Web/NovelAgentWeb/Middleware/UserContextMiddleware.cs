using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace TM.Web.NovelAgentWeb.Middleware;

/// <summary>
/// Middleware that extracts user context (userId, role) from JWT claims and stores them in HttpContext.Items.
/// This middleware should run after Authentication middleware.
/// </summary>
public class UserContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UserContextMiddleware> _logger;

    // HttpContext.Items keys for storing user context
    public const string UserIdKey = "CurrentUserId";
    public const string UserRoleKey = "CurrentUserRole";

    public UserContextMiddleware(RequestDelegate next, ILogger<UserContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Extract user context from JWT claims if user is authenticated
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            try
            {
                // Extract userId from "sub" claim
                var userId = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                if (!string.IsNullOrEmpty(userId))
                {
                    context.Items[UserIdKey] = userId;
                    _logger.LogDebug("User context extracted: UserId={UserId}", userId);
                }

                // Extract role from ClaimTypes.Role
                var role = context.User.FindFirst(ClaimTypes.Role)?.Value;
                if (!string.IsNullOrEmpty(role))
                {
                    context.Items[UserRoleKey] = role;
                    _logger.LogDebug("User role extracted: Role={Role}", role);
                }
            }
            catch (Exception ex)
            {
                // Log the error but don't block the request
                // The downstream services can handle missing claims appropriately
                _logger.LogWarning(ex, "Failed to extract user context from JWT claims");
            }
        }

        await _next(context);
    }
}
