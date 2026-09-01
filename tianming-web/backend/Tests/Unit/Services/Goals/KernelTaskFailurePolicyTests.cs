using TM.Web.NovelAgentWeb.Services.Goals;
using Xunit;

namespace Tests.Unit.Services.Goals;

public sealed class KernelTaskFailurePolicyTests
{
    [Theory]
    [InlineData("FreezeBaselines", 3)]
    [InlineData("CompileChapterContext", 3)]
    [InlineData("PlanChapter", 2)]
    [InlineData("WriteCandidate", 2)]
    [InlineData("ReviewContinuity", 2)]
    [InlineData("ReviewLiteraryQuality", 2)]
    [InlineData("PrefixMerge", 1)]
    public void MaxAttempts_AreAssignedByTaskSemantics(string taskType, int expected)
    {
        Assert.Equal(expected, KernelTaskFailurePolicy.MaxAttempts(taskType));
    }

    [Fact]
    public void TransientFailure_RetriesWithBackoffWhileAttemptsRemain()
    {
        var decision = KernelTaskFailurePolicy.Decide(
            "CompileChapterContext",
            attempt: 1,
            maxAttempts: 3,
            KernelTaskFailureCategory.Transient);

        Assert.Equal(KernelTaskFailureDisposition.Retry, decision.Disposition);
        Assert.True(decision.RetryAfter > TimeSpan.Zero);
    }

    [Theory]
    [InlineData("WriteCandidate", KernelTaskFailureCategory.Transient)]
    [InlineData("PrefixMerge", KernelTaskFailureCategory.NeedsDecision)]
    public void ExhaustedRecoverableFailure_RequiresHumanDecision(
        string taskType,
        KernelTaskFailureCategory category)
    {
        var maxAttempts = KernelTaskFailurePolicy.MaxAttempts(taskType);
        var decision = KernelTaskFailurePolicy.Decide(taskType, maxAttempts, maxAttempts, category);

        Assert.Equal(KernelTaskFailureDisposition.AwaitingDecision, decision.Disposition);
    }

    [Fact]
    public void FatalContractFailure_TerminatesGoal()
    {
        var decision = KernelTaskFailurePolicy.Decide(
            "WriteCandidate",
            attempt: 1,
            maxAttempts: 2,
            KernelTaskFailureCategory.Fatal);

        Assert.Equal(KernelTaskFailureDisposition.FailGoal, decision.Disposition);
    }
}
