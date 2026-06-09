using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Services.Repositories;

/// <summary>
/// Repository implementation for StoryBible data access with user isolation.
/// All queries are filtered by the current user's ID.
/// </summary>
public class StoryBibleRepository : IStoryBibleRepository
{
    private readonly NovelAgentDbContext _context;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<StoryBibleRepository> _logger;

    public StoryBibleRepository(
        NovelAgentDbContext context,
        ICurrentUserService currentUserService,
        ILogger<StoryBibleRepository> logger)
    {
        _context = context;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<StoryBible> LoadStoryBibleAsync(string projectId, CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.GetUserId();

        // Verify project ownership
        var project = await _context.NovelProjects
            .Where(p => p.Id == projectId && p.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (project == null)
        {
            throw new KeyNotFoundException($"Project with ID {projectId} not found or access denied");
        }

        var storyBible = new StoryBible();

        // Load constitution (one-to-one with project)
        storyBible.Constitution = await _context.StoryConstitutions
            .Where(c => c.ProjectId == projectId && c.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        // Load volume arcs
        storyBible.VolumeArcs = await _context.VolumeArcs
            .Where(v => v.ProjectId == projectId && v.UserId == userId)
            .OrderBy(v => v.VolumeNumber)
            .ToListAsync(cancellationToken);

        // Load characters
        storyBible.Characters = await _context.Characters
            .Where(c => c.ProjectId == projectId && c.UserId == userId)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        // Load foreshadow entries
        storyBible.ForeshadowEntries = await _context.ForeshadowEntries
            .Where(f => f.ProjectId == projectId && f.UserId == userId)
            .OrderBy(f => f.PlantedInChapter)
            .ToListAsync(cancellationToken);

        // Load world settings
        storyBible.WorldSettings = await _context.WorldSettingEntries
            .Where(w => w.ProjectId == projectId && w.UserId == userId)
            .OrderBy(w => w.Category)
            .ThenBy(w => w.Title)
            .ToListAsync(cancellationToken);

        // Load agent runs
        storyBible.AgentRuns = await _context.AgentRuns
            .Where(a => a.ProjectId == projectId && a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "Loaded StoryBible for project {ProjectId}: Constitution={HasConstitution}, VolumeArcs={VolumeArcCount}, Characters={CharacterCount}, Foreshadows={ForeshadowCount}, WorldSettings={WorldSettingCount}, AgentRuns={AgentRunCount}",
            projectId,
            storyBible.Constitution != null,
            storyBible.VolumeArcs.Count,
            storyBible.Characters.Count,
            storyBible.ForeshadowEntries.Count,
            storyBible.WorldSettings.Count,
            storyBible.AgentRuns.Count);

        return storyBible;
    }

    public async Task SaveConstitutionAsync(StoryConstitution constitution, CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.GetUserId();

        // Verify user owns the project
        var project = await _context.NovelProjects
            .Where(p => p.Id == constitution.ProjectId && p.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (project == null)
        {
            throw new KeyNotFoundException($"Project with ID {constitution.ProjectId} not found or access denied");
        }

        // Ensure UserId is set correctly
        constitution.UserId = userId;
        constitution.UpdatedAt = DateTime.UtcNow;

        var existing = await _context.StoryConstitutions
            .Where(c => c.ProjectId == constitution.ProjectId && c.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing != null)
        {
            // Update existing
            constitution.Id = existing.Id;
            constitution.CreatedAt = existing.CreatedAt;
            _context.Entry(existing).CurrentValues.SetValues(constitution);
            _logger.LogInformation("Updated StoryConstitution for project {ProjectId}", constitution.ProjectId);
        }
        else
        {
            // Create new
            constitution.CreatedAt = DateTime.UtcNow;
            _context.StoryConstitutions.Add(constitution);
            _logger.LogInformation("Created new StoryConstitution for project {ProjectId}", constitution.ProjectId);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveVolumeArcAsync(VolumeArc volumeArc, CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.GetUserId();

        // Verify user owns the project
        var project = await _context.NovelProjects
            .Where(p => p.Id == volumeArc.ProjectId && p.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (project == null)
        {
            throw new KeyNotFoundException($"Project with ID {volumeArc.ProjectId} not found or access denied");
        }

        // Ensure UserId is set correctly
        volumeArc.UserId = userId;
        volumeArc.UpdatedAt = DateTime.UtcNow;

        var existing = await _context.VolumeArcs
            .Where(v => v.ProjectId == volumeArc.ProjectId && v.VolumeNumber == volumeArc.VolumeNumber && v.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing != null)
        {
            // Update existing
            volumeArc.Id = existing.Id;
            volumeArc.CreatedAt = existing.CreatedAt;
            _context.Entry(existing).CurrentValues.SetValues(volumeArc);
            _logger.LogInformation("Updated VolumeArc {VolumeNumber} for project {ProjectId}", volumeArc.VolumeNumber, volumeArc.ProjectId);
        }
        else
        {
            // Create new
            volumeArc.CreatedAt = DateTime.UtcNow;
            _context.VolumeArcs.Add(volumeArc);
            _logger.LogInformation("Created new VolumeArc {VolumeNumber} for project {ProjectId}", volumeArc.VolumeNumber, volumeArc.ProjectId);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveCharacterAsync(Character character, CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.GetUserId();

        // Verify user owns the project
        var project = await _context.NovelProjects
            .Where(p => p.Id == character.ProjectId && p.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (project == null)
        {
            throw new KeyNotFoundException($"Project with ID {character.ProjectId} not found or access denied");
        }

        // Ensure UserId is set correctly
        character.UserId = userId;
        character.UpdatedAt = DateTime.UtcNow;

        var existing = await _context.Characters
            .Where(c => c.Id == character.Id && c.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing != null)
        {
            // Update existing
            character.CreatedAt = existing.CreatedAt;
            _context.Entry(existing).CurrentValues.SetValues(character);
            _logger.LogInformation("Updated Character {CharacterName} (ID: {CharacterId}) for project {ProjectId}", character.Name, character.Id, character.ProjectId);
        }
        else
        {
            // Create new
            character.CreatedAt = DateTime.UtcNow;
            _context.Characters.Add(character);
            _logger.LogInformation("Created new Character {CharacterName} for project {ProjectId}", character.Name, character.ProjectId);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task PlantForeshadowAsync(ForeshadowEntry foreshadow, CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.GetUserId();

        // Verify user owns the project
        var project = await _context.NovelProjects
            .Where(p => p.Id == foreshadow.ProjectId && p.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (project == null)
        {
            throw new KeyNotFoundException($"Project with ID {foreshadow.ProjectId} not found or access denied");
        }

        // Ensure UserId and timestamps are set correctly
        foreshadow.UserId = userId;
        foreshadow.Status = "planted";
        foreshadow.PlantedAt = DateTime.UtcNow;
        foreshadow.CreatedAt = DateTime.UtcNow;
        foreshadow.UpdatedAt = DateTime.UtcNow;

        _context.ForeshadowEntries.Add(foreshadow);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Planted new Foreshadow '{Title}' in chapter {Chapter} for project {ProjectId}",
            foreshadow.Title, foreshadow.PlantedInChapter, foreshadow.ProjectId);
    }

    public async Task ResolveForeshadowAsync(string foreshadowId, string resolvedInChapter, string? resolvedContext, CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.GetUserId();

        var foreshadow = await _context.ForeshadowEntries
            .Where(f => f.Id == foreshadowId && f.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (foreshadow == null)
        {
            throw new KeyNotFoundException($"Foreshadow with ID {foreshadowId} not found or access denied");
        }

        foreshadow.Status = "resolved";
        foreshadow.ResolvedInChapter = resolvedInChapter;
        foreshadow.ResolvedContext = resolvedContext;
        foreshadow.ResolvedAt = DateTime.UtcNow;
        foreshadow.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Resolved Foreshadow '{Title}' (ID: {ForeshadowId}) in chapter {Chapter}",
            foreshadow.Title, foreshadowId, resolvedInChapter);
    }

    public async Task SaveWorldSettingAsync(WorldSettingEntry worldSetting, CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.GetUserId();

        // Verify user owns the project
        var project = await _context.NovelProjects
            .Where(p => p.Id == worldSetting.ProjectId && p.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (project == null)
        {
            throw new KeyNotFoundException($"Project with ID {worldSetting.ProjectId} not found or access denied");
        }

        // Ensure UserId is set correctly
        worldSetting.UserId = userId;
        worldSetting.UpdatedAt = DateTime.UtcNow;

        var existing = await _context.WorldSettingEntries
            .Where(w => w.Id == worldSetting.Id && w.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing != null)
        {
            // Update existing - increment version
            worldSetting.Version = existing.Version + 1;
            worldSetting.PreviousVersion = existing.Content; // Store previous content
            worldSetting.CreatedAt = existing.CreatedAt;
            _context.Entry(existing).CurrentValues.SetValues(worldSetting);
            _logger.LogInformation("Updated WorldSetting '{Title}' (ID: {SettingId}, Version: {Version}) for project {ProjectId}",
                worldSetting.Title, worldSetting.Id, worldSetting.Version, worldSetting.ProjectId);
        }
        else
        {
            // Create new
            worldSetting.Version = 1;
            worldSetting.CreatedAt = DateTime.UtcNow;
            _context.WorldSettingEntries.Add(worldSetting);
            _logger.LogInformation("Created new WorldSetting '{Title}' for project {ProjectId}", worldSetting.Title, worldSetting.ProjectId);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAgentRunAsync(AgentRun agentRun, CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.GetUserId();

        // Verify user owns the project
        var project = await _context.NovelProjects
            .Where(p => p.Id == agentRun.ProjectId && p.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (project == null)
        {
            throw new KeyNotFoundException($"Project with ID {agentRun.ProjectId} not found or access denied");
        }

        // Ensure UserId and timestamps are set correctly
        agentRun.UserId = userId;
        agentRun.CreatedAt = DateTime.UtcNow;
        agentRun.UpdatedAt = DateTime.UtcNow;

        var existing = await _context.AgentRuns
            .Where(a => a.Id == agentRun.Id && a.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing != null)
        {
            // Update existing
            agentRun.CreatedAt = existing.CreatedAt;
            _context.Entry(existing).CurrentValues.SetValues(agentRun);
            _logger.LogInformation("Updated AgentRun {RunId} for project {ProjectId}", agentRun.Id, agentRun.ProjectId);
        }
        else
        {
            // Create new
            _context.AgentRuns.Add(agentRun);
            _logger.LogInformation("Created new AgentRun {RunId} for project {ProjectId}", agentRun.Id, agentRun.ProjectId);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
