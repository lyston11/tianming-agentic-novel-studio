namespace TM.Web.NovelAgentWeb.DataMigration;

public sealed record MigrationSkippedItem(string EntityType, string SourceId, string Reason);

public sealed record MigrationImportReport(
    string UserId,
    int ImportedCount,
    int ReusedCount,
    IReadOnlyDictionary<string, int> ImportedByEntity,
    IReadOnlyList<MigrationSkippedItem> Skipped);

public sealed record MigrationVerificationCheck(
    string Name,
    long Expected,
    long Actual,
    bool Passed,
    string Detail = "");

public sealed record MigrationVerificationReport(
    string UserId,
    bool IsValid,
    IReadOnlyList<MigrationVerificationCheck> Checks,
    IReadOnlyList<string> Errors);

public sealed record TargetArchitectureCutoverReport(
    string UserId,
    bool Ready,
    MigrationVerificationReport Migration,
    int ActiveLegacyRuns,
    int PendingOutboxEvents,
    int RebuiltVectorCount,
    IReadOnlyList<string> Blockers);
