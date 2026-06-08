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
    private readonly dynamic? _catalog;
    private readonly dynamic? _workspace;

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

    public ProjectRouter(dynamic? catalog, dynamic? workspace)
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

    /// <summary>
    /// Resolves the user's message to a project by classifying intent and creating or loading accordingly.
    /// </summary>
    /// <param name="userMessage">The user's message.</param>
    /// <param name="session">Current session context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Project resolution result with success status and resolved project.</returns>
    public async Task<ProjectResolutionResult> ResolveProjectAsync(
        string userMessage,
        SessionContext session,
        CancellationToken cancellationToken)
    {
        var intent = await ClassifyIntentAsync(userMessage, session, cancellationToken).ConfigureAwait(false);

        return intent switch
        {
            UserProjectIntent.CreateNew => await CreateNewProjectAsync(session, cancellationToken).ConfigureAwait(false),
            UserProjectIntent.ContinueExisting => await LoadExistingProjectAsync(userMessage, session, cancellationToken).ConfigureAwait(false),
            UserProjectIntent.Unresolved => new ProjectResolutionResult
            {
                Success = false,
                NeedsClarification = true,
                ClarificationMessage = "请问您是要创建新小说，还是续写已有的小说？",
            },
            _ => throw new InvalidOperationException($"Unknown intent: {intent}"),
        };
    }

    /// <summary>
    /// Creates a new project and updates the session.
    /// </summary>
    /// <param name="session">Current session context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Project resolution result with the newly created project.</returns>
    private async Task<ProjectResolutionResult> CreateNewProjectAsync(
        SessionContext session,
        CancellationToken cancellationToken)
    {
        if (_catalog == null || _workspace == null)
        {
            return new ProjectResolutionResult
            {
                Success = false,
                NeedsClarification = true,
                ClarificationMessage = "项目目录服务未初始化。",
            };
        }

        var projectId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var project = new NovelProjectInfo
        {
            Id = projectId,
            Title = $"新小说 {now:yyyy-MM-dd HH:mm}",
            Genre = "未分类",
            StorageProjectName = $"{_workspace!.ProjectName}__novel__{projectId[..8]}",
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _catalog!.AddAsync(project, cancellationToken).ConfigureAwait(false);
        await _catalog!.ActivateAsync(projectId, cancellationToken).ConfigureAwait(false);
        session.ActiveProjectId = projectId;

        return new ProjectResolutionResult
        {
            Success = true,
            NeedsClarification = false,
            Project = project,
        };
    }

    /// <summary>
    /// Loads an existing project by matching title in the message or falling back to active project.
    /// </summary>
    /// <param name="userMessage">The user's message that may contain a project title.</param>
    /// <param name="session">Current session context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Project resolution result with the loaded project.</returns>
    private async Task<ProjectResolutionResult> LoadExistingProjectAsync(
        string userMessage,
        SessionContext session,
        CancellationToken cancellationToken)
    {
        if (_catalog == null)
        {
            return new ProjectResolutionResult
            {
                Success = false,
                NeedsClarification = true,
                ClarificationMessage = "项目目录服务未初始化。",
            };
        }

        NovelProjectInfo? project = null;

        // Try to find project by title in message
        var catalog = await _catalog.GetAsync(cancellationToken).ConfigureAwait(false);
        foreach (var p in catalog.Projects)
        {
            if (!string.IsNullOrWhiteSpace(p.Title) &&
                userMessage.Contains(p.Title, StringComparison.OrdinalIgnoreCase))
            {
                project = p;
                break;
            }
        }

        // Fallback to active project
        if (project == null)
        {
            try
            {
                project = await _catalog.GetActiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // No active project available
            }
        }

        // No project found
        if (project == null)
        {
            return new ProjectResolutionResult
            {
                Success = false,
                NeedsClarification = true,
                ClarificationMessage = "未找到匹配的小说项目。请问您要继续哪一本小说？",
            };
        }

        // Activate and update session
        await _catalog.ActivateAsync(project.Id, cancellationToken).ConfigureAwait(false);
        session.ActiveProjectId = project.Id;

        return new ProjectResolutionResult
        {
            Success = true,
            NeedsClarification = false,
            Project = project,
        };
    }
}
