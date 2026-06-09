using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Services.StoryBible;

/// <summary>
/// Service for managing story bible components including constitution and characters.
/// </summary>
public class StoryBibleService : IStoryBibleService
{
    private readonly NovelAgentDbContext _db;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<StoryBibleService> _logger;

    public StoryBibleService(
        NovelAgentDbContext db,
        ICurrentUserService currentUserService,
        ILogger<StoryBibleService> logger)
    {
        _db = db;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    // Story Constitution operations

    public async Task<StoryConstitutionResponse> CreateConstitutionAsync(
        CreateStoryConstitutionRequest request, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var project = await _db.NovelProjects
            .FirstOrDefaultAsync(p => p.Id == request.ProjectId && p.UserId == userId, ct);

        if (project == null)
            throw new KeyNotFoundException($"Project {request.ProjectId} not found");

        var constitution = new StoryConstitution
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            ProjectId = request.ProjectId,
            Genre = request.Genre,
            SubGenre = request.SubGenre,
            CoreHook = request.CoreHook,
            ReaderPromise = request.ReaderPromise,
            GenreProfile = request.GenreProfile,
            TargetAudience = request.TargetAudience,
            Taboos = request.Taboos,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.StoryConstitutions.Add(constitution);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created story constitution {ConstitutionId} for project {ProjectId}", constitution.Id, request.ProjectId);

        return MapConstitutionToResponse(constitution);
    }

    public async Task<StoryConstitutionResponse?> GetConstitutionByProjectAsync(string projectId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var project = await _db.NovelProjects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct);

        if (project == null)
            throw new KeyNotFoundException($"Project {projectId} not found");

        var constitution = await _db.StoryConstitutions
            .FirstOrDefaultAsync(c => c.ProjectId == projectId, ct);

        return constitution != null ? MapConstitutionToResponse(constitution) : null;
    }

    public async Task<StoryConstitutionResponse> UpdateConstitutionAsync(
        string constitutionId, UpdateStoryConstitutionRequest request, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var constitution = await _db.StoryConstitutions
            .Include(c => c.Project)
            .FirstOrDefaultAsync(c => c.Id == constitutionId, ct);

        if (constitution == null)
            throw new KeyNotFoundException($"Story constitution {constitutionId} not found");

        if (constitution.Project?.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        if (request.Genre != null)
            constitution.Genre = request.Genre;
        if (request.SubGenre != null)
            constitution.SubGenre = request.SubGenre;
        if (request.CoreHook != null)
            constitution.CoreHook = request.CoreHook;
        if (request.ReaderPromise != null)
            constitution.ReaderPromise = request.ReaderPromise;
        if (request.GenreProfile != null)
            constitution.GenreProfile = request.GenreProfile;
        if (request.TargetAudience != null)
            constitution.TargetAudience = request.TargetAudience;
        if (request.Taboos != null)
            constitution.Taboos = request.Taboos;

        constitution.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Updated story constitution {ConstitutionId}", constitutionId);

        return MapConstitutionToResponse(constitution);
    }

    public async Task DeleteConstitutionAsync(string constitutionId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var constitution = await _db.StoryConstitutions
            .Include(c => c.Project)
            .FirstOrDefaultAsync(c => c.Id == constitutionId, ct);

        if (constitution == null)
            throw new KeyNotFoundException($"Story constitution {constitutionId} not found");

        if (constitution.Project?.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        _db.StoryConstitutions.Remove(constitution);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted story constitution {ConstitutionId}", constitutionId);
    }

    // Character operations

    public async Task<CharacterResponse> CreateCharacterAsync(
        CreateCharacterRequest request, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var project = await _db.NovelProjects
            .FirstOrDefaultAsync(p => p.Id == request.ProjectId && p.UserId == userId, ct);

        if (project == null)
            throw new KeyNotFoundException($"Project {request.ProjectId} not found");

        var character = new Character
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            ProjectId = request.ProjectId,
            Name = request.Name,
            Role = request.Role,
            Alias = request.Alias,
            Age = request.Age,
            Gender = request.Gender,
            Appearance = request.Appearance,
            Personality = request.Personality,
            Background = request.Background,
            InitialPowerLevel = request.InitialPowerLevel,
            CurrentPowerLevel = request.CurrentPowerLevel,
            SpecialAbilities = request.SpecialAbilities,
            CoreGoal = request.CoreGoal,
            Motivation = request.Motivation,
            Relationships = request.Relationships,
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.Characters.Add(character);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Created character {CharacterId} in project {ProjectId}", character.Id, request.ProjectId);

        return MapCharacterToResponse(character);
    }

    public async Task<List<CharacterResponse>> ListCharactersAsync(string projectId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var project = await _db.NovelProjects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.UserId == userId, ct);

        if (project == null)
            throw new KeyNotFoundException($"Project {projectId} not found");

        var characters = await _db.Characters
            .Where(c => c.ProjectId == projectId)
            .OrderBy(c => c.Role)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);

        return characters.Select(MapCharacterToResponse).ToList();
    }

