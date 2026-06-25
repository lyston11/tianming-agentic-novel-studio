namespace TM.Web.NovelAgentWeb.Services.Production;

public interface IRepositoryBoundaryGuard
{
    RepositoryBoundaryReport Scan(string repositoryRoot);
    void EnsureClean(string repositoryRoot);
}

public sealed record RepositoryBoundaryReport(IReadOnlyList<RepositoryBoundaryViolation> Violations)
{
    public bool IsClean => Violations.Count == 0;
}

public sealed record RepositoryBoundaryViolation(
    string Source,
    string Rule,
    string Message);
