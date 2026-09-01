using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Data.Interceptors;

public sealed class LegacyControlPlaneWriteGuard(IConfiguration configuration) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Guard(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Guard(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Guard(DbContext? context)
    {
        if (!configuration.GetValue("TargetArchitecture:EnforceLegacyControlPlaneReadOnly", false)
            || context is null)
            return;

        var forbidden = context.ChangeTracker.Entries()
            .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .FirstOrDefault(x => x.Entity is CreativeGoal
                or GoalRevision
                or BookProduction
                or ProductionBatch
                or TaskGraphVersion
                or KernelTask
                or KernelArtifact);
        if (forbidden is not null)
        {
            throw new InvalidOperationException(
                $"Legacy control-plane writes are disabled for {forbidden.Metadata.ClrType.Name}; use NovelAgent Application commands.");
        }
    }
}
