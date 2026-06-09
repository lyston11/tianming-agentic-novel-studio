using System.ComponentModel.DataAnnotations;

namespace TM.Web.NovelAgentWeb.DTOs;

/// <summary>
/// Request DTO for uploading a material file.
/// </summary>
public class UploadMaterialRequest
{
    [Required]
    public string ProjectId { get; set; } = null!;

    [Required]
    public string Title { get; set; } = null!;

    public string? Category { get; set; }

    public string? Tags { get; set; }

    [Required]
    public IFormFile File { get; set; } = null!;
}

/// <summary>
/// Request DTO for creating a material with text content.
/// </summary>
public class CreateMaterialRequest
{
    [Required]
    public string ProjectId { get; set; } = null!;

    [Required]
    public string Title { get; set; } = null!;

    public string? Category { get; set; }

    public string? Tags { get; set; }

    [Required]
    public string Content { get; set; } = null!;

    public string? ContentType { get; set; }
}

/// <summary>
/// Request DTO for updating material metadata.
/// </summary>
public class UpdateMaterialRequest
{
    public string? Category { get; set; }

    public string? Tags { get; set; }
}

/// <summary>
/// Response DTO for material metadata.
/// </summary>
public class MaterialResponse
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? ProjectId { get; set; }
    public string Title { get; set; } = null!;
    public string? Category { get; set; }
    public string? ContentType { get; set; }
    public string? FilePath { get; set; }
    public string? Tags { get; set; }
    public DateTime CreatedAt { get; set; }
    public int VectorChunkCount { get; set; }
}

/// <summary>
/// Response DTO for material content.
/// </summary>
public class MaterialContentResponse
{
    public string Id { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? ContentType { get; set; }
    public string Content { get; set; } = null!;
}
