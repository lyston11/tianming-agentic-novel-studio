using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Web.NovelAgentWeb.Data;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ProductionDesignElementLookupService : IDesignElementLookupService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly NovelAgentDbContext? _db;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly string _userId;
    private readonly string _projectId;

    public ProductionDesignElementLookupService(
        NovelAgentDbContext db,
        string userId,
        string projectId)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userId = userId;
        _projectId = projectId;
    }

    public ProductionDesignElementLookupService(
        IServiceScopeFactory scopeFactory,
        string userId,
        string projectId)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _userId = userId;
        _projectId = projectId;
    }

    public async Task<string?> ResolveCharacterNameAsync(string characterId)
    {
        if (string.IsNullOrWhiteSpace(characterId))
            return null;

        if (_db != null)
            return await ResolveCharacterNameAsync(_db, characterId).ConfigureAwait(false);

        if (_scopeFactory == null)
            throw new InvalidOperationException("Design element lookup requires project-scoped database truth.");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        return await ResolveCharacterNameAsync(db, characterId).ConfigureAwait(false);
    }

    public Task<string?> ResolveLocationNameAsync(string locationId) =>
        ResolveFromFactSnapshotsAsync(locationId, snapshot => snapshot.LocationStates.Select(item => (item.Id, item.Name)));

    public Task<string?> ResolveFactionNameAsync(string factionId) =>
        ResolveFromFactSnapshotsAsync(factionId, snapshot => snapshot.FactionStates.Select(item => (item.Id, item.Name)));

    public Task<string?> ResolveConflictNameAsync(string conflictId) =>
        ResolveFromFactSnapshotsAsync(conflictId, snapshot => snapshot.ConflictProgress.Select(item => (item.Id, item.Name)));

    private async Task<string?> ResolveCharacterNameAsync(NovelAgentDbContext db, string characterId)
    {
        var query = db.Characters
            .AsNoTracking()
            .Where(character => character.ProjectId == _projectId && character.Id == characterId);
        if (!string.IsNullOrWhiteSpace(_userId))
            query = query.Where(character => character.UserId == _userId);

        return await query
            .Select(character => character.Name)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
    }

    private async Task<string?> ResolveFromFactSnapshotsAsync(
        string id,
        Func<FactSnapshot, IEnumerable<(string Id, string Name)>> selector)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(_projectId))
            return null;

        if (_db != null)
            return await ResolveFromFactSnapshotsAsync(_db, id, selector).ConfigureAwait(false);

        if (_scopeFactory == null)
            throw new InvalidOperationException("Design element lookup requires project-scoped database truth.");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        return await ResolveFromFactSnapshotsAsync(db, id, selector).ConfigureAwait(false);
    }

    private async Task<string?> ResolveFromFactSnapshotsAsync(
        NovelAgentDbContext db,
        string id,
        Func<FactSnapshot, IEnumerable<(string Id, string Name)>> selector)
    {
        var query = db.ProjectFactSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.ProjectId == _projectId);
        if (!string.IsNullOrWhiteSpace(_userId))
            query = query.Where(snapshot => snapshot.UserId == _userId);

        var snapshots = await query
            .OrderByDescending(snapshot => snapshot.CreatedAt)
            .ThenByDescending(snapshot => snapshot.VersionNumber)
            .Take(200)
            .ToListAsync()
            .ConfigureAwait(false);

        foreach (var snapshot in snapshots)
        {
            var fact = TryDeserialize(snapshot.SnapshotJson);
            if (fact == null)
                continue;

            var match = selector(fact)
                .FirstOrDefault(item =>
                    string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(item.Name));
            if (!string.IsNullOrWhiteSpace(match.Name))
                return match.Name;
        }

        return null;
    }

    private static FactSnapshot? TryDeserialize(string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return null;

        try
        {
            return JsonSerializer.Deserialize<FactSnapshot>(snapshotJson, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