    public async Task<CharacterResponse> GetCharacterAsync(string characterId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var character = await _db.Characters
            .Include(c => c.Project)
            .FirstOrDefaultAsync(c => c.Id == characterId, ct);

        if (character == null)
            throw new KeyNotFoundException($"Character {characterId} not found");

        if (character.Project?.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        return MapCharacterToResponse(character);
    }

    public async Task<CharacterResponse> UpdateCharacterAsync(
        string characterId, UpdateCharacterRequest request, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var character = await _db.Characters
            .Include(c => c.Project)
            .FirstOrDefaultAsync(c => c.Id == characterId, ct);

        if (character == null)
            throw new KeyNotFoundException($"Character {characterId} not found");

        if (character.Project?.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        if (request.Name != null)
            character.Name = request.Name;
        if (request.Role != null)
            character.Role = request.Role;
        if (request.Alias != null)
            character.Alias = request.Alias;
        if (request.Age.HasValue)
            character.Age = request.Age;
        if (request.Gender != null)
            character.Gender = request.Gender;
        if (request.Appearance != null)
            character.Appearance = request.Appearance;
        if (request.Personality != null)
            character.Personality = request.Personality;
        if (request.Background != null)
            character.Background = request.Background;
        if (request.InitialPowerLevel != null)
            character.InitialPowerLevel = request.InitialPowerLevel;
        if (request.CurrentPowerLevel != null)
            character.CurrentPowerLevel = request.CurrentPowerLevel;
        if (request.SpecialAbilities != null)
            character.SpecialAbilities = request.SpecialAbilities;
        if (request.CoreGoal != null)
            character.CoreGoal = request.CoreGoal;
        if (request.Motivation != null)
            character.Motivation = request.Motivation;
        if (request.Relationships != null)
            character.Relationships = request.Relationships;
        if (request.Status != null)
            character.Status = request.Status;
        if (request.FirstAppearChapter != null)
            character.FirstAppearChapter = request.FirstAppearChapter;
        if (request.LastAppearChapter != null)
            character.LastAppearChapter = request.LastAppearChapter;

        character.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Updated character {CharacterId}", characterId);

        return MapCharacterToResponse(character);
    }

    public async Task DeleteCharacterAsync(string characterId, CancellationToken ct = default)
    {
        var userId = _currentUserService.GetUserId();

        var character = await _db.Characters
            .Include(c => c.Project)
            .FirstOrDefaultAsync(c => c.Id == characterId, ct);

        if (character == null)
            throw new KeyNotFoundException($"Character {characterId} not found");

        if (character.Project?.UserId != userId)
            throw new UnauthorizedAccessException("Access denied");

        _db.Characters.Remove(character);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted character {CharacterId}", characterId);
    }

    private static StoryConstitutionResponse MapConstitutionToResponse(StoryConstitution constitution)
    {
        return new StoryConstitutionResponse
        {
            Id = constitution.Id,
            UserId = constitution.UserId,
            ProjectId = constitution.ProjectId,
            Genre = constitution.Genre,
            SubGenre = constitution.SubGenre,
            CoreHook = constitution.CoreHook,
            ReaderPromise = constitution.ReaderPromise,
            GenreProfile = constitution.GenreProfile,
            TargetAudience = constitution.TargetAudience,
            Taboos = constitution.Taboos,
            CreatedAt = constitution.CreatedAt,
            UpdatedAt = constitution.UpdatedAt
        };
    }

    private static CharacterResponse MapCharacterToResponse(Character character)
    {
        return new CharacterResponse
        {
            Id = character.Id,
            UserId = character.UserId,
            ProjectId = character.ProjectId,
            Name = character.Name,
            Role = character.Role,
            Alias = character.Alias,
            Age = character.Age,
            Gender = character.Gender,
            Appearance = character.Appearance,
            Personality = character.Personality,
            Background = character.Background,
            InitialPowerLevel = character.InitialPowerLevel,
            CurrentPowerLevel = character.CurrentPowerLevel,
            SpecialAbilities = character.SpecialAbilities,
            CoreGoal = character.CoreGoal,
            Motivation = character.Motivation,
            Relationships = character.Relationships,
            Status = character.Status,
            FirstAppearChapter = character.FirstAppearChapter,
            LastAppearChapter = character.LastAppearChapter,
            CreatedAt = character.CreatedAt,
            UpdatedAt = character.UpdatedAt
        };
    }
}
