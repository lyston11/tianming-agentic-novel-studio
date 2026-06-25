using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using TM.Framework.Common.Helpers;
using TM.Services.Modules.ProjectData.Implementations;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ProductionVolumeFactArchiveService : IVolumeFactArchiveService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly NovelAgentDbContext? _db;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly string _userId;
    private readonly string _projectId;

    public ProductionVolumeFactArchiveService(
        NovelAgentDbContext db,
        string userId,
        string projectId)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _userId = userId;
        _projectId = projectId;
    }

    public ProductionVolumeFactArchiveService(
        IServiceScopeFactory scopeFactory,
        string userId,
        string projectId)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _userId = userId;
        _projectId = projectId;
    }

    public async Task<List<VolumeFactArchive>> GetPreviousArchivesAsync(int currentVolumeNumber)
    {
        if (currentVolumeNumber <= 1 || string.IsNullOrWhiteSpace(_projectId))
            return new List<VolumeFactArchive>();

        if (_db != null)
            return await QueryAsync(_db, currentVolumeNumber).ConfigureAwait(false);

        if (_scopeFactory == null)
            throw new InvalidOperationException("Volume fact archive requires project-scoped database truth.");

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NovelAgentDbContext>();
        return await QueryAsync(db, currentVolumeNumber).ConfigureAwait(false);
    }

    public void InvalidateCache()
    {
    }

    private async Task<List<VolumeFactArchive>> QueryAsync(NovelAgentDbContext db, int currentVolumeNumber)
    {
        var cfg = LayeredContextConfig.TakeSnapshot();
        var startVolume = Math.Max(1, currentVolumeNumber - cfg.ArchiveMaxPreviousVolumes);

        var query = db.ProjectFactSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.ProjectId == _projectId);
        if (!string.IsNullOrWhiteSpace(_userId))
            query = query.Where(snapshot => snapshot.UserId == _userId);

        var snapshots = await query
            .OrderByDescending(snapshot => snapshot.CreatedAt)
            .ThenByDescending(snapshot => snapshot.VersionNumber)
            .Take(2000)
            .ToListAsync()
            .ConfigureAwait(false);

        var latestByVolume = new Dictionary<int, ProjectFactSnapshot>();
        foreach (var snapshot in snapshots)
        {
            var volumeNumber = GetVolumeNumber(snapshot.ChapterId);
            if (volumeNumber < startVolume || volumeNumber >= currentVolumeNumber)
                continue;

            latestByVolume.TryAdd(volumeNumber, snapshot);
        }

        return latestByVolume
            .OrderBy(pair => pair.Key)
            .Select(pair => BuildArchive(pair.Key, pair.Value))
            .Where(archive => archive != null)
            .Cast<VolumeFactArchive>()
            .ToList();
    }

    private static VolumeFactArchive? BuildArchive(int volumeNumber, ProjectFactSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.SnapshotJson))
            return null;

        var archive = TryBuildFromFactSnapshot(volumeNumber, snapshot)
            ?? TryBuildFromLightweightSnapshot(volumeNumber, snapshot);
        if (archive == null)
            return null;

        archive.VolumeNumber = volumeNumber;
        archive.LastChapterId = snapshot.ChapterId ?? string.Empty;
        archive.ArchivedAt = snapshot.CreatedAt;
        return archive;
    }

    private static VolumeFactArchive? TryBuildFromFactSnapshot(int volumeNumber, ProjectFactSnapshot snapshot)
    {
        try
        {
            var fact = JsonSerializer.Deserialize<FactSnapshot>(snapshot.SnapshotJson, JsonOptions);
            if (fact == null || !HasFactContent(fact))
                return null;

            return new VolumeFactArchive
            {
                VolumeNumber = volumeNumber,
                LastChapterId = snapshot.ChapterId ?? string.Empty,
                ArchivedAt = snapshot.CreatedAt,
                CharacterStates = fact.CharacterStates ?? new(),
                ConflictProgress = fact.ConflictProgress ?? new(),
                ForeshadowingStatus = fact.ForeshadowingStatus ?? new(),
                LocationStates = fact.LocationStates ?? new(),
                FactionStates = fact.FactionStates ?? new(),
                ItemStates = fact.ItemStates ?? new(),
                SecretStates = fact.SecretStates ?? new(),
                Timeline = fact.Timeline ?? new(),
                CharacterLocations = fact.CharacterLocations ?? new(),
                PledgeStates = fact.PledgeStates ?? new(),
                DeadlineStates = fact.DeadlineStates ?? new()
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static VolumeFactArchive? TryBuildFromLightweightSnapshot(int volumeNumber, ProjectFactSnapshot snapshot)
    {
        try
        {
            using var document = JsonDocument.Parse(snapshot.SnapshotJson);
            var root = document.RootElement;
            var archive = new VolumeFactArchive
            {
                VolumeNumber = volumeNumber,
                LastChapterId = snapshot.ChapterId ?? string.Empty,
                ArchivedAt = snapshot.CreatedAt
            };

            foreach (var line in ReadStringArray(root, "characterStates"))
            {
                var name = ExtractDisplayName(line);
                archive.CharacterStates.Add(new CharacterStateSnapshot
                {
                    Id = name,
                    Name = name,
                    Stage = line,
                    ChapterId = snapshot.ChapterId ?? string.Empty
                });
            }

            foreach (var line in ReadStringArray(root, "activeConflicts"))
            {
                archive.ConflictProgress.Add(new ConflictProgressSnapshot
                {
                    Id = ExtractDisplayName(line),
                    Name = ExtractDisplayName(line),
                    Status = line,
                    RecentProgress = new List<string> { line }
                });
            }

            foreach (var line in ReadStringArray(root, "nextChapterMustCarry"))
            {
                archive.Timeline.Add(new TimelineSnapshot
                {
                    ChapterId = snapshot.ChapterId ?? string.Empty,
                    KeyTimeEvent = line
                });
            }

            return HasArchiveContent(archive) ? archive : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    yield return value;
            }
        }
    }

    private static bool HasFactContent(FactSnapshot fact) =>
        fact.CharacterStates.Count > 0 ||
        fact.ConflictProgress.Count > 0 ||
        fact.ForeshadowingStatus.Count > 0 ||
        fact.LocationStates.Count > 0 ||
        fact.FactionStates.Count > 0 ||
        fact.ItemStates.Count > 0 ||
        fact.SecretStates.Count > 0 ||
        fact.Timeline.Count > 0 ||
        fact.CharacterLocations.Count > 0 ||
        fact.PledgeStates.Count > 0 ||
        fact.DeadlineStates.Count > 0;

    private static bool HasArchiveContent(VolumeFactArchive archive) =>
        archive.CharacterStates.Count > 0 ||
        archive.ConflictProgress.Count > 0 ||
        archive.Timeline.Count > 0;

    private static int GetVolumeNumber(string? chapterId) =>
        string.IsNullOrWhiteSpace(chapterId)
            ? 1
            : ChapterParserHelper.ParseChapterId(chapterId)?.volumeNumber ?? 1;

    private static string ExtractDisplayName(string line)
    {
        var text = line.Trim();
        var separators = new[] { '：', ':', '，', ',', ' ' };
        var index = text.IndexOfAny(separators);
        return index > 0 ? text[..index] : text;
    }
}
