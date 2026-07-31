using Moq;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Execution;
using TM.Web.NovelAgentWeb.Services.Goals;
using TM.Web.NovelAgentWeb.Services.Kernels;
using TM.Web.NovelAgentWeb.Services.Models;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Execution;

public sealed class DefaultKernelStructuredModelClientBudgetTests
{
    [Fact]
    public async Task GenerateAsync_ReservesBeforeModelCallAndSettlesSuccessfulExecution()
    {
        var sequence = new MockSequence();
        var configurations = new Mock<IKernelModelConfigurationService>(MockBehavior.Strict);
        configurations.InSequence(sequence)
            .Setup(service => service.ResolveAsync(
                "user-1", "project-1", "setting", "balanced", "goal-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Configuration());
        var budget = new Mock<IGoalBudgetService>(MockBehavior.Strict);
        budget.InSequence(sequence)
            .Setup(service => service.FindReusableResultAsync(
                "user-1",
                "task-1",
                "setting",
                "setting:v2",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModelExecutionResultReceipt?)null);
        budget.InSequence(sequence)
            .Setup(service => service.ReserveAsync(
                It.Is<GoalBudgetReservationRequest>(request =>
                    request.GoalId == "goal-1" && request.WorstCaseCost > 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoalBudgetReservationResult(true, "execution-1", 1m, 9m));
        budget.InSequence(sequence)
            .Setup(service => service.MarkProviderCallStartedAsync(
                "user-1",
                "execution-1",
                It.Is<string>(owner => !string.IsNullOrWhiteSpace(owner)),
                TimeSpan.FromSeconds(60),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var completion = new Mock<IWritingModelCompletionService>(MockBehavior.Strict);
        completion.InSequence(sequence)
            .Setup(service => service.CompleteWithMetadataAsync(
                "user-1",
                It.IsAny<ResolvedKernelModelConfiguration>(),
                It.IsAny<string>(),
                It.Is<string>(prompt =>
                    prompt.Contains("HumanReadableObjective", StringComparison.Ordinal) &&
                    prompt.Contains("write_batch", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WritingModelCompletionResult(
                "{\"result\":\"ok\"}",
                "fallback-provider",
                "fallback-model",
                "provider-request-1",
                120,
                30,
                3m,
                9m));
        budget.InSequence(sequence)
            .Setup(service => service.RecordResultAsync(
                It.Is<ModelExecutionResultRecord>(record =>
                    record.ExecutionId == "execution-1" &&
                    record.ActualCost > 0 &&
                    record.InputTokens == 120 &&
                    record.OutputTokens == 30 &&
                    record.Provider == "fallback-provider" &&
                    record.Model == "fallback-model" &&
                    record.ProviderRequestId == "provider-request-1" &&
                    !string.IsNullOrWhiteSpace(record.ProviderLeaseOwner) &&
                    record.ResultContentHash.Length == 64),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        budget.InSequence(sequence)
            .Setup(service => service.SettleAsync(
                "user-1",
                "execution-1",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var client = new DefaultKernelStructuredModelClient(
            completion.Object,
            configurations.Object,
            new KernelPromptAssembler(),
            budget.Object);

        var result = await client.GenerateAsync("setting", Context());

        Assert.Equal("{\"result\":\"ok\"}", result);
        configurations.VerifyAll();
        completion.VerifyAll();
        budget.VerifyAll();
    }

    [Fact]
    public async Task GenerateAsync_DoesNotCallModelWhenWorstCaseReservationIsDenied()
    {
        var configurations = new Mock<IKernelModelConfigurationService>();
        configurations.Setup(service => service.ResolveAsync(
                "user-1", "project-1", "setting", "balanced", "goal-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Configuration());
        var budget = new Mock<IGoalBudgetService>();
        budget.Setup(service => service.ReserveAsync(
                It.IsAny<GoalBudgetReservationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoalBudgetReservationResult(false, null, 0, 0));
        var completion = new Mock<IWritingModelCompletionService>(MockBehavior.Strict);
        var client = new DefaultKernelStructuredModelClient(
            completion.Object,
            configurations.Object,
            new KernelPromptAssembler(),
            budget.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GenerateAsync("setting", Context()));

        completion.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GenerateAsync_ReusesPersistedResultWhenSettlementRetryFollowsProviderSuccess()
    {
        var configurations = new Mock<IKernelModelConfigurationService>(MockBehavior.Strict);
        configurations.Setup(service => service.ResolveAsync(
                "user-1", "project-1", "setting", "balanced", "goal-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Configuration());
        var persisted = new ModelExecutionResultReceipt(
            "execution-1",
            "{\"result\":\"ok\"}",
            RequiresSettlement: true);
        var budget = new Mock<IGoalBudgetService>(MockBehavior.Strict);
        budget.SetupSequence(service => service.FindReusableResultAsync(
                "user-1",
                "task-1",
                "setting",
                "setting:v2",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModelExecutionResultReceipt?)null)
            .ReturnsAsync(persisted);
        budget.Setup(service => service.ReserveAsync(
                It.IsAny<GoalBudgetReservationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoalBudgetReservationResult(true, "execution-1", 1m, 9m));
        budget.Setup(service => service.MarkProviderCallStartedAsync(
                "user-1",
                "execution-1",
                It.Is<string>(owner => !string.IsNullOrWhiteSpace(owner)),
                TimeSpan.FromSeconds(60),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        budget.Setup(service => service.RecordResultAsync(
                It.Is<ModelExecutionResultRecord>(result =>
                    result.ExecutionId == "execution-1" &&
                    result.ResultJson == "{\"result\":\"ok\"}"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        budget.SetupSequence(service => service.SettleAsync(
                "user-1",
                "execution-1",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("结算提交结果未知"))
            .Returns(Task.CompletedTask);
        var completion = new Mock<IWritingModelCompletionService>(MockBehavior.Strict);
        completion.Setup(service => service.CompleteWithMetadataAsync(
                "user-1",
                It.IsAny<ResolvedKernelModelConfiguration>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WritingModelCompletionResult(
                "{\"result\":\"ok\"}",
                "openai",
                "test-model",
                "provider-request-2",
                100,
                20,
                2m,
                8m));
        var client = new DefaultKernelStructuredModelClient(
            completion.Object,
            configurations.Object,
            new KernelPromptAssembler(),
            budget.Object);

        await Assert.ThrowsAsync<TimeoutException>(() => client.GenerateAsync("setting", Context()));
        var retried = await client.GenerateAsync("setting", Context());

        Assert.Equal("{\"result\":\"ok\"}", retried);
        completion.Verify(service => service.CompleteWithMetadataAsync(
            "user-1",
            It.IsAny<ResolvedKernelModelConfiguration>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
        budget.Verify(service => service.ReserveAsync(
            It.IsAny<GoalBudgetReservationRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
        budget.Verify(service => service.RecordResultAsync(
            It.IsAny<ModelExecutionResultRecord>(),
            It.IsAny<CancellationToken>()), Times.Once);
        budget.Verify(service => service.SettleAsync(
            "user-1",
            "execution-1",
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private static ResolvedKernelModelConfiguration Configuration() => new(
        "setting",
        2,
        "goal",
        "openai",
        "https://models.example/v1",
        "user-settings:llm",
        "test-model",
        0.2f,
        1024,
        60,
        [],
        string.Empty,
        2m,
        8m);

    private static KernelExecutionContext Context() => new(
        new KernelTaskClaim(
            "task-1",
            "user-1",
            "project-1",
            "goal-1",
            "graph-1",
            "branch-1",
            "setting",
            "BuildSetting",
            1,
            "worker-1",
            DateTime.UtcNow.AddMinutes(2)),
        new GoalContextSnapshot
        {
            Id = "snapshot-1",
            UserId = "user-1",
            ProjectId = "project-1",
            GoalId = "goal-1",
            CanonVersion = "canon-1",
            KnowledgeVersion = "knowledge-1",
            QualityContractVersion = "quality-1",
            StyleProfileVersion = "style-1"
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
        []);
}
