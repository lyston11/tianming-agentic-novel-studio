using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public sealed class KnowledgeQueryService : IKnowledgeQueryService
{
    private readonly IKnowledgeService _knowledge;
    private readonly ICurrentUserService _currentUser;
    private readonly NovelAgentDbContext _db;

    public KnowledgeQueryService(
        IKnowledgeService knowledge,
        ICurrentUserService currentUser,
        NovelAgentDbContext db)
    {
        _knowledge = knowledge;
        _currentUser = currentUser;
        _db = db;
    }

    public async Task<KnowledgeQueryResult> QueryAsync(
        KnowledgeQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Limit is < 1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(request), "Knowledge.Query 的 Limit 必须在 1 到 50 之间。");

        var projectId = request.ProjectId?.Trim() ?? string.Empty;
        if (request.Scope == KnowledgeQueryScope.CurrentProject && string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("Knowledge.Query 的 current_project scope 必须提供 ProjectId。", nameof(request));
        if (request.Intent == KnowledgeQueryIntent.Retrieve && string.IsNullOrWhiteSpace(request.Query))
            throw new ArgumentException("Knowledge.Query 的 retrieve intent 必须提供 Query。", nameof(request));
        if (request.Intent == KnowledgeQueryIntent.Retrieve && string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("Knowledge.Query 检索必须提供项目上下文。", nameof(request));

        var inventoryProjectId = request.Scope == KnowledgeQueryScope.CurrentProject ? projectId : string.Empty;
        IReadOnlyList<KnowledgeResponse> inventory = request.Intent == KnowledgeQueryIntent.Inventory
            ? await _knowledge.ListKnowledgeAsync(inventoryProjectId, cancellationToken).ConfigureAwait(false)
            : [];
        var directories = await _knowledge.ListKnowledgeDirectoriesAsync(cancellationToken)
            .ConfigureAwait(false);
        var userId = _currentUser.GetUserId();
        var catalog = await KnowledgeCatalogProjection.GetOrRebuildAsync(_db, userId, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<KnowledgeSearchResult> matches = [];
        if (request.Intent == KnowledgeQueryIntent.Retrieve && catalog.ActiveEntryCount > 0)
        {
            matches = await _knowledge.SearchKnowledgeAsync(new SearchKnowledgeRequest
            {
                ProjectId = projectId,
                Query = request.Query!.Trim(),
                TopK = request.Limit,
                EntryType = request.EntryType
            }, cancellationToken).ConfigureAwait(false);
        }

        var processedVersion = await _db.KnowledgeDocumentBlobs
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.Status == "processed")
            .Select(item => (long?)item.KnowledgeVersion)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);
        var knowledgeVersion = processedVersion.HasValue
            ? $"knowledge:{userId}:v{processedVersion.Value}"
            : catalog.ActiveEntryCount == 0
                ? "knowledge:empty"
                : $"knowledge:{userId}:manual";
        var directoryViews = new List<AgentKnowledgeDirectory>(directories.Count);
        foreach (var directory in directories)
        {
            var titles = await _db.KnowledgeBases.AsNoTracking()
                .Where(item => item.UserId == userId && !item.IsArchived && item.EntryType == directory.Key)
                .OrderByDescending(item => item.Weight)
                .ThenByDescending(item => item.CreatedAt)
                .Select(item => item.Title)
                .Take(3)
                .ToArrayAsync(cancellationToken);
            directoryViews.Add(new AgentKnowledgeDirectory(
                directory.Key,
                directory.Name,
                directory.EntryCount,
                titles));
        }
        var contextItems = request.Intent == KnowledgeQueryIntent.Retrieve
            ? matches.Select(item => new AgentKnowledgeItem(
                    item.Id,
                    item.EntryType,
                    item.Title,
                    Excerpt(item.Content),
                    item.Score,
                    item.SourceType,
                    item.ProjectUsageStatus))
                .ToArray()
            : inventory
                .OrderByDescending(item => item.Weight)
                .ThenByDescending(item => item.CreatedAt)
                .Take(request.Limit)
                .Select(item => new AgentKnowledgeItem(
                    item.Id,
                    item.EntryType,
                    item.Title,
                    Excerpt(item.Content),
                    null,
                    item.SourceType,
                    item.ProjectUsageStatus))
                .ToArray();
        var context = new AgentKnowledgeContext(
            KnowledgeQueryTool.ToolName,
            IntentName(request.Intent),
            ScopeName(request.Scope),
            knowledgeVersion,
            catalog.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.Query?.Trim() ?? string.Empty,
            catalog.ActiveEntryCount,
            directoryViews,
            contextItems,
            request.Intent == KnowledgeQueryIntent.Inventory
                ? catalog.ActiveEntryCount > contextItems.Length
                : matches.Count >= request.Limit);

        return new KnowledgeQueryResult(context, inventory, directories, matches);
    }

    private static string IntentName(KnowledgeQueryIntent intent) => intent switch
    {
        KnowledgeQueryIntent.Inventory => "inventory",
        KnowledgeQueryIntent.Retrieve => "retrieve",
        _ => throw new ArgumentOutOfRangeException(nameof(intent))
    };

    private static string ScopeName(KnowledgeQueryScope scope) => scope switch
    {
        KnowledgeQueryScope.CurrentProject => "current_project",
        KnowledgeQueryScope.UserLibrary => "user_library",
        _ => throw new ArgumentOutOfRangeException(nameof(scope))
    };

    private static string Excerpt(string content)
    {
        var normalized = string.Join(' ', (content ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 240 ? normalized : $"{normalized[..240]}…";
    }

}
