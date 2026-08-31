namespace Tianming.NovelAgent.Contracts.Models;

public sealed record ModelMessage(string Role, string Content);

public sealed record ModelRequest(
    string Purpose,
    IReadOnlyList<ModelMessage> Messages,
    string? JsonSchema,
    string Provider,
    string Model,
    string PromptVersion,
    string SchemaVersion,
    string ContextHash,
    decimal MaximumCost,
    string CorrelationId,
    string IdempotencyKey,
    bool Stream = false,
    string UserId = "",
    string ProjectId = "",
    string GoalId = "",
    string TaskId = "");

public sealed record ModelUsage(int InputTokens, int OutputTokens, decimal ActualCost, string Currency);

public sealed record ModelResult(
    string Content,
    string? StructuredJson,
    string Provider,
    string Model,
    string? ProviderRequestId,
    string FinishReason,
    ModelUsage Usage,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record ModelGatewayError(string Code, string Message, bool Retryable, bool OutcomeUnknown);

public sealed record ModelProviderCapabilities(
    bool StructuredOutput,
    bool Streaming,
    bool ToolCalling,
    bool UsageReporting,
    bool IdempotencyQuery);
