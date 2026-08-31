namespace TM.Web.NovelAgentWeb.Data.Entities;

public class GenerationGateReportRecord
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string RuntimeRunId { get; set; } = null!;
    public string ChapterId { get; set; } = null!;
    public string? PackageId { get; set; }
    public string ArtifactId { get; set; } = null!;
    public string Status { get; set; } = "pending";
    public string ReportJson { get; set; } = "{}";
    public bool ProtocolPassed { get; set; }
    public bool ChangesDetected { get; set; }
    public bool FactSnapshotPassed { get; set; }
    public bool BlueprintPassed { get; set; }
    public bool RagPassed { get; set; }
    public int IssueCount { get; set; }
    public int RepairHintCount { get; set; }
    public DateTime ValidatedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public NovelProject Project { get; set; } = null!;
    public Chapter? Chapter { get; set; }
}
