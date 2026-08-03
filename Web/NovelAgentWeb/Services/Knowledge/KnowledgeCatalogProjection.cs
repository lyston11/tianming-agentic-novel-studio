using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Knowledge;

public static class KnowledgeCatalogProjection
{
    public static async Task<KnowledgeCatalogState> GetOrRebuildAsync(
        NovelAgentDbContext db,
        string userId,
        CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO knowledge_catalog_states (user_id, revision, active_entry_count, updated_at)
                SELECT {userId}, 1, COUNT(*), CURRENT_TIMESTAMP
                FROM knowledge_base
                WHERE user_id = {userId} AND is_archived IS FALSE
                ON CONFLICT (user_id) DO NOTHING
                """, cancellationToken);
            return await db.KnowledgeCatalogStates.AsNoTracking()
                .SingleAsync(item => item.UserId == userId, cancellationToken);
        }
        var state = await db.KnowledgeCatalogStates.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (state != null)
            return state;
        state = new KnowledgeCatalogState
        {
            UserId = userId,
            Revision = 1,
            ActiveEntryCount = await db.KnowledgeBases.AsNoTracking()
                .CountAsync(item => item.UserId == userId && !item.IsArchived, cancellationToken),
            UpdatedAt = DateTime.UtcNow
        };
        db.KnowledgeCatalogStates.Add(state);
        await db.SaveChangesAsync(cancellationToken);
        return state;
    }

    public static async Task TouchAtomicAsync(
        NovelAgentDbContext db,
        string userId,
        int activeEntryDelta,
        CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO knowledge_catalog_states (user_id, revision, active_entry_count, updated_at)
                SELECT {userId}, 1, COUNT(*), CURRENT_TIMESTAMP
                FROM knowledge_base
                WHERE user_id = {userId} AND is_archived IS FALSE
                ON CONFLICT (user_id) DO UPDATE SET
                    revision = knowledge_catalog_states.revision + 1,
                    active_entry_count = CASE
                        WHEN knowledge_catalog_states.active_entry_count + {activeEntryDelta} < 0 THEN 0
                        ELSE knowledge_catalog_states.active_entry_count + {activeEntryDelta}
                    END,
                    updated_at = CURRENT_TIMESTAMP
                """, cancellationToken);
            return;
        }
        var state = await db.KnowledgeCatalogStates.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (state == null)
        {
            var persistedCount = await db.KnowledgeBases.AsNoTracking()
                .CountAsync(item => item.UserId == userId && !item.IsArchived, cancellationToken);
            state = new KnowledgeCatalogState
            {
                UserId = userId,
                Revision = 1,
                ActiveEntryCount = Math.Max(0, persistedCount + activeEntryDelta),
                UpdatedAt = DateTime.UtcNow
            };
            db.KnowledgeCatalogStates.Add(state);
        }
        else
        {
            state.Revision++;
            state.ActiveEntryCount = Math.Max(0, state.ActiveEntryCount + activeEntryDelta);
            state.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
