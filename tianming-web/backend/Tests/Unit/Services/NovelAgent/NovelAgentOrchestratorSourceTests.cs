using Xunit;

namespace Tests.Unit.Services.NovelAgent;

public class NovelAgentOrchestratorSourceTests
{
    [Fact]
    public void GenerateChapterDraftStage_DoesNotClaimChangesWereGeneratedUnconditionally()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "Services/Framework/AI/NovelAgent/Services/NovelAgentOrchestrator.cs"));

        Assert.DoesNotContain("正文草稿和 CHANGES 已生成，等待硬门禁校验。", source);
        Assert.DoesNotContain("章节草稿和 CHANGES 已生成，下一步需要执行硬门禁校验。", source);
        Assert.Contains("DraftArtifact.HasChanges", source);
        Assert.Contains("缺少 CHANGES", source);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            current = current.Parent;
        }

        throw new FileNotFoundException($"Cannot find repository file {relativePath}");
    }
}
