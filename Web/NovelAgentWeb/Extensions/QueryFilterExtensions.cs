using System.Linq.Expressions;

namespace TM.Web.NovelAgentWeb.Extensions;

/// <summary>
/// Extension methods for applying automatic user-based filtering to queries.
/// Provides data isolation by filtering entities based on UserId property.
/// </summary>
public static class QueryFilterExtensions
{
    /// <summary>
    /// Applies a user filter to a queryable collection.
    /// Automatically adds a .Where(e => e.UserId == userId) clause.
    /// </summary>
    /// <typeparam name="T">The entity type that has a UserId property</typeparam>
    /// <param name="query">The queryable collection to filter</param>
    /// <param name="userId">The user ID to filter by</param>
    /// <returns>Filtered queryable with UserId condition applied</returns>
    /// <exception cref="ArgumentNullException">Thrown when userId is null or empty</exception>
    /// <exception cref="InvalidOperationException">Thrown when entity type doesn't have UserId property</exception>
    public static IQueryable<T> WithUserFilter<T>(this IQueryable<T> query, string userId) where T : class
    {
        if (string.IsNullOrEmpty(userId))
        {
            throw new ArgumentNullException(nameof(userId), "User ID cannot be null or empty");
        }

        // Verify the entity has a UserId property
        var entityType = typeof(T);
        var userIdProperty = entityType.GetProperty("UserId");
        
        if (userIdProperty == null || userIdProperty.PropertyType != typeof(string))
        {
            throw new InvalidOperationException($"Entity type {entityType.Name} does not have a string UserId property");
        }

        // Build expression: e => e.UserId == userId
        var parameter = Expression.Parameter(entityType, "e");
        var property = Expression.Property(parameter, userIdProperty);
        var constant = Expression.Constant(userId);
        var equality = Expression.Equal(property, constant);
        var lambda = Expression.Lambda<Func<T, bool>>(equality, parameter);

        return query.Where(lambda);
    }

    /// <summary>
    /// Applies a user filter to a queryable collection, with admin bypass.
    /// Admins can see all entities, while regular users only see their own.
    /// </summary>
    /// <typeparam name="T">The entity type that has a UserId property</typeparam>
    /// <param name="query">The queryable collection to filter</param>
    /// <param name="userId">The user ID to filter by</param>
    /// <param name="isAdmin">Whether the current user is an admin</param>
    /// <returns>Filtered queryable (or unfiltered if user is admin)</returns>
    public static IQueryable<T> WithUserFilterIfNotAdmin<T>(this IQueryable<T> query, string userId, bool isAdmin) where T : class
    {
        if (isAdmin)
        {
            return query;
        }

        return query.WithUserFilter(userId);
    }

    /// <summary>
    /// Applies a user filter to an enumerable collection.
    /// Useful for in-memory filtering when working with collections.
    /// </summary>
    /// <typeparam name="T">The entity type that has a UserId property</typeparam>
    /// <param name="collection">The collection to filter</param>
    /// <param name="userId">The user ID to filter by</param>
    /// <returns>Filtered enumerable with UserId condition applied</returns>
    public static IEnumerable<T> WhereUserOwns<T>(this IEnumerable<T> collection, string userId) where T : class
    {
        if (string.IsNullOrEmpty(userId))
        {
            throw new ArgumentNullException(nameof(userId), "User ID cannot be null or empty");
        }

        var entityType = typeof(T);
        var userIdProperty = entityType.GetProperty("UserId");
        
        if (userIdProperty == null || userIdProperty.PropertyType != typeof(string))
        {
            throw new InvalidOperationException($"Entity type {entityType.Name} does not have a string UserId property");
        }

        return collection.Where(e =>
        {
            var resourceUserId = userIdProperty.GetValue(e) as string;
            return resourceUserId == userId;
        });
    }
}
