using Moq;
using TM.Services.Framework.AI.Embedding;
using TM.Web.NovelAgentWeb.Services.Embedding;
using TM.Web.NovelAgentWeb.Services.Execution;
using Xunit;

namespace Tests.Unit.Services.Execution;

public sealed class GoalEmbeddingExecutionEnvelopeTests
{
    [Fact]
    public async Task ExecuteAsync_InKernelScope_PersistsZeroCostEmbeddingExecution()
    {
        var scopes = new KernelModelExecutionScopeAccessor();
        using var _ = scopes.Push(new KernelModelExecutionScope(
            "user-1", "project-1", "goal-1", "task-1", "knowledge_retrieval", 1));
        var budget = new Mock<IGoalBudgetService>(MockBehavior.Strict);
        budget.Setup(service => service.FindReusableResultAsync(
                "user-1", "task-1", "embedding", "embedding:bge-small-zh:bge-small-zh-v1.5",
                It.Is<string>(key => key.Length == 64),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((ModelExecutionResultReceipt?)null);
        budget.Setup(service => service.ReserveAsync(
                It.Is<GoalBudgetReservationRequest>(request =>
                    request.UserId == "user-1" &&
                    request.GoalId == "goal-1" &&
                    request.TaskId == "task-1" &&
                    request.KernelName == "embedding" &&
                    request.Provider == "bge-small-zh" &&
                    request.Model == "bge-small-zh-v1.5" &&
                    request.WorstCaseCost == 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoalBudgetReservationResult(true, "embedding-execution-1", 0, 10));
        budget.Setup(service => service.MarkProviderCallStartedAsync(
                "user-1", "embedding-execution-1", It.IsAny<string>(), TimeSpan.FromSeconds(120),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        budget.Setup(service => service.RecordResultAsync(
                It.Is<ModelExecutionResultRecord>(record =>
                    record.ExecutionId == "embedding-execution-1" &&
                    record.ActualCost == 0 &&
                    record.InputTokens > 0 &&
                    record.OutputTokens == 0 &&
                    record.Provider == "bge-small-zh" &&
                    record.Model == "bge-small-zh-v1.5" &&
                    record.ResultContentHash.Length == 64),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        budget.Setup(service => service.SettleAsync(
                "user-1", "embedding-execution-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var envelope = new GoalEmbeddingExecutionEnvelope(
            budget.Object,
            scopes,
            new EmbeddingRuntimeStatus
            {
                Provider = "bge-small-zh",
                Model = "bge-small-zh-v1.5",
                Dimension = 3
            });
        var providerCalls = 0;

        var vector = await envelope.ExecuteAsync(
            "主角的身份承诺",
            EmbeddingMode.Query,
            _ =>
            {
                providerCalls++;
                return Task.FromResult(new[] { 0.1f, 0.2f, 0.3f });
            });

        Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, vector);
        Assert.Equal(1, providerCalls);
        budget.VerifyAll();
    }
}
