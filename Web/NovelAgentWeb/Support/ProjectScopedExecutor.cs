namespace TM.Web.NovelAgentWeb.Support;

public sealed class ProjectScopedExecutor
{
    private readonly NovelProjectCatalog _catalog;

    public ProjectScopedExecutor(NovelProjectCatalog catalog) => _catalog = catalog;

    public async Task<T> RunActiveAsync<T>(Func<Task<T>> operation, CancellationToken ct = default)
    {
        var project = await _catalog.GetActiveAsync(ct).ConfigureAwait(false);
        return await _catalog.WithProjectAsync(project, operation, ct).ConfigureAwait(false);
    }

    public async Task<T> RunProjectAsync<T>(string? projectId, Func<Task<T>> operation, CancellationToken ct = default)
    {
        var project = !string.IsNullOrWhiteSpace(projectId)
            ? await _catalog.FindAsync(projectId, ct).ConfigureAwait(false)
            : null;
        project ??= await _catalog.GetActiveAsync(ct).ConfigureAwait(false);
        return await _catalog.WithProjectAsync(project, operation, ct).ConfigureAwait(false);
    }

    public async Task<T> RunSessionAsync<T>(AgentSession session, Func<Task<T>> operation, CancellationToken ct = default)
    {
        var project = !string.IsNullOrWhiteSpace(session.ActiveProjectId)
            ? await _catalog.FindAsync(session.ActiveProjectId, ct).ConfigureAwait(false)
            : null;
        project ??= await _catalog.GetActiveAsync(ct).ConfigureAwait(false);
        session.ActiveProjectId = project.Id;
        return await _catalog.WithProjectAsync(project, operation, ct).ConfigureAwait(false);
    }
}
