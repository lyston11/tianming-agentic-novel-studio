using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TM.Web.NovelAgentWeb.Support;

/// <summary>
/// User's intent regarding which project to work on.
/// </summary>
public enum UserProjectIntent
{
    /// <summary>User wants to create a new novel project.</summary>
    CreateNew = 0,

    /// <summary>User wants to continue working on an existing project.</summary>
    ContinueExisting = 1,

    /// <summary>Intent is unclear; need to ask user for clarification.</summary>
    Unresolved = 2,
}

/// <summary>
/// Result of project routing and resolution.
/// </summary>
public sealed class ProjectResolutionResult
{
    /// <summary>True if the router successfully resolved to a project (new or existing).</summary>
    public required bool Success { get; init; }

    /// <summary>True if the router needs to ask the user for clarification.</summary>
    public required bool NeedsClarification { get; init; }

    /// <summary>Message to display to the user when clarification is needed.</summary>
    public string? ClarificationMessage { get; init; }

    /// <summary>The resolved project, if any.</summary>
    public NovelProjectInfo? Project { get; init; }
}

/// <summary>
/// Routes user messages to the correct project context.
/// Determines if user wants to create a new novel or continue an existing one.
/// </summary>
public sealed class ProjectRouter
{
    private readonly NovelProjectCatalog? _catalog;
    private readonly NovelAgentWorkspace? _workspace;

    // Keywords that indicate user wants to create a new project
    private static readonly HashSet<string> NewProjectKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "新书",
        "新小说",
        "创建",
        "开始写",
        "写一本",
        "写一个",
        "新建",
    };

    // Keywords that indicate user wants to continue existing project
    private static readonly HashSet<string> ContinueKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "续写",
        "继续",
        "上一本",
        "之前的",
        "接着写",
        "继续写",
    };

    public ProjectRouter(NovelProjectCatalog? catalog, NovelAgentWorkspace? workspace)
    {
        _catalog = catalog;
        _workspace = workspace;
    }

    /// <summary>
    /// Classifies user intent based on their message.
    /// </summary>
    /// <param name="userMessage">The user's message.</param>
    /// <param name="session">Current session context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The classified intent.</returns>
    public async Task<UserProjectIntent> ClassifyIntentAsync(
        string userMessage,
        SessionContext session,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return UserProjectIntent.Unresolved;
        }

        // Fast path: check for new project keywords
        if (NewProjectKeywords.Any(keyword => userMessage.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return UserProjectIntent.CreateNew;
        }

        // Fast path: check for continue keywords
        if (ContinueKeywords.Any(keyword => userMessage.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return UserProjectIntent.ContinueExisting;
        }

        // Check if message mentions an existing project title
        if (_catalog != null)
        {
            var catalog = await _catalog.GetAsync(cancellationToken);
            foreach (var project in catalog.Projects)
            {
                if (!string.IsNullOrWhiteSpace(project.Title) &&
                    userMessage.Contains(project.Title, StringComparison.OrdinalIgnoreCase))
                {
                    return UserProjectIntent.ContinueExisting;
                }
            }
        }

        // If session already has an active project, default to continuing
        if (!string.IsNullOrWhiteSpace(session.ActiveProjectId))
        {
            return UserProjectIntent.ContinueExisting;
        }

        // Unable to determine intent
        return UserProjectIntent.Unresolved;
    }
}
