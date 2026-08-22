using System.Security.Cryptography;
using System.Text;
using TM.Web.NovelAgentWeb.Services.DomainEvents;

namespace TM.Web.NovelAgentWeb.Services.Kernels;

internal static class KernelOutputFactory
{
    public static KernelExecutionOutput Single(
        KernelExecutionContext context,
        string artifactType,
        string eventType,
        string aggregateType,
        string aggregateId,
        string json)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        return new KernelExecutionOutput(
        [
            new KernelArtifactProposal(artifactType, 1, json, hash, "agent", false)
        ],
        [
            new DomainEventProposal(
                aggregateType,
                aggregateId,
                1,
                eventType,
                ["@artifact:0"],
                context.Inputs.Select(input => input.Id).ToArray(),
                "{}",
                $"{context.Claim.TaskId}:{eventType}:{hash}")
        ]);
    }
}
