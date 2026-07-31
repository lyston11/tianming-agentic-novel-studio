namespace TM.Web.NovelAgentWeb.Services.Rag;

public sealed class QueryPlanner : IQueryPlanner
{
    private readonly IRagQueryPlanningModelClient _model;

    public QueryPlanner(IRagQueryPlanningModelClient model)
    {
        _model = model;
    }

    public async Task<RagQueryPlan> PlanAsync(
        RagQueryPlanningRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserMessage);

        var plan = await _model.PlanAsync(request, cancellationToken);
        var routes = plan.Routes.Distinct().ToArray();
        var semanticQueries = Normalize(plan.SemanticQueries);
        if (routes.Length == 0)
            throw new InvalidOperationException("RAG 查询规划模型没有返回检索路由。");
        if (semanticQueries.Length == 0)
            throw new InvalidOperationException("RAG 查询规划模型没有返回语义查询。");

        return plan with
        {
            Routes = routes,
            SemanticQueries = semanticQueries,
            EntityReferences = Normalize(plan.EntityReferences),
            TargetChapterIds = Normalize(plan.TargetChapterIds)
        };
    }

    private static string[] Normalize(IEnumerable<string> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}
