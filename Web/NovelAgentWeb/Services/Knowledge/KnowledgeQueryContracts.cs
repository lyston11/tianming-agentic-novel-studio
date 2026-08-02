using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public enum KnowledgeQueryIntent
{
    Inventory,
    Retrieve
}

public enum KnowledgeQueryScope
{
    CurrentProject,
    UserLibrary
}

public sealed record KnowledgeQueryRequest(
    KnowledgeQueryIntent Intent,
    KnowledgeQueryScope Scope,
    string? ProjectId,
    string? Query = null,
    string? EntryType = null,
    int Limit = 12);

public sealed record KnowledgeQueryResult(
    AgentKnowledgeContext Context,
    IReadOnlyList<KnowledgeResponse> Inventory,
    IReadOnlyList<KnowledgeDirectoryResponse> Directories,
    IReadOnlyList<KnowledgeSearchResult> Matches);

public interface IKnowledgeQueryService
{
    Task<KnowledgeQueryResult> QueryAsync(
        KnowledgeQueryRequest request,
        CancellationToken cancellationToken = default);
}

public interface IKnowledgeQueryTool
{
    string Name { get; }

    Task<KnowledgeQueryResult> ExecuteAsync(
        KnowledgeQueryRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class KnowledgeQueryTool : IKnowledgeQueryTool
{
    public const string ToolName = "Knowledge.Query";
    private readonly IKnowledgeQueryService _queries;

    public KnowledgeQueryTool(IKnowledgeQueryService queries)
    {
        _queries = queries;
    }

    public string Name => ToolName;

    public Task<KnowledgeQueryResult> ExecuteAsync(
        KnowledgeQueryRequest request,
        CancellationToken cancellationToken = default) =>
        _queries.QueryAsync(request, cancellationToken);
}
