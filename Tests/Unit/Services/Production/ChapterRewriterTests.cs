using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ChapterRewriterTests
{
    [Fact]
    public async Task RepairAsync_UsesChangesOnlyPromptAndMergesWithExistingBody()
    {
        IChapterRewriter rewriter = new ChapterRewriter(new ChapterPromptBuilder(new ChapterDirectiveBuilder()));
        var draft = new ChapterDraftArtifact
        {
            ArtifactId = "draft-1",
            DraftContent = """
            第四章正文
            沈砚抵达镜港，发现女子线索指向旧画廊。
            <chapter_changes>
            {"CharacterStateChanges":[{"character":"沈砚"
            """,
            RepairAttemptCount = 2
        };
        var report = new GenerationGateReport
        {
            Issues = { "CHANGES JSON 不是可解析对象。" },
            RepairHints = { "修复 CHANGES JSON 语法，并保留角色、冲突、伏笔等顶级字段。" }
        };
        var request = new ChapterRewriteRequest
        {
            Run = new NovelAgentRun { TargetChapterId = "chapter-004" },
            ContextPackage = new ChapterContextPackageSummary { ChapterId = "chapter-004" },
            Draft = draft,
            GateReport = report,
            CompleteAsync = (system, user, ct) =>
            {
                Assert.Contains("章节修订记录生成模型", system);
                Assert.Contains("repair_chapter_changes_only", user);
                using var doc = JsonDocument.Parse(user);
                Assert.False(doc.RootElement.TryGetProperty("previousDraft", out _));
                return Task.FromResult("""
                <chapter_changes>
                {"CharacterStateChanges":["沈砚进入旧画廊"],"ConflictProgress":[],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":["沈砚抵达镜港"],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}
                </chapter_changes>
                """);
            }
        };

        var repaired = await rewriter.RepairAsync(request);

        Assert.Equal("draft-1", repaired.ArtifactId);
        Assert.Equal("chapter-004", repaired.ChapterId);
        Assert.Equal(3, repaired.RepairAttemptCount);
        Assert.Equal("repairing", repaired.Status);
        Assert.True(repaired.HasChanges);
        Assert.Contains("沈砚抵达镜港", repaired.DraftContent);
        Assert.Contains("chapter_changes", repaired.DraftContent);
        Assert.DoesNotContain("\"character\":\"沈砚\"", repaired.DraftContent);
        Assert.Contains("CharacterStateChanges", repaired.ChangesJson);
    }

    [Fact]
    public async Task RepairAsync_UsesFullRepairPromptForContinuityIssue()
    {
        IChapterRewriter rewriter = new ChapterRewriter(new ChapterPromptBuilder(new ChapterDirectiveBuilder()));
        var report = new GenerationGateReport
        {
            Issues = { "核心连续性失败：未承接「工会追踪者的持续追捕」。" },
            RepairHints = { "开章或关键场景必须回应上一章结尾/必须承接项：工会追踪者的持续追捕" }
        };
        var request = new ChapterRewriteRequest
        {
            Run = new NovelAgentRun { TargetChapterId = "chapter-005" },
            ContextPackage = new ChapterContextPackageSummary
            {
                ChapterId = "chapter-005",
                HardContinuityFacts = { "下一章必须承接：工会追踪者的持续追捕" }
            },
            Draft = new ChapterDraftArtifact
            {
                ArtifactId = "draft-2",
                DraftContent = "旧草稿\n<chapter_changes>{}</chapter_changes>",
                RepairAttemptCount = 0
            },
            GateReport = report,
            CompleteAsync = (system, user, ct) =>
            {
                Assert.Contains("正文写作模型", system);
                Assert.Contains(NovelAgentProductionStages.DraftRepair, user);
                Assert.Contains("previousDraft", user);
                return Task.FromResult("""
                第五章正文
                工会追踪者在雨夜逼近，沈砚被迫转入旧邮路。
                <chapter_changes>{"CharacterStateChanges":[],"ConflictProgress":["工会追踪者持续追捕"],"NewPlotPoints":[],"ForeshadowingActions":[],"LocationStateChanges":[],"FactionStateChanges":[],"TimeProgression":[],"CharacterMovements":[],"ItemTransfers":[],"SecretRevealChanges":[],"PledgeConstraintChanges":[],"DeadlineConstraintChanges":[]}</chapter_changes>
                """);
            }
        };

        var repaired = await rewriter.RepairAsync(request);

        Assert.Equal("draft-2", repaired.ArtifactId);
        Assert.Equal("chapter-005", repaired.ChapterId);
        Assert.Equal(1, repaired.RepairAttemptCount);
        Assert.True(repaired.HasChanges);
        Assert.Contains("工会追踪者持续追捕", repaired.ChangesJson);
    }
}
