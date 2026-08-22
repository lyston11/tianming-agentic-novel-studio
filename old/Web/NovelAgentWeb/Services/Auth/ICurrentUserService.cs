namespace TM.Web.NovelAgentWeb.Services.Auth;

/// <summary>
/// Provides access to the current authenticated user's context from JWT claims.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>
    /// Gets the current user's ID from JWT claims.
    /// </summary>
    /// <returns>User ID if authenticated.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when user is not authenticated.</exception>
    string GetUserId();

    /// <summary>
    /// Gets the current user's username from JWT claims.
    /// </summary>
    /// <returns>Username if authenticated.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when user is not authenticated.</exception>
    string GetUsername();

    /// <summary>
    /// Gets the current user's email from JWT claims.
    /// </summary>
    /// <returns>Email if authenticated.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when user is not authenticated.</exception>
    string GetEmail();

    /// <summary>
    /// Gets the current user's role from JWT claims.
    /// </summary>
    /// <returns>User role (e.g., "author", "admin") if authenticated.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when user is not authenticated.</exception>
    string GetRole();

    /// <summary>
    /// Checks if the current user has the admin role.
    /// </summary>
    /// <returns>True if user is admin, false otherwise.</returns>
    bool IsAdmin();

    /// <summary>
    /// Checks if the current request has an authenticated user.
    /// </summary>
    /// <returns>True if authenticated, false otherwise.</returns>
    bool IsAuthenticated();

    /// <summary>
    /// Tries to get the current user's ID without throwing an exception.
    /// </summary>
    /// <returns>User ID if authenticated, null otherwise.</returns>
    string? TryGetUserId();
}
