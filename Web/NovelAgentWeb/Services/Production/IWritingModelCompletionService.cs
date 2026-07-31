using TM.Web.NovelAgentWeb.Services.Models;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ModelCallKnownFailureException : InvalidOperationException
{
    public ModelCallKnownFailureException(string message) : base(message) { }
}

public sealed class ModelCallOutcomeUnknownException : InvalidOperationException
{
    public ModelCallOutcomeUnknownException(string message) : base(message) { }
}

public sealed record WritingModelCompletionResult(
    string Text,
    string Provider,
    string Model,
    string? ProviderRequestId,
    int InputTokens,
    int OutputTokens,
    decimal InputPricePerMillion,
    decimal OutputPricePerMillion,
    bool UsageReported = true);

public interface IWritingModelCompletionService
{
    Task<string> CompleteAsync(
        string userId,
        string system,
        string user,
        CancellationToken ct = default);

    Task<string> CompleteAsync(
        string userId,
        ResolvedKernelModelConfiguration configuration,
        string system,
        string user,
        CancellationToken ct = default) =>
        CompleteAsync(userId, system, user, ct);

    async Task<WritingModelCompletionResult> CompleteWithMetadataAsync(
        string userId,
        ResolvedKernelModelConfiguration configuration,
        string system,
        string user,
        CancellationToken ct = default) =>
        new(
            await CompleteAsync(userId, configuration, system, user, ct),
            configuration.Provider,
            configuration.Model,
            null,
            0,
            0,
            configuration.InputPricePerMillion,
            configuration.OutputPricePerMillion);
}
