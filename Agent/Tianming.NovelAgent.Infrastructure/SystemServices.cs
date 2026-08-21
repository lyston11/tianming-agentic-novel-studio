using System.Security.Cryptography;
using System.Text.Json;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Domain.Goals;

namespace Tianming.NovelAgent.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class GuidIdGenerator : IIdGenerator
{
    public string NewId() => Guid.NewGuid().ToString("N");
}

public sealed class Sha256ContractHasher : IContractHasher
{
    public string Hash(GoalContract contract)
    {
        var canonical = new
        {
            contract.Objective,
            contract.Mode,
            contract.ChapterRange,
            contract.SuccessCriteria,
            contract.MustPreserve,
            contract.MustHappen,
            contract.MustNotChange,
            contract.AcceptancePolicy,
            contract.ReworkPolicy,
            contract.TotalCostLimit,
            contract.CanonBaselineVersion,
            contract.KnowledgeSnapshotVersion,
            contract.QualityContractVersion,
            contract.StyleProfileVersion,
            ModelVersions = contract.ModelVersions.OrderBy(x => x.Key),
            ProtocolVersions = contract.ProtocolVersions.OrderBy(x => x.Key)
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(canonical);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}

public sealed class NullTransientAgentStream : ITransientAgentStream
{
    public Task PublishTokenDeltaAsync(
        string userId,
        ConversationBinding binding,
        string sessionId,
        string correlationId,
        string delta,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
