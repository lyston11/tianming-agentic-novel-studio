using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterPlanning;
using TM.Services.Modules.ProjectData.Models.Generate.ChapterBlueprint;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;
using TM.Services.Modules.ProjectData.Models.Context;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Services.Modules.ProjectData.Models.Design.Templates;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public static class LayeredContextConfig
    {
        private static readonly System.Text.Json.JsonSerializerOptions CaseInsensitiveJsonOptions = new() { PropertyNameCaseInsensitive = true };
        private static readonly System.Text.Json.JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

        public static int PreviousSummaryCount { get; set; } = 30;

        public const int MdFallbackMaxDistance = 1;

        public static int MdSummaryExtractLength { get; set; } = 500;

        public static int ActiveEntityWindowChapters { get; set; } = 8;

        public static int ActiveEntityWindowMaxCount { get; set; } = 25;

        public static int SummaryRecentWindowCount { get; set; } = 30;

        public static int MilestoneAnchorInterval { get; set; } = 8;

        public static int VolumeMilestoneMaxChars { get; set; } = 20000;

        public static int SummaryMaxCrossVolumeAnchors { get; set; } = 100;

        public static int VolumeMilestoneTailRecentCount { get; set; } = 15;

        public const int PreviousChapterTailLength = 1000;

        public const int PreviousChapterTailMinLength = 200;

        public static int LedgerCharacterStateKeepRecent { get; set; } = 10000000;
        public static int LedgerConflictProgressKeepRecent { get; set; } = 2000000;
        public static int LedgerPlotPointsKeepRecent { get; set; } = 10000000;
        public static int LedgerLocationStateKeepRecent { get; set; } = 5000000;
        public static int LedgerFactionStateKeepRecent { get; set; } = 5000000;
        public static int LedgerTimelineKeepRecent { get; set; } = 10000000;
        public static int LedgerMovementKeepRecent { get; set; } = 5000000;
        public static int LedgerItemStateKeepRecent { get; set; } = 5000000;
        public static int LedgerConstraintHistoryKeepRecent { get; set; } = 5000000;
        public static int LedgerMaxCriticalPerEntity { get; set; } = int.MaxValue;
        public static int LedgerImportantKeepRecent { get; set; } = 2000000;
        public static int LedgerNormalSampleInterval { get; set; } = 50;
        public static int DriftWarningsMaxPerEntity { get; set; } = 100000;
        public static int DriftEscalateThreshold { get; set; } = 3;
        public static int DriftMinNameLength { get; set; } = 2;
        public static int DriftWarningsRecentChapterWindow { get; set; } = 50;
        public static int PledgeMaxDanglingChapters { get; set; } = 100;
        public static int DeadlineMaxDanglingChapters { get; set; } = 50;
        public static int SnapshotMaxFactionInject { get; set; } = 30;
        public static int SnapshotMaxItemInject { get; set; } = 50;
        public static int SnapshotMaxTimelineInject { get; set; } = 5;
        public static int SnapshotMaxCharacterInject { get; set; } = 50;
        public static int SnapshotMaxLocationInject { get; set; } = 30;
        public static int SnapshotMaxConflictInject { get; set; } = 20;
        public static int SnapshotMaxForeshadowInject { get; set; } = 30;
        public static int SnapshotMaxSecretInject { get; set; } = 20;
        public static int SnapshotMaxPledgeInject { get; set; } = 15;
        public static int SnapshotMaxDeadlineInject { get; set; } = 15;
        public static int MilestoneMaxPreviousVolumes { get; set; } = 12;
        public static int ArchiveMaxPreviousVolumes { get; set; } = 8;

        public static int ArchiveInjectMaxCharacterStates { get; set; } = 60;

        public static int ArchiveInjectMaxConflictProgress { get; set; } = 25;

        public static int ArchiveInjectMaxTimelineEntries { get; set; } = 10;

        public static int ArchiveInjectMaxCharacterLocations { get; set; } = 50;

        public static int ArchiveInjectMaxFactionStates { get; set; } = 20;

        public static int ArchiveInjectMaxLocationStates { get; set; } = 20;

        public static int ArchiveInjectMaxFieldChars { get; set; } = 300;

        public static int ArchiveInjectMaxItemStates { get; set; } = 50;

        public static int ArchiveInjectMaxForeshadowingStatus { get; set; } = 40;
        public static int ArchiveInjectMaxSecretStates { get; set; } = 20;
        public static int ArchiveInjectMaxPledgeStates { get; set; } = 15;
        public static int ArchiveInjectMaxDeadlineStates { get; set; } = 15;

        public static bool SemanticRecallEnabled { get; set; } = true;
        public static int SemanticForeshadowingTopK { get; set; } = 5;
        public static int SemanticCharacterTopK { get; set; } = 3;
        public static int SemanticGeneralTopK { get; set; } = 5;
        public static int SemanticQuotaForeshadowing { get; set; } = 2;
        public static int SemanticQuotaCharacter { get; set; } = 1;
        public static int SemanticQuotaGeneral { get; set; } = 2;
        public static int SemanticRrfK { get; set; } = 60;
        public static int FirstDescriptionWindowSize { get; set; } = 1;
        public static double FirstDescriptionThreshold { get; set; } = 0.5;
        public static int EmbeddingIdleReleaseMinutes { get; set; } = 10;

        public const int EmbeddingMaxChars = 500;

        public const int EmbeddingChunkOverlap = 50;

        public static int SemanticTfRecallTopK { get; set; } = 10;

        public static int SemanticKeywordRecallTopK { get; set; } = 10;

        public static int KeywordIndexMaxChaptersPerTerm { get; set; } = 50;

        public static readonly object ConfigLock = new();

        public static LayeredContextConfigSnapshot TakeSnapshot()
        {
            lock (ConfigLock)
            {
                return new LayeredContextConfigSnapshot
                {
                    PreviousSummaryCount = PreviousSummaryCount,
                    MdSummaryExtractLength = MdSummaryExtractLength,
                    ActiveEntityWindowChapters = ActiveEntityWindowChapters,
                    ActiveEntityWindowMaxCount = ActiveEntityWindowMaxCount,
                    SummaryRecentWindowCount = SummaryRecentWindowCount,
                    MilestoneAnchorInterval = MilestoneAnchorInterval,
                    VolumeMilestoneMaxChars = VolumeMilestoneMaxChars,
                    SummaryMaxCrossVolumeAnchors = SummaryMaxCrossVolumeAnchors,
                    VolumeMilestoneTailRecentCount = VolumeMilestoneTailRecentCount,
                    LedgerCharacterStateKeepRecent = LedgerCharacterStateKeepRecent,
                    LedgerConflictProgressKeepRecent = LedgerConflictProgressKeepRecent,
                    LedgerPlotPointsKeepRecent = LedgerPlotPointsKeepRecent,
                    LedgerLocationStateKeepRecent = LedgerLocationStateKeepRecent,
                    LedgerFactionStateKeepRecent = LedgerFactionStateKeepRecent,
                    LedgerTimelineKeepRecent = LedgerTimelineKeepRecent,
                    LedgerMovementKeepRecent = LedgerMovementKeepRecent,
                    LedgerItemStateKeepRecent = LedgerItemStateKeepRecent,
                    LedgerConstraintHistoryKeepRecent = LedgerConstraintHistoryKeepRecent,
                    LedgerMaxCriticalPerEntity = LedgerMaxCriticalPerEntity,
                    LedgerImportantKeepRecent = LedgerImportantKeepRecent,
                    LedgerNormalSampleInterval = LedgerNormalSampleInterval,
                    DriftWarningsMaxPerEntity = DriftWarningsMaxPerEntity,
                    DriftEscalateThreshold = DriftEscalateThreshold,
                    DriftMinNameLength = DriftMinNameLength,
                    DriftWarningsRecentChapterWindow = DriftWarningsRecentChapterWindow,
                    PledgeMaxDanglingChapters = PledgeMaxDanglingChapters,
                    DeadlineMaxDanglingChapters = DeadlineMaxDanglingChapters,
                    SnapshotMaxFactionInject = SnapshotMaxFactionInject,
                    SnapshotMaxItemInject = SnapshotMaxItemInject,
                    SnapshotMaxTimelineInject = SnapshotMaxTimelineInject,
                    MilestoneMaxPreviousVolumes = MilestoneMaxPreviousVolumes,
                    ArchiveMaxPreviousVolumes = ArchiveMaxPreviousVolumes,
                    ArchiveInjectMaxCharacterStates = ArchiveInjectMaxCharacterStates,
                    ArchiveInjectMaxConflictProgress = ArchiveInjectMaxConflictProgress,
                    ArchiveInjectMaxTimelineEntries = ArchiveInjectMaxTimelineEntries,
                    ArchiveInjectMaxCharacterLocations = ArchiveInjectMaxCharacterLocations,
                    ArchiveInjectMaxFactionStates = ArchiveInjectMaxFactionStates,
                    ArchiveInjectMaxLocationStates = ArchiveInjectMaxLocationStates,
                    ArchiveInjectMaxFieldChars = ArchiveInjectMaxFieldChars,
                    ArchiveInjectMaxItemStates = ArchiveInjectMaxItemStates,
                    ArchiveInjectMaxForeshadowingStatus = ArchiveInjectMaxForeshadowingStatus,
                    ArchiveInjectMaxSecretStates = ArchiveInjectMaxSecretStates,
                    ArchiveInjectMaxPledgeStates = ArchiveInjectMaxPledgeStates,
                    ArchiveInjectMaxDeadlineStates = ArchiveInjectMaxDeadlineStates,
                    SnapshotMaxCharacterInject = SnapshotMaxCharacterInject,
                    SnapshotMaxLocationInject = SnapshotMaxLocationInject,
                    SnapshotMaxConflictInject = SnapshotMaxConflictInject,
                    SnapshotMaxForeshadowInject = SnapshotMaxForeshadowInject,
                    SnapshotMaxSecretInject = SnapshotMaxSecretInject,
                    SnapshotMaxPledgeInject = SnapshotMaxPledgeInject,
                    SnapshotMaxDeadlineInject = SnapshotMaxDeadlineInject,
                    SemanticRecallEnabled = SemanticRecallEnabled,
                    SemanticForeshadowingTopK = SemanticForeshadowingTopK,
                    SemanticCharacterTopK = SemanticCharacterTopK,
                    SemanticGeneralTopK = SemanticGeneralTopK,
                    SemanticQuotaForeshadowing = SemanticQuotaForeshadowing,
                    SemanticQuotaCharacter = SemanticQuotaCharacter,
                    SemanticQuotaGeneral = SemanticQuotaGeneral,
                    SemanticRrfK = SemanticRrfK,
                    FirstDescriptionWindowSize = FirstDescriptionWindowSize,
                    FirstDescriptionThreshold = FirstDescriptionThreshold,
                    EmbeddingIdleReleaseMinutes = EmbeddingIdleReleaseMinutes,
                    SemanticTfRecallTopK = SemanticTfRecallTopK,
                    SemanticKeywordRecallTopK = SemanticKeywordRecallTopK,
                    KeywordIndexMaxChaptersPerTerm = KeywordIndexMaxChaptersPerTerm,
                };
            }
        }

        private static readonly string SettingsFileName = "layered_context_settings.json";

        public static async Task InitializeFromStorageAsync()
        {
            try
            {
                var path = System.IO.Path.Combine(
                    StoragePathHelper.GetServicesStoragePath("Settings"), SettingsFileName);
                if (!System.IO.File.Exists(path)) return;

                await using var cfgStream = System.IO.File.OpenRead(path);
                var dict = await System.Text.Json.JsonSerializer.DeserializeAsync<Dictionary<string, System.Text.Json.JsonElement>>(cfgStream, CaseInsensitiveJsonOptions).ConfigureAwait(false);
                if (dict == null) return;

                void TrySetInt(string key, Action<int> setter, int min, int max)
                {
                    if (dict.TryGetValue(key, out var el) && el.TryGetInt32(out var v))
                        setter(Math.Clamp(v, min, max));
                }

                void TrySetBool(string key, Action<bool> setter)
                {
                    if (!dict.TryGetValue(key, out var el)) return;
                    if (el.ValueKind == System.Text.Json.JsonValueKind.True) setter(true);
                    else if (el.ValueKind == System.Text.Json.JsonValueKind.False) setter(false);
                    else if (el.ValueKind == System.Text.Json.JsonValueKind.Number && el.TryGetInt32(out var n)) setter(n != 0);
                }

                void TrySetDouble(string key, Action<double> setter, double min, double max)
                {
                    if (!dict.TryGetValue(key, out var el)) return;
                    if (el.ValueKind == System.Text.Json.JsonValueKind.Number && el.TryGetDouble(out var v))
                        setter(Math.Clamp(v, min, max));
                }

                TrySetInt(nameof(ActiveEntityWindowChapters), v => ActiveEntityWindowChapters = v, 1, 1000);
                TrySetInt(nameof(ActiveEntityWindowMaxCount), v => ActiveEntityWindowMaxCount = v, 0, 10000);
                TrySetInt(nameof(SummaryRecentWindowCount), v => SummaryRecentWindowCount = v, 0, 2000);
                TrySetInt(nameof(PreviousSummaryCount), v => PreviousSummaryCount = v, 0, 2000);
                TrySetInt(nameof(MilestoneAnchorInterval), v => MilestoneAnchorInterval = v, 1, 1000);
                TrySetInt(nameof(VolumeMilestoneMaxChars), v => VolumeMilestoneMaxChars = v, 0, 2000000);
                TrySetInt(nameof(SummaryMaxCrossVolumeAnchors), v => SummaryMaxCrossVolumeAnchors = v, 0, 10000);
                TrySetInt(nameof(VolumeMilestoneTailRecentCount), v => VolumeMilestoneTailRecentCount = v, 0, 2000);
                TrySetInt(nameof(MilestoneMaxPreviousVolumes), v => MilestoneMaxPreviousVolumes = v, 0, 2000);
                TrySetInt(nameof(ArchiveMaxPreviousVolumes), v => ArchiveMaxPreviousVolumes = v, 0, 2000);
                TrySetInt(nameof(SnapshotMaxFactionInject), v => SnapshotMaxFactionInject = v, 0, 10000);
                TrySetInt(nameof(SnapshotMaxItemInject), v => SnapshotMaxItemInject = v, 0, 10000);
                TrySetInt(nameof(SnapshotMaxTimelineInject), v => SnapshotMaxTimelineInject = v, 1, 50);
                TrySetInt(nameof(ArchiveInjectMaxCharacterStates), v => ArchiveInjectMaxCharacterStates = v, 0, 10000);
                TrySetInt(nameof(ArchiveInjectMaxConflictProgress), v => ArchiveInjectMaxConflictProgress = v, 0, 10000);
                TrySetInt(nameof(ArchiveInjectMaxTimelineEntries), v => ArchiveInjectMaxTimelineEntries = v, 0, 10000);
                TrySetInt(nameof(ArchiveInjectMaxCharacterLocations), v => ArchiveInjectMaxCharacterLocations = v, 0, 10000);
                TrySetInt(nameof(ArchiveInjectMaxFactionStates), v => ArchiveInjectMaxFactionStates = v, 0, 10000);
                TrySetInt(nameof(ArchiveInjectMaxLocationStates), v => ArchiveInjectMaxLocationStates = v, 0, 10000);
                TrySetInt(nameof(ArchiveInjectMaxFieldChars), v => ArchiveInjectMaxFieldChars = v, 0, 1000000);
                TrySetInt(nameof(ArchiveInjectMaxItemStates), v => ArchiveInjectMaxItemStates = v, 0, 10000);
                TrySetInt(nameof(ArchiveInjectMaxForeshadowingStatus), v => ArchiveInjectMaxForeshadowingStatus = v, 0, 10000);
                TrySetInt(nameof(LedgerConstraintHistoryKeepRecent), v => LedgerConstraintHistoryKeepRecent = v, 0, int.MaxValue);
                TrySetInt(nameof(ArchiveInjectMaxSecretStates), v => ArchiveInjectMaxSecretStates = v, 0, 10000);
                TrySetInt(nameof(ArchiveInjectMaxPledgeStates), v => ArchiveInjectMaxPledgeStates = v, 0, 10000);
                TrySetInt(nameof(ArchiveInjectMaxDeadlineStates), v => ArchiveInjectMaxDeadlineStates = v, 0, 10000);
                TrySetInt(nameof(SnapshotMaxCharacterInject), v => SnapshotMaxCharacterInject = v, 0, 10000);
                TrySetInt(nameof(SnapshotMaxLocationInject), v => SnapshotMaxLocationInject = v, 0, 10000);
                TrySetInt(nameof(SnapshotMaxConflictInject), v => SnapshotMaxConflictInject = v, 0, 10000);
                TrySetInt(nameof(SnapshotMaxForeshadowInject), v => SnapshotMaxForeshadowInject = v, 0, 10000);
                TrySetInt(nameof(SnapshotMaxSecretInject), v => SnapshotMaxSecretInject = v, 0, 10000);
                TrySetInt(nameof(SnapshotMaxPledgeInject), v => SnapshotMaxPledgeInject = v, 0, 10000);
                TrySetInt(nameof(SnapshotMaxDeadlineInject), v => SnapshotMaxDeadlineInject = v, 0, 10000);

                TrySetBool(nameof(SemanticRecallEnabled), v => SemanticRecallEnabled = v);
                TrySetInt(nameof(SemanticForeshadowingTopK), v => SemanticForeshadowingTopK = v, 1, 20);
                TrySetInt(nameof(SemanticCharacterTopK), v => SemanticCharacterTopK = v, 1, 20);
                TrySetInt(nameof(SemanticGeneralTopK), v => SemanticGeneralTopK = v, 1, 20);
                TrySetInt(nameof(SemanticQuotaForeshadowing), v => SemanticQuotaForeshadowing = v, 0, 10);
                TrySetInt(nameof(SemanticQuotaCharacter), v => SemanticQuotaCharacter = v, 0, 10);
                TrySetInt(nameof(SemanticQuotaGeneral), v => SemanticQuotaGeneral = v, 0, 10);
                TrySetInt(nameof(SemanticRrfK), v => SemanticRrfK = v, 1, 200);
                TrySetInt(nameof(FirstDescriptionWindowSize), v => FirstDescriptionWindowSize = v, 1, 5);
                TrySetDouble(nameof(FirstDescriptionThreshold), v => FirstDescriptionThreshold = v, 0.0, 1.0);
                TrySetInt(nameof(EmbeddingIdleReleaseMinutes), v => EmbeddingIdleReleaseMinutes = v, 0, 120);

                TrySetInt(nameof(SemanticTfRecallTopK), v => SemanticTfRecallTopK = v, 1, 50);
                TrySetInt(nameof(SemanticKeywordRecallTopK), v => SemanticKeywordRecallTopK = v, 1, 50);
                TrySetInt(nameof(KeywordIndexMaxChaptersPerTerm), v => KeywordIndexMaxChaptersPerTerm = v, 10, 10000);

                TM.App.Log("[LayeredContextConfig] 已从本地存储加载参数");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LayeredContextConfig] 加载本地参数失败，使用默认值: {ex.Message}");
            }
        }

        public static async Task SaveToStorageAsync()
        {
            try
            {
                var dir = StoragePathHelper.GetServicesStoragePath("Settings");
                System.IO.Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, SettingsFileName);
                var dict = new Dictionary<string, object>
                {
                    [nameof(ActiveEntityWindowChapters)] = ActiveEntityWindowChapters,
                    [nameof(ActiveEntityWindowMaxCount)] = ActiveEntityWindowMaxCount,
                    [nameof(SummaryRecentWindowCount)] = SummaryRecentWindowCount,
                    [nameof(PreviousSummaryCount)] = PreviousSummaryCount,
                    [nameof(MilestoneAnchorInterval)] = MilestoneAnchorInterval,
                    [nameof(VolumeMilestoneMaxChars)] = VolumeMilestoneMaxChars,
                    [nameof(SummaryMaxCrossVolumeAnchors)] = SummaryMaxCrossVolumeAnchors,
                    [nameof(VolumeMilestoneTailRecentCount)] = VolumeMilestoneTailRecentCount,
                    [nameof(MilestoneMaxPreviousVolumes)] = MilestoneMaxPreviousVolumes,
                    [nameof(ArchiveMaxPreviousVolumes)] = ArchiveMaxPreviousVolumes,
                    [nameof(SnapshotMaxFactionInject)] = SnapshotMaxFactionInject,
                    [nameof(SnapshotMaxItemInject)] = SnapshotMaxItemInject,
                    [nameof(SnapshotMaxTimelineInject)] = SnapshotMaxTimelineInject,
                    [nameof(ArchiveInjectMaxCharacterStates)] = ArchiveInjectMaxCharacterStates,
                    [nameof(ArchiveInjectMaxConflictProgress)] = ArchiveInjectMaxConflictProgress,
                    [nameof(ArchiveInjectMaxTimelineEntries)] = ArchiveInjectMaxTimelineEntries,
                    [nameof(ArchiveInjectMaxCharacterLocations)] = ArchiveInjectMaxCharacterLocations,
                    [nameof(ArchiveInjectMaxFactionStates)] = ArchiveInjectMaxFactionStates,
                    [nameof(ArchiveInjectMaxLocationStates)] = ArchiveInjectMaxLocationStates,
                    [nameof(ArchiveInjectMaxFieldChars)] = ArchiveInjectMaxFieldChars,
                    [nameof(ArchiveInjectMaxItemStates)] = ArchiveInjectMaxItemStates,
                    [nameof(ArchiveInjectMaxForeshadowingStatus)] = ArchiveInjectMaxForeshadowingStatus,
                    [nameof(LedgerConstraintHistoryKeepRecent)] = LedgerConstraintHistoryKeepRecent,
                    [nameof(ArchiveInjectMaxSecretStates)] = ArchiveInjectMaxSecretStates,
                    [nameof(ArchiveInjectMaxPledgeStates)] = ArchiveInjectMaxPledgeStates,
                    [nameof(ArchiveInjectMaxDeadlineStates)] = ArchiveInjectMaxDeadlineStates,
                    [nameof(SnapshotMaxCharacterInject)] = SnapshotMaxCharacterInject,
                    [nameof(SnapshotMaxLocationInject)] = SnapshotMaxLocationInject,
                    [nameof(SnapshotMaxConflictInject)] = SnapshotMaxConflictInject,
                    [nameof(SnapshotMaxForeshadowInject)] = SnapshotMaxForeshadowInject,
                    [nameof(SnapshotMaxSecretInject)] = SnapshotMaxSecretInject,
                    [nameof(SnapshotMaxPledgeInject)] = SnapshotMaxPledgeInject,
                    [nameof(SnapshotMaxDeadlineInject)] = SnapshotMaxDeadlineInject,
                    [nameof(SemanticRecallEnabled)] = SemanticRecallEnabled,
                    [nameof(SemanticForeshadowingTopK)] = SemanticForeshadowingTopK,
                    [nameof(SemanticCharacterTopK)] = SemanticCharacterTopK,
                    [nameof(SemanticGeneralTopK)] = SemanticGeneralTopK,
                    [nameof(SemanticQuotaForeshadowing)] = SemanticQuotaForeshadowing,
                    [nameof(SemanticQuotaCharacter)] = SemanticQuotaCharacter,
                    [nameof(SemanticQuotaGeneral)] = SemanticQuotaGeneral,
                    [nameof(SemanticRrfK)] = SemanticRrfK,
                    [nameof(FirstDescriptionWindowSize)] = FirstDescriptionWindowSize,
                    [nameof(FirstDescriptionThreshold)] = FirstDescriptionThreshold,
                    [nameof(EmbeddingIdleReleaseMinutes)] = EmbeddingIdleReleaseMinutes,
                    [nameof(SemanticTfRecallTopK)] = SemanticTfRecallTopK,
                    [nameof(SemanticKeywordRecallTopK)] = SemanticKeywordRecallTopK,
                    [nameof(KeywordIndexMaxChaptersPerTerm)] = KeywordIndexMaxChaptersPerTerm,
                };
                var tmpPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await using (var stream = System.IO.File.Create(tmpPath))
                {
                    await System.Text.Json.JsonSerializer.SerializeAsync(stream, dict, IndentedJsonOptions).ConfigureAwait(false);
                }
                System.IO.File.Move(tmpPath, path, overwrite: true);
                TM.App.Log("[LayeredContextConfig] 参数已保存到本地存储");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[LayeredContextConfig] 保存本地参数失败: {ex.Message}");
            }
        }
    }

    public sealed record LayeredContextConfigSnapshot
    {
        public int PreviousSummaryCount { get; init; }
        public int MdSummaryExtractLength { get; init; }
        public int ActiveEntityWindowChapters { get; init; }
        public int ActiveEntityWindowMaxCount { get; init; }
        public int SummaryRecentWindowCount { get; init; }
        public int MilestoneAnchorInterval { get; init; }
        public int VolumeMilestoneMaxChars { get; init; }
        public int SummaryMaxCrossVolumeAnchors { get; init; }
        public int VolumeMilestoneTailRecentCount { get; init; }
        public int LedgerCharacterStateKeepRecent { get; init; }
        public int LedgerConflictProgressKeepRecent { get; init; }
        public int LedgerPlotPointsKeepRecent { get; init; }
        public int LedgerLocationStateKeepRecent { get; init; }
        public int LedgerFactionStateKeepRecent { get; init; }
        public int LedgerTimelineKeepRecent { get; init; }
        public int LedgerMovementKeepRecent { get; init; }
        public int LedgerItemStateKeepRecent { get; init; }
        public int LedgerMaxCriticalPerEntity { get; init; }
        public int LedgerImportantKeepRecent { get; init; }
        public int LedgerNormalSampleInterval { get; init; }
        public int DriftWarningsMaxPerEntity { get; init; }
        public int DriftEscalateThreshold { get; init; }
        public int DriftMinNameLength { get; init; }
        public int DriftWarningsRecentChapterWindow { get; init; }
        public int PledgeMaxDanglingChapters { get; init; }
        public int DeadlineMaxDanglingChapters { get; init; }
        public int SnapshotMaxFactionInject { get; init; }
        public int SnapshotMaxItemInject { get; init; }
        public int SnapshotMaxTimelineInject { get; init; }
        public int MilestoneMaxPreviousVolumes { get; init; }
        public int ArchiveMaxPreviousVolumes { get; init; }
        public int ArchiveInjectMaxCharacterStates { get; init; }
        public int ArchiveInjectMaxConflictProgress { get; init; }
        public int ArchiveInjectMaxTimelineEntries { get; init; }
        public int ArchiveInjectMaxCharacterLocations { get; init; }
        public int ArchiveInjectMaxFactionStates { get; init; }
        public int ArchiveInjectMaxLocationStates { get; init; }
        public int ArchiveInjectMaxFieldChars { get; init; }
        public int ArchiveInjectMaxItemStates { get; init; }
        public int ArchiveInjectMaxForeshadowingStatus { get; init; }
        public int LedgerConstraintHistoryKeepRecent { get; init; }
        public int ArchiveInjectMaxSecretStates { get; init; }
        public int ArchiveInjectMaxPledgeStates { get; init; }
        public int ArchiveInjectMaxDeadlineStates { get; init; }
        public int SnapshotMaxCharacterInject { get; init; }
        public int SnapshotMaxLocationInject { get; init; }
        public int SnapshotMaxConflictInject { get; init; }
        public int SnapshotMaxForeshadowInject { get; init; }
        public int SnapshotMaxSecretInject { get; init; }
        public int SnapshotMaxPledgeInject { get; init; }
        public int SnapshotMaxDeadlineInject { get; init; }

        public bool SemanticRecallEnabled { get; init; }
        public int SemanticForeshadowingTopK { get; init; }
        public int SemanticCharacterTopK { get; init; }
        public int SemanticGeneralTopK { get; init; }
        public int SemanticQuotaForeshadowing { get; init; }
        public int SemanticQuotaCharacter { get; init; }
        public int SemanticQuotaGeneral { get; init; }
        public int SemanticRrfK { get; init; }
        public int FirstDescriptionWindowSize { get; init; }
        public double FirstDescriptionThreshold { get; init; }
        public int EmbeddingIdleReleaseMinutes { get; init; }
        public int SemanticTfRecallTopK { get; init; }
        public int SemanticKeywordRecallTopK { get; init; }
        public int KeywordIndexMaxChaptersPerTerm { get; init; }
    }

    public partial class GuideContextService : Interfaces.IGuideContextService
    {
        private readonly FactSnapshotExtractor _factSnapshotExtractor;
        private readonly ChapterSummaryStore _summaryStore;
        private readonly ChapterMilestoneStore _milestoneStore;

        private static string[] _cachedChapterIds = Array.Empty<string>();
        private static string _chapterIdsCachedForPath = string.Empty;
        private static DateTime _chapterIdsCachedAt = DateTime.MinValue;
        private static readonly object _chapterIdsCacheLock = new();

        public GuideContextService(FactSnapshotExtractor factSnapshotExtractor, ChapterSummaryStore summaryStore, ChapterMilestoneStore milestoneStore)
        {
            _factSnapshotExtractor = factSnapshotExtractor;
            _summaryStore = summaryStore;
            _milestoneStore = milestoneStore;

            CacheInvalidated += (_, _) => ClearCache();

            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) => ClearCache();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[GuideContextService] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        public static event EventHandler? CacheInvalidated;

        public static void RaiseCacheInvalidated()
        {
            CacheInvalidated?.Invoke(null, EventArgs.Empty);
            TM.App.Log("[GuideContextService] 已触发全局缓存失效事件");
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static string TruncateString(string? text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= maxLength ? text : text[..maxLength] + "...";
        }

        private static List<VolumeFactArchive> BuildInjectableArchives(
            List<VolumeFactArchive> rawArchives,
            Models.Guides.ContextIdCollection? contextIds,
            LayeredContextConfigSnapshot cfg)
        {
            if (rawArchives == null || rawArchives.Count == 0)
                return new List<VolumeFactArchive>();

            var focusCharacters = contextIds?.Characters ?? new List<string>();
            var focusConflicts = contextIds?.Conflicts ?? new List<string>();
            var focusFactions = contextIds?.Factions ?? new List<string>();
            var focusLocations = contextIds?.Locations ?? new List<string>();

            var maxChars = cfg.ArchiveInjectMaxFieldChars;
            var maxCharacterStates = cfg.ArchiveInjectMaxCharacterStates;
            var maxConflicts = cfg.ArchiveInjectMaxConflictProgress;
            var maxTimeline = cfg.ArchiveInjectMaxTimelineEntries;
            var maxLocations = cfg.ArchiveInjectMaxCharacterLocations;
            var maxFactionStates = cfg.ArchiveInjectMaxFactionStates;
            var maxLocationStates = cfg.ArchiveInjectMaxLocationStates;

            var focusCharacterSet = new HashSet<string>(focusCharacters, StringComparer.OrdinalIgnoreCase);
            var focusConflictSet = new HashSet<string>(focusConflicts, StringComparer.OrdinalIgnoreCase);
            var focusFactionSet = new HashSet<string>(focusFactions, StringComparer.OrdinalIgnoreCase);
            var focusLocationSet = new HashSet<string>(focusLocations, StringComparer.OrdinalIgnoreCase);

            var chapterComparer = Comparer<string>.Create(ChapterParserHelper.CompareChapterId);

            var result = new List<VolumeFactArchive>(rawArchives.Count);
            foreach (var archive in rawArchives)
            {
                if (archive == null) continue;

                var trimmed = new VolumeFactArchive
                {
                    VolumeNumber = archive.VolumeNumber,
                    LastChapterId = archive.LastChapterId ?? string.Empty,
                    ArchivedAt = archive.ArchivedAt,
                    CharacterStates = new List<CharacterStateSnapshot>(),
                    ConflictProgress = new List<ConflictProgressSnapshot>(),
                    ForeshadowingStatus = new List<ForeshadowingStatusSnapshot>(),
                    LocationStates = new List<LocationStateSnapshot>(),
                    FactionStates = new List<FactionStateSnapshot>(),
                    ItemStates = new List<ItemStateSnapshot>(),
                    SecretStates = new List<SecretStateSnapshot>(),
                    Timeline = new List<TimelineSnapshot>(),
                    CharacterLocations = new List<CharacterLocationSnapshot>(),
                    PledgeStates = new List<PledgeStateSnapshot>(),
                    DeadlineStates = new List<DeadlineStateSnapshot>()
                };

                if (archive.CharacterStates != null && archive.CharacterStates.Count > 0)
                {
                    var selected = (focusCharacterSet.Count > 0
                        ? archive.CharacterStates.Where(s => !string.IsNullOrWhiteSpace(s.Id) && focusCharacterSet.Contains(s.Id))
                        : archive.CharacterStates.Where(s => !string.IsNullOrWhiteSpace(s.Id)))
                        .OrderByDescending(s => s.ChapterId ?? string.Empty, chapterComparer);

                    foreach (var cs in selected)
                    {
                        if (trimmed.CharacterStates.Count >= maxCharacterStates) break;
                        trimmed.CharacterStates.Add(new CharacterStateSnapshot
                        {
                            Id = cs.Id ?? string.Empty,
                            Name = cs.Name ?? string.Empty,
                            Stage = TruncateString(cs.Stage, maxChars),
                            Abilities = TruncateString(cs.Abilities, maxChars),
                            Relationships = TruncateString(cs.Relationships, maxChars),
                            ChapterId = cs.ChapterId ?? string.Empty
                        });
                    }
                }

                if (archive.ConflictProgress != null && archive.ConflictProgress.Count > 0)
                {
                    var selected = (focusConflictSet.Count > 0
                        ? archive.ConflictProgress.Where(c => !string.IsNullOrWhiteSpace(c.Id) && focusConflictSet.Contains(c.Id))
                        : archive.ConflictProgress.Where(c => !string.IsNullOrWhiteSpace(c.Status)))
                        .OrderByDescending(c => c.RecentProgress != null && c.RecentProgress.Count > 0 ? 1 : 0);

                    foreach (var cf in selected)
                    {
                        if (trimmed.ConflictProgress.Count >= maxConflicts) break;
                        var progress = cf.RecentProgress ?? new List<string>();
                        trimmed.ConflictProgress.Add(new ConflictProgressSnapshot
                        {
                            Id = cf.Id ?? string.Empty,
                            Name = cf.Name ?? string.Empty,
                            Status = TruncateString(cf.Status, maxChars),
                            RecentProgress = progress
                                .Where(p => !string.IsNullOrWhiteSpace(p))
                                .Take(3)
                                .Select(p => TruncateString(p, maxChars))
                                .ToList()
                        });
                    }
                }

                if (archive.Timeline != null && archive.Timeline.Count > 0 && maxTimeline > 0)
                {
                    var take = Math.Min(maxTimeline, archive.Timeline.Count);
                    var skip = Math.Max(0, archive.Timeline.Count - take);
                    foreach (var t in archive.Timeline.Skip(skip))
                    {
                        trimmed.Timeline.Add(new TimelineSnapshot
                        {
                            ChapterId = t.ChapterId ?? string.Empty,
                            TimePeriod = TruncateString(t.TimePeriod, maxChars),
                            ElapsedTime = TruncateString(t.ElapsedTime, maxChars),
                            KeyTimeEvent = TruncateString(t.KeyTimeEvent, maxChars)
                        });
                    }
                }

                if (archive.CharacterLocations != null && archive.CharacterLocations.Count > 0 && maxLocations > 0)
                {
                    var selected = (focusCharacterSet.Count > 0
                        ? archive.CharacterLocations.Where(l => !string.IsNullOrWhiteSpace(l.CharacterId) && focusCharacterSet.Contains(l.CharacterId))
                        : archive.CharacterLocations.Where(l => !string.IsNullOrWhiteSpace(l.CharacterId)))
                        .OrderByDescending(l => l.ChapterId ?? string.Empty, chapterComparer);

                    foreach (var loc in selected)
                    {
                        if (trimmed.CharacterLocations.Count >= maxLocations) break;
                        trimmed.CharacterLocations.Add(new CharacterLocationSnapshot
                        {
                            CharacterId = loc.CharacterId ?? string.Empty,
                            CharacterName = loc.CharacterName ?? string.Empty,
                            CurrentLocation = TruncateString(loc.CurrentLocation, maxChars),
                            ChapterId = loc.ChapterId ?? string.Empty
                        });
                    }
                }

                if (archive.FactionStates != null && archive.FactionStates.Count > 0 && maxFactionStates > 0)
                {
                    var selected = (focusFactionSet.Count > 0
                        ? archive.FactionStates.Where(f => !string.IsNullOrWhiteSpace(f.Id) && focusFactionSet.Contains(f.Id))
                        : archive.FactionStates.Where(f => !string.IsNullOrWhiteSpace(f.Status)))
                        .OrderByDescending(f => f.ChapterId ?? string.Empty, chapterComparer);

                    foreach (var fac in selected)
                    {
                        if (trimmed.FactionStates.Count >= maxFactionStates) break;
                        trimmed.FactionStates.Add(new FactionStateSnapshot
                        {
                            Id = fac.Id ?? string.Empty,
                            Name = fac.Name ?? string.Empty,
                            Status = TruncateString(fac.Status, maxChars),
                            ChapterId = fac.ChapterId ?? string.Empty
                        });
                    }
                }

                if (archive.LocationStates != null && archive.LocationStates.Count > 0 && maxLocationStates > 0)
                {
                    var selected = (focusLocationSet.Count > 0
                        ? archive.LocationStates.Where(l => !string.IsNullOrWhiteSpace(l.Id) && focusLocationSet.Contains(l.Id))
                        : archive.LocationStates.Where(l => !string.IsNullOrWhiteSpace(l.Status)))
                        .OrderByDescending(l => l.ChapterId ?? string.Empty, chapterComparer);

                    foreach (var locState in selected)
                    {
                        if (trimmed.LocationStates.Count >= maxLocationStates) break;
                        trimmed.LocationStates.Add(new LocationStateSnapshot
                        {
                            Id = locState.Id ?? string.Empty,
                            Name = locState.Name ?? string.Empty,
                            Status = TruncateString(locState.Status, maxChars),
                            ChapterId = locState.ChapterId ?? string.Empty
                        });
                    }
                }

                if (archive.ItemStates != null && archive.ItemStates.Count > 0)
                {
                    var selected = (focusCharacterSet.Count > 0
                        ? archive.ItemStates.Where(i => !string.IsNullOrWhiteSpace(i.CurrentHolder) && focusCharacterSet.Contains(i.CurrentHolder))
                        : archive.ItemStates.Where(i => !string.IsNullOrWhiteSpace(i.Id)))
                        .OrderByDescending(i => i.ChapterId ?? string.Empty, chapterComparer);
                    var maxItems = cfg.ArchiveInjectMaxItemStates;
                    foreach (var item in selected)
                    {
                        if (trimmed.ItemStates.Count >= maxItems) break;
                        trimmed.ItemStates.Add(new ItemStateSnapshot
                        {
                            Id = item.Id ?? string.Empty,
                            Name = item.Name ?? string.Empty,
                            CurrentHolder = item.CurrentHolder ?? string.Empty,
                            Status = TruncateString(item.Status, maxChars),
                            ChapterId = item.ChapterId ?? string.Empty
                        });
                    }
                }

                if (archive.ForeshadowingStatus != null && archive.ForeshadowingStatus.Count > 0)
                {
                    var maxForeshadowing = cfg.ArchiveInjectMaxForeshadowingStatus;
                    var foreshadowingSelected = archive.ForeshadowingStatus
                        .Where(f => !f.IsResolved)
                        .OrderByDescending(f => f.IsOverdue)
                        .ThenByDescending(f => f.IsSetup);
                    foreach (var fs in foreshadowingSelected)
                    {
                        if (trimmed.ForeshadowingStatus.Count >= maxForeshadowing) break;
                        trimmed.ForeshadowingStatus.Add(new ForeshadowingStatusSnapshot
                        {
                            Id = fs.Id ?? string.Empty,
                            Name = fs.Name ?? string.Empty,
                            IsSetup = fs.IsSetup,
                            IsResolved = fs.IsResolved,
                            IsOverdue = fs.IsOverdue,
                            SetupChapterId = fs.SetupChapterId ?? string.Empty,
                            PayoffChapterId = fs.PayoffChapterId ?? string.Empty
                        });
                    }
                }

                if (archive.SecretStates != null && archive.SecretStates.Count > 0)
                {
                    var maxSecrets = cfg.ArchiveInjectMaxSecretStates;
                    var secretComparer = Comparer<string>.Create(ChapterParserHelper.CompareChapterId);
                    var sortedSecrets = archive.SecretStates
                        .Where(s => !string.IsNullOrWhiteSpace(s.Id))
                        .OrderByDescending(s => s.KnowerIds?.Count ?? 0)
                        .ThenByDescending(s => s.ChapterId ?? string.Empty, secretComparer);
                    foreach (var s in sortedSecrets)
                    {
                        if (trimmed.SecretStates.Count >= maxSecrets) break;
                        trimmed.SecretStates.Add(new SecretStateSnapshot
                        {
                            Id = s.Id ?? string.Empty,
                            Name = s.Name ?? string.Empty,
                            KnowerIds = s.KnowerIds?.Take(20).ToList() ?? new(),
                            Status = TruncateString(s.Status, maxChars),
                            ChapterId = s.ChapterId ?? string.Empty
                        });
                    }
                }

                if (archive.PledgeStates != null && archive.PledgeStates.Count > 0)
                {
                    var maxConstraints = cfg.ArchiveInjectMaxPledgeStates;
                    var pledgeComparer = Comparer<string>.Create(ChapterParserHelper.CompareChapterId);
                    var sortedPledges = archive.PledgeStates
                        .Where(p => !string.IsNullOrWhiteSpace(p.Id))
                        .OrderByDescending(p => string.Equals(p.Status, "active", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                        .ThenByDescending(p => p.IsOverdue ? 1 : 0)
                        .ThenByDescending(p => p.ChapterId ?? string.Empty, pledgeComparer);
                    foreach (var p in sortedPledges)
                    {
                        if (trimmed.PledgeStates.Count >= maxConstraints) break;
                        trimmed.PledgeStates.Add(new PledgeStateSnapshot
                        {
                            Id = p.Id ?? string.Empty,
                            Name = p.Name ?? string.Empty,
                            Type = TruncateString(p.Type, maxChars),
                            Status = TruncateString(p.Status, maxChars),
                            PartyIds = TruncateString(p.PartyIds, maxChars),
                            Condition = TruncateString(p.Condition, maxChars),
                            Consequence = TruncateString(p.Consequence, maxChars),
                            ChapterId = p.ChapterId ?? string.Empty
                        });
                    }
                }

                if (archive.DeadlineStates != null && archive.DeadlineStates.Count > 0)
                {
                    var maxConstraints = cfg.ArchiveInjectMaxDeadlineStates;
                    var deadlineComparer = Comparer<string>.Create(ChapterParserHelper.CompareChapterId);
                    var sortedDeadlines = archive.DeadlineStates
                        .Where(d => !string.IsNullOrWhiteSpace(d.Id))
                        .OrderByDescending(d => string.Equals(d.Status, "active", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                        .ThenByDescending(d => d.IsOverdue ? 1 : 0)
                        .ThenByDescending(d => d.ChapterId ?? string.Empty, deadlineComparer);
                    foreach (var d in sortedDeadlines)
                    {
                        if (trimmed.DeadlineStates.Count >= maxConstraints) break;
                        trimmed.DeadlineStates.Add(new DeadlineStateSnapshot
                        {
                            Id = d.Id ?? string.Empty,
                            Name = d.Name ?? string.Empty,
                            Type = TruncateString(d.Type, maxChars),
                            Status = TruncateString(d.Status, maxChars),
                            Deadline = TruncateString(d.Deadline, maxChars),
                            TriggerCondition = TruncateString(d.TriggerCondition, maxChars),
                            Consequence = TruncateString(d.Consequence, maxChars),
                            PartyIds = TruncateString(d.PartyIds, maxChars),
                            ChapterId = d.ChapterId ?? string.Empty
                        });
                    }
                }

                result.Add(trimmed);
            }

            return result;
        }

        private readonly ConcurrentDictionary<string, Models.Design.Characters.CharacterRulesData> _characterCache = new();
        private readonly ConcurrentDictionary<string, Models.Design.Worldview.WorldRulesData> _worldRulesCache = new();
        private readonly ConcurrentDictionary<string, CreativeMaterialData> _templateCache = new();
        private readonly ConcurrentDictionary<string, Models.Design.Factions.FactionRulesData> _factionCache = new();
        private readonly ConcurrentDictionary<string, Models.Design.Location.LocationRulesData> _locationCache = new();
        private readonly ConcurrentDictionary<string, Models.Design.Plot.PlotRulesData> _plotRulesCache = new();
        private readonly ConcurrentDictionary<string, Models.Generate.StrategicOutline.OutlineData> _volumeCache = new();
        private readonly ConcurrentDictionary<string, ChapterData> _chapterPlanCache = new();
        private readonly ConcurrentDictionary<string, BlueprintData> _blueprintCache = new();
        private readonly ConcurrentDictionary<string, VolumeDesignData> _volumeDesignCache = new();

        private ContentGuide? _contentGuideCache;
        private readonly object _contentGuideCacheLock = new();
        private readonly SemaphoreSlim _contentGuideLoadLock = new(1, 1);

        private volatile ExpansionConfig? _expansionConfig;

        private volatile bool _cacheInitialized = false;
        private readonly SemaphoreSlim _cacheInitLock = new(1, 1);
        private int _cacheEpoch;

    }
}
