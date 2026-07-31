namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class CreativeGoal
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string SourceSessionId { get; set; } = string.Empty;
    public string GoalType { get; set; } = string.Empty;
    public string CollaborationMode { get; set; } = "coauthor";
    public string HumanReadableObjective { get; set; } = string.Empty;
    public string TargetChapterRangeJson { get; set; } = "{}";
    public string SuccessCriteriaJson { get; set; } = "[]";
    public string MustPreserveJson { get; set; } = "[]";
    public string MustHappenJson { get; set; } = "[]";
    public string MustNotChangeJson { get; set; } = "[]";
    public string AcceptancePolicyJson { get; set; } = "{}";
    public string ReworkPolicyJson { get; set; } = "{}";
    public decimal TotalCostLimit { get; set; }
    public decimal ReservedCost { get; set; }
    public decimal ActualCost { get; set; }
    public string CanonBaselineVersion { get; set; } = string.Empty;
    public string KnowledgeSnapshotVersion { get; set; } = string.Empty;
    public string QualityContractVersion { get; set; } = string.Empty;
    public string StyleProfileVersion { get; set; } = string.Empty;
    public string ModelConfigVersionsJson { get; set; } = "{}";
    public string ProtocolVersionsJson { get; set; } = "{}";
    public string Status { get; set; } = "committed";
    public long AggregateVersion { get; set; } = 1;
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
