using System.Text.Json;
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
        var dir = Path.Combine(workspace.StorageRoot, "Projects", workspace.ProjectName, "NovelProjects");
        Directory.CreateDirectory(dir);
        var project = new NovelProjectInfo
        {
            Id = "existing-project",
            Title = "既有测试小说",
            Genre = "Regression",
            StorageProjectName = $"{workspace.ProjectName}__existing",
            Status = "Drafting",
        };
        var document = new NovelProjectCatalogDocument
        {
            ActiveProjectId = project.Id,
            Projects = new List<NovelProjectInfo> { project },
        };
        File.WriteAllText(
            Path.Combine(dir, "projects.json"),
            JsonSerializer.Serialize(document, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            }));
    }

    public static void BindWorkspace(NovelAgentWorkspace workspace)
    {
        AgentMemoryService.SetWorkspace(workspace);
        PhaseContextBuilder.SetWorkspace(workspace);
    }

    public static void ClearWorkspace()
    {
        AgentMemoryService.ClearWorkspace();
        PhaseContextBuilder.ClearWorkspace();
    }
}
