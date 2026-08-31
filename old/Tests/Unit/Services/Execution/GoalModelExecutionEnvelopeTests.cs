using Moq;
using TM.Web.NovelAgentWeb.Services.Execution;
using TM.Web.NovelAgentWeb.Services.Models;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Execution;

public sealed class GoalModelExecutionEnvelopeTests
{
    [Fact]
    public async Task ExecuteAsync_UsesAmbientKernelScopeAndPersistsProviderUsage()
    {
        var scopes = new KernelModelExecutionScopeAccessor();
        using var _ = scopes.Push(new KernelModelExecutionScope(
            "user-1", "project-1", "goal-1", "task-1", "tianming_writing", 1));
        var budget = new Mock<IGoalBudgetService>(MockBehavior.Strict);
        budget.Setup(service => service.FindReusableResultAsync(
                "user-1", "task-1", "tianming_writing", "tianming_writing:v2",
                It.Is<string>(key => key.Length == 64),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModelExecutionResultReceipt?)null);
        budget.Setup(service => service.ReserveAsync(
                It.Is<GoalBudgetReservationRequest>(request =>
                    request.GoalId == "goal-1" &&
                    request.OperationKey.Length == 64 &&
                    request.WorstCaseCost > 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoalBudgetReservationResult(true, "execution-1", 1m, 9m));
        budget.Setup(service => service.MarkProviderCallStartedAsync(
                "user-1", "execution-1", It.IsAny<string>(), TimeSpan.FromSeconds(60),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        budget.Setup(service => service.RecordResultAsync(
                It.Is<ModelExecutionResultRecord>(record =>
                    record.ExecutionId == "execution-1" &&
                    record.InputTokens == 120 &&
                    record.OutputTokens == 30 &&
                    record.Provider == "openai" &&
                    record.ProviderRequestId == "request-1"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        budget.Setup(service => service.SettleAsync(
                "user-1", "execution-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var envelope = new GoalModelExecutionEnvelope(budget.Object, scopes);
        var providerCalls = 0;

        var result = await envelope.ExecuteAsync(
            Configuration(),
            "system contract",
            "chapter context",
            _ =>
            {
                providerCalls++;
                return Task.FromResult(new WritingModelCompletionResult(
                    "正文", "openai", "model-a", "request-1",
                    120, 30, 2m, 8m));
            });

        Assert.Equal("正文", result.Text);
        Assert.Equal(1, providerCalls);
        budget.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_KnownProviderFailureClosesExecutionAndReleasesReservation()
    {
        var scopes = new KernelModelExecutionScopeAccessor();
        using var _ = scopes.Push(new KernelModelExecutionScope(
            "user-1", "project-1", "goal-1", "task-1", "tianming_writing", 1));
        var budget = new Mock<IGoalBudgetService>(MockBehavior.Strict);
        budget.Setup(service => service.FindReusableResultAsync(
                "user-1", "task-1", "tianming_writing", "tianming_writing:v2",
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModelExecutionResultReceipt?)null);
        budget.Setup(service => service.ReserveAsync(
                It.IsAny<GoalBudgetReservationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoalBudgetReservationResult(true, "execution-1", 1m, 9m));
        budget.Setup(service => service.MarkProviderCallStartedAsync(
                "user-1", "execution-1", It.IsAny<string>(), TimeSpan.FromSeconds(60),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        budget.Setup(service => service.FailKnownAsync(
                "user-1", "execution-1", It.IsAny<string>(), "配置被明确拒绝",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var envelope = new GoalModelExecutionEnvelope(budget.Object, scopes);

        await Assert.ThrowsAsync<ModelCallKnownFailureException>(() => envelope.ExecuteAsync(
            Configuration(),
            "system contract",
            "chapter context",
            _ => throw new ModelCallKnownFailureException("配置被明确拒绝")));

        budget.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_UnknownOutcomeLeavesExecutionForLeaseRecovery()
    {
        var scopes = new KernelModelExecutionScopeAccessor();
        using var _ = scopes.Push(new KernelModelExecutionScope(
            "user-1", "project-1", "goal-1", "task-1", "tianming_writing", 1));
        var budget = new Mock<IGoalBudgetService>(MockBehavior.Strict);
        budget.Setup(service => service.FindReusableResultAsync(
                "user-1", "task-1", "tianming_writing", "tianming_writing:v2",
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModelExecutionResultReceipt?)null);
        budget.Setup(service => service.ReserveAsync(
                It.IsAny<GoalBudgetReservationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoalBudgetReservationResult(true, "execution-1", 1m, 9m));
        budget.Setup(service => service.MarkProviderCallStartedAsync(
                "user-1", "execution-1", It.IsAny<string>(), TimeSpan.FromSeconds(60),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var envelope = new GoalModelExecutionEnvelope(budget.Object, scopes);

        await Assert.ThrowsAsync<ModelCallOutcomeUnknownException>(() => envelope.ExecuteAsync(
            Configuration(),
            "system contract",
            "chapter context",
            _ => throw new ModelCallOutcomeUnknownException("网络超时，结果未知")));

        budget.VerifyAll();
    }

    private static ResolvedKernelModelConfiguration Configuration() => new(
        "tianming_writing", 2, "goal", "openai", "https://models.example/v1",
        "user-settings:llm", "model-a", 0.2f, 1024, 60, [], string.Empty, 2m, 8m);
}
