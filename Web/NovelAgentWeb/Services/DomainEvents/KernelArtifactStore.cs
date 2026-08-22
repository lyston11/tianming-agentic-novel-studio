using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using Tianming.NovelAgent.Application.Ports;
using TM.Web.NovelAgentWeb.Services.Goals;

namespace TM.Web.NovelAgentWeb.Services.DomainEvents;

public sealed class KernelArtifactStore : IKernelArtifactStore
{
    private readonly NovelAgentDbContext _db;
    private readonly ILegacyControlPlaneCommands _controlPlane;

    public KernelArtifactStore(NovelAgentDbContext db, ILegacyControlPlaneCommands controlPlane)
    {
        _db = db;
        _controlPlane = controlPlane;
    }

    public async Task<IReadOnlyList<string>> AddOrReuseAsync(
        KernelTaskClaim claim,
        IReadOnlyList<KernelArtifactProposal> proposals,
        CancellationToken cancellationToken = default)
    {
        var ids = new List<string>(proposals.Count);
        foreach (var proposal in proposals)
        {
            var existingId = await _db.KernelArtifacts
                .AsNoTracking()
                .Where(artifact =>
                    artifact.UserId == claim.UserId &&
                    artifact.TaskId == claim.TaskId &&
                    artifact.ArtifactType == proposal.ArtifactType &&
                    artifact.SchemaVersion == proposal.SchemaVersion &&
                    artifact.ContentHash == proposal.ContentHash)
                .Select(artifact => artifact.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (existingId != null)
            {
                ids.Add(existingId);
                continue;
            }

            var artifactId = Guid.NewGuid().ToString("N");
            await _controlPlane.CreateArtifactAsync(new LegacyArtifactCommand(
                claim.UserId,
                claim.ProjectId,
                claim.GoalId,
                claim.TaskId,
                artifactId,
                claim.BranchId,
                proposal.ArtifactType,
                proposal.SchemaVersion,
                proposal.ContentJson,
                proposal.ContentHash,
                "adopted",
                proposal.Authorship,
                proposal.IsProtected,
                ModelExecutionId: null,
                CausationId: null,
                DateTime.UtcNow),
                cancellationToken);
            ids.Add(artifactId);
        }

        return ids;
    }
}
