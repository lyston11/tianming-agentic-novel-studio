using System.ComponentModel.DataAnnotations;

namespace TM.Web.NovelAgentWeb.DTOs;

/// <summary>
/// Request DTO for creating a volume arc.
/// </summary>
public class CreateVolumeArcRequest
{
    [Required]
    public string ProjectId { get; set; } = null!;

    [Required]
    [Range(1, int.MaxValue)]
    public int VolumeNumber { get; set; }

    [Required]
    public string VolumeTitle { get; set; } = null!;

    public string? VolumeTheme { get; set; }
    public int? TargetChapters { get; set; }
    public string? Act1Setup { get; set; }
    public string? Act2Confrontation { get; set; }
    public string? Act3Climax { get; set; }
    public string? Act4Resolution { get; set; }
    public string? KeyEvents { get; set; }
    public string? MajorConflict { get; set; }
    public string? ConflictEscalation { get; set; }
}

/// <summary>
/// Request DTO for updating a volume arc.
/// </summary>
public class UpdateVolumeArcRequest
{
    public string? VolumeTitle { get; set; }
    public string? VolumeTheme { get; set; }
    public int? TargetChapters { get; set; }
    public int? CurrentChapters { get; set; }
    public string? Act1Setup { get; set; }
    public string? Act2Confrontation { get; set; }
    public string? Act3Climax { get; set; }
    public string? Act4Resolution { get; set; }
    public string? KeyEvents { get; set; }
    public string? MajorConflict { get; set; }
    public string? ConflictEscalation { get; set; }
    public string? Status { get; set; }
}

/// <summary>
/// Response DTO for volume arc.
/// </summary>
public class VolumeArcResponse
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public int VolumeNumber { get; set; }
    public string VolumeTitle { get; set; } = null!;
    public string? VolumeTheme { get; set; }
    public int? TargetChapters { get; set; }
    public int CurrentChapters { get; set; }
    public string? Act1Setup { get; set; }
    public string? Act2Confrontation { get; set; }
    public string? Act3Climax { get; set; }
    public string? Act4Resolution { get; set; }
    public string? KeyEvents { get; set; }
    public string? MajorConflict { get; set; }
    public string? ConflictEscalation { get; set; }
    public string Status { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
