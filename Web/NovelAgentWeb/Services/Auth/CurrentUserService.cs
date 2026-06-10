using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace TM.Web.NovelAgentWeb.Services.Auth;

/// <summary>
/// Implementation of ICurrentUserService that extracts user context from HttpContext.User claims.
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string GetUserId()
    {
        var userId = TryGetUserId();
        if (string.IsNullOrEmpty(userId))
        {
            throw new UnauthorizedAccessException("User is not authenticated");
        }
        return userId;
    }

    public string GetUsername()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated != true)
        {
            throw new UnauthorizedAccessException("User is not authenticated");
        }

        var username = httpContext.User.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrEmpty(username))
        {
            throw new InvalidOperationException("Username claim not found in JWT");
        }

        return username;
    }

    public string GetEmail()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated != true)
        {
            throw new UnauthorizedAccessException("User is not authenticated");
        }

        var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrEmpty(email))
        {
            throw new InvalidOperationException("Email claim not found in JWT");
        }

        return email;
    }

    public string GetRole()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated != true)
        {
            throw new UnauthorizedAccessException("User is not authenticated");
        }

        var role = httpContext.User.FindFirst(ClaimTypes.Role)?.Value;
        if (string.IsNullOrEmpty(role))
        {
            throw new InvalidOperationException("User role claim not found in JWT");
        }

        return role;
    }

    public bool IsAdmin()
    {
        if (!IsAuthenticated())
        {
            return false;
        }

        try
        {
            var role = GetRole();
            return string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public bool IsAuthenticated()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        return httpContext?.User?.Identity?.IsAuthenticated == true;
    }

    public string? TryGetUserId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        // JWT "sub" claim is mapped to NameIdentifier by ASP.NET Core JWT middleware
        return httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }
}
