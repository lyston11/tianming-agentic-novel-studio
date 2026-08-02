using System.Security.Cryptography;
using System.Text;
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
        var inventory = await _knowledge.ListKnowledgeAsync(inventoryProjectId, cancellationToken)
            .ConfigureAwait(false);
        var directories = await _knowledge.ListKnowledgeDirectoriesAsync(cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<KnowledgeSearchResult> matches = [];
        if (request.Intent == KnowledgeQueryIntent.Retrieve && inventory.Count > 0)
        {
            matches = await _knowledge.SearchKnowledgeAsync(new SearchKnowledgeRequest
            {
                ProjectId = projectId,
                Query = request.Query!.Trim(),
                TopK = request.Limit,
                EntryType = request.EntryType
            }, cancellationToken).ConfigureAwait(false);
        }

        var userId = _currentUser.GetUserId();
        var processedVersion = await _db.KnowledgeDocumentBlobs
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.Status == "processed")
            .Select(item => (long?)item.KnowledgeVersion)
            .MaxAsync(cancellationToken)
            .ConfigureAwait(false);
        var knowledgeVersion = processedVersion.HasValue
            ? $"knowledge:{userId}:v{processedVersion.Value}"
            : inventory.Count == 0
                ? "knowledge:empty"
                : $"knowledge:{userId}:manual";
        var catalogRevision = BuildCatalogRevision(inventory);
        var directoryViews = directories
            .Select(directory => new AgentKnowledgeDirectory(
                directory.Key,
                directory.Name,
                directory.EntryCount,
                inventory
                    .Where(item => string.Equals(item.EntryType, directory.Key, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(item => item.Weight)
                    .ThenByDescending(item => item.CreatedAt)
                    .Take(3)
                    .Select(item => item.Title)
                    .ToArray()))
            .ToArray();
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
            catalogRevision,
            request.Query?.Trim() ?? string.Empty,
            inventory.Count,
            directoryViews,
            contextItems,
            request.Intent == KnowledgeQueryIntent.Inventory
                ? inventory.Count > contextItems.Length
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

    private static string BuildCatalogRevision(IReadOnlyList<KnowledgeResponse> inventory)
    {
        if (inventory.Count == 0)
            return "empty";

        var source = string.Join('\n', inventory
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .Select(item => $"{item.Id}|{item.EntryType}|{item.Title}|{item.Content}|{item.Weight}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))
            .ToLowerInvariant()[..12];
    }
}
