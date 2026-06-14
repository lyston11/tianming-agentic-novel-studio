using TM.Web.NovelAgentWeb.Support;

namespace TM.Tests.NovelAgentRegression;

internal static class TestNovelAgentWorkspaceFactory
{
    public static NovelAgentWorkspace Create(string? storageRoot = null, string projectName = "NovelAgentRegression")
    {
        storageRoot ??= Path.Combine(Path.GetTempPath(), "novelagent-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);

        return new NovelAgentWorkspace
        {
            ProjectName = projectName,
            StorageRoot = storageRoot,
            Orchestrator = null!,
        };
    }

    public static void SeedCatalog(NovelAgentWorkspace workspace)
    {
        _ = workspace;
    }

    public static void BindWorkspace(NovelAgentWorkspace workspace)
    {
        PhaseContextBuilder.SetWorkspace(workspace);
    }

    public static void ClearWorkspace()
    {
        PhaseContextBuilder.ClearWorkspace();
    }
}
