using System.ComponentModel.DataAnnotations.Schema;

namespace TM.Web.NovelAgentWeb.Data.Entities;

public class CreativeIntent
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string? SessionId { get; set; }
    public string? RuntimeRunId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string Source { get; set; } = "chat";
    public string RawContent { get; set; } = null!;
    public string NormalizedIntent { get; set; } = null!;
    public string TargetScope { get; set; } = "project";
    public string? TargetVolumeId { get; set; }
    public string? TargetChapterId { get; set; }
    public string? TargetCharacterName { get; set; }
    public string Status { get; set; } = "candidate";
    public string ImpactLevel { get; set; } = "future_carry";
    public bool RequiresConfirmation { get; set; }
    public string ConflictStatus { get; set; } = "unknown";
    public string? DecisionReason { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }
    public DateTime? ExecutedAt { get; set; }

    public User User { get; set; } = null!;
    public NovelProject Project { get; set; } = null!;

    [NotMapped]
    public bool IsAccepted => string.Equals(Status, "accepted", StringComparison.OrdinalIgnoreCase);
}
