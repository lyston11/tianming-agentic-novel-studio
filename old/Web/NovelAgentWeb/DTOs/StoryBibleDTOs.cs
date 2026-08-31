using System.ComponentModel.DataAnnotations;

namespace TM.Web.NovelAgentWeb.DTOs;

/// <summary>
/// Request DTO for creating a story constitution.
/// </summary>
public class CreateStoryConstitutionRequest
{
    [Required]
    public string ProjectId { get; set; } = null!;

    [StringLength(160)]
    public string? IdempotencyKey { get; set; }

    [Required]
    public string Genre { get; set; } = null!;

    public string? SubGenre { get; set; }

    [Required]
    public string CoreHook { get; set; } = null!;

    public string? ReaderPromise { get; set; }
    public string? GenreProfile { get; set; }
    public string? TargetAudience { get; set; }
    public string? Taboos { get; set; }
}

/// <summary>
/// Request DTO for updating a story constitution.
/// </summary>
public class UpdateStoryConstitutionRequest
{
    public string? Genre { get; set; }
    public string? SubGenre { get; set; }
    public string? CoreHook { get; set; }
    public string? ReaderPromise { get; set; }
    public string? GenreProfile { get; set; }
    public string? TargetAudience { get; set; }
    public string? Taboos { get; set; }
}

/// <summary>
/// Response DTO for story constitution.
/// </summary>
public class StoryConstitutionResponse
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string Genre { get; set; } = null!;
    public string? SubGenre { get; set; }
    public string CoreHook { get; set; } = null!;
    public string? ReaderPromise { get; set; }
    public string? GenreProfile { get; set; }
    public string? TargetAudience { get; set; }
    public string? Taboos { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Request DTO for creating a character.
/// </summary>
public class CreateCharacterRequest
{
    [Required]
    public string ProjectId { get; set; } = null!;

    [StringLength(160)]
    public string? IdempotencyKey { get; set; }

    [Required]
    public string Name { get; set; } = null!;

    [Required]
    public string Role { get; set; } = null!;

    public string? Alias { get; set; }
    public int? Age { get; set; }
    public string? Gender { get; set; }
    public string? Appearance { get; set; }
    public string? Personality { get; set; }
    public string? Background { get; set; }
    public string? InitialPowerLevel { get; set; }
    public string? CurrentPowerLevel { get; set; }
    public string? SpecialAbilities { get; set; }
    public string? CoreGoal { get; set; }
    public string? Motivation { get; set; }
    public string? Relationships { get; set; }
}

/// <summary>
/// Request DTO for updating a character.
/// </summary>
public class UpdateCharacterRequest
{
    public string? Name { get; set; }
    public string? Role { get; set; }
    public string? Alias { get; set; }
    public int? Age { get; set; }
    public string? Gender { get; set; }
    public string? Appearance { get; set; }
    public string? Personality { get; set; }
    public string? Background { get; set; }
    public string? InitialPowerLevel { get; set; }
    public string? CurrentPowerLevel { get; set; }
    public string? SpecialAbilities { get; set; }
    public string? CoreGoal { get; set; }
    public string? Motivation { get; set; }
    public string? Relationships { get; set; }
    public string? Status { get; set; }
    public string? FirstAppearChapter { get; set; }
    public string? LastAppearChapter { get; set; }
}

/// <summary>
/// Response DTO for character.
/// </summary>
public class CharacterResponse
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Role { get; set; } = null!;
    public string? Alias { get; set; }
    public int? Age { get; set; }
    public string? Gender { get; set; }
    public string? Appearance { get; set; }
    public string? Personality { get; set; }
    public string? Background { get; set; }
    public string? InitialPowerLevel { get; set; }
    public string? CurrentPowerLevel { get; set; }
    public string? SpecialAbilities { get; set; }
    public string? CoreGoal { get; set; }
    public string? Motivation { get; set; }
    public string? Relationships { get; set; }
    public string Status { get; set; } = null!;
    public string? FirstAppearChapter { get; set; }
    public string? LastAppearChapter { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class StoryBibleResponse
{
    public StoryConstitutionResponse? Constitution { get; set; }
    public List<CharacterResponse> Characters { get; set; } = new();
}
