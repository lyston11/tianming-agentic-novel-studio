using System.Text.Json;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.Kernels;
using TM.Web.NovelAgentWeb.Services.Quality;
using Xunit;

namespace Tests.Unit.Services.Quality;

public sealed class DualChannelReviewTests
{
    [Fact]
    public async Task ContinuityKernel_TreatsRuleFindingAsSuspicionWhenSemanticEvidenceExplainsIt()
    {
        var semantic = new StubContinuityModel(new ContinuityReviewDecision(
            ReviewVerdict.Pass,
            [new ContinuityClaimDecision(
                "人物说自己从未来回来",
                ["draft:paragraph-3"],
                ["canon:chapter-1"],
                "角色正在撒谎，叙述层没有把它当作事实",
                0.94,
                "explained")],
            []));
        var detector = new StubViolationDetector(
            [new PotentialViolation("identity", "角色身份表述与正史不同", "draft:paragraph-3", "high")]);
        var kernel = new ContinuityReviewKernel(detector, semantic);

        var output = await kernel.ExecuteAsync(Context(
            "continuity_review",
            "ReviewContinuity",
            Draft("角色宣称自己从未来回来。"),
            Evidence("正史中角色出生于当代。")));

        var review = Deserialize<ContinuityReviewArtifact>(Assert.Single(output.Artifacts).ContentJson);
        Assert.Equal(ReviewVerdict.Pass, review.Verdict);
        Assert.Single(review.PotentialViolations);
        Assert.Equal("explained", Assert.Single(review.Claims).Verdict);
    }

    [Fact]
    public async Task ContinuityKernel_CanFindSemanticContradictionMissingFromRules()
    {
        var semantic = new StubContinuityModel(new ContinuityReviewDecision(
            ReviewVerdict.ReworkRequired,
            [new ContinuityClaimDecision(
                "角色在同一时刻出现在两地",
                ["draft:paragraph-8"],
                ["canon:chapter-20"],
                "没有闪回、分身或误导叙述证据",
                0.98,
                "contradiction")],
            ["调整场景时间或地点"]));
        var kernel = new ContinuityReviewKernel(new StubViolationDetector([]), semantic);

        var output = await kernel.ExecuteAsync(Context(
            "continuity_review",
            "ReviewContinuity",
            Draft("同一时刻他同时在城南和城北。"),
            Evidence("前文明确两地相距三日路程。")));

        Assert.Equal(
            ReviewVerdict.ReworkRequired,
            Deserialize<ContinuityReviewArtifact>(Assert.Single(output.Artifacts).ContentJson).Verdict);
    }

    [Fact]
    public async Task LiteraryKernel_ReportsDimensionsWithoutTurningSubjectiveTasteIntoHardFailure()
    {
        var model = new StubLiteraryModel(new LiteraryReviewDecision(
            ReviewVerdict.PassWithSuggestions,
            new Dictionary<string, LiteraryDimensionReview>
            {
                ["pacing"] = new("节奏略慢", ["draft:paragraph-5"], "可压缩环境描写"),
                ["characterCredibility"] = new("人物选择可信", ["draft:paragraph-9"], "")
            },
            ["压缩中段两段环境描写"]));
        var kernel = new LiteraryReviewKernel(model);

        var output = await kernel.ExecuteAsync(Context(
            "literary_review",
            "ReviewLiteraryQuality",
            Draft("正文内容")));

        var review = Deserialize<LiteraryReviewDecision>(Assert.Single(output.Artifacts).ContentJson);
        Assert.Equal(ReviewVerdict.PassWithSuggestions, review.Verdict);
        Assert.Equal(2, review.Dimensions.Count);
        Assert.False(review.Dimensions.ContainsKey("overallScore"));
    }

    private static KernelExecutionContext Context(
        string kernel,
        string taskType,
        params KernelInputArtifact[] inputs) => new(
            new KernelTaskClaim(
                "task-1",
                "user-1",
                "project-1",
                "goal-1",
                "graph-1",
                "branch-1",
                kernel,
                taskType,
                1,
                "worker-1",
                DateTime.UtcNow.AddMinutes(2)),
            new GoalContextSnapshot
            {
                Id = "snapshot-1",
                UserId = "user-1",
                ProjectId = "project-1",
                GoalId = "goal-1",
                QualityContractVersion = "quality-1"
            },
            new KernelGoalContract(
                "write_batch",
                "coauthor",
                "写第一章",
                "{\"start\":1,\"end\":1}",
                "[]",
                "[]",
                "[]",
                "[]",
                "{}",
                "{}"),
            inputs);

    private static KernelInputArtifact Draft(string content)
    {
        var draft = new ChapterDraftArtifact { ChapterId = "chapter-1", DraftContent = content };
        return new KernelInputArtifact("draft-1", "CandidateChapterDraft", 1, JsonSerializer.Serialize(draft), "hash-draft");
    }

    private static KernelInputArtifact Evidence(string content) =>
        new("evidence-1", "EvidenceBundle", 1, JsonSerializer.Serialize(new { content }), "hash-evidence");

    private static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json) ?? throw new InvalidOperationException("JSON deserialize failed.");

    private sealed class StubViolationDetector(IReadOnlyList<PotentialViolation> result) : IPotentialViolationDetector
    {
        public IReadOnlyList<PotentialViolation> Detect(ContinuityReviewRequest request) => result;
    }

    private sealed class StubContinuityModel(ContinuityReviewDecision result) : IContinuityReviewModelClient
    {
        public Task<ContinuityReviewDecision> ReviewAsync(
            ContinuityReviewRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class StubLiteraryModel(LiteraryReviewDecision result) : ILiteraryReviewModelClient
    {
        public Task<LiteraryReviewDecision> ReviewAsync(
            LiteraryReviewRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
}
