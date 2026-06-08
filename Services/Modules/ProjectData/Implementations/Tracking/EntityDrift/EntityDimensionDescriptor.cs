using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public enum DriftStrategy
    {
        AutoPatch,
        WarnOnly
    }

    public sealed record DriftEntityRecord(
        string Id,
        string Name,
        int VolumeNumber,
        System.Collections.Generic.IReadOnlyList<string> DriftWarnings);

    public sealed class EntityDimensionDescriptor
    {
        public string DimensionName { get; init; } = string.Empty;

        public string DimensionCode { get; init; } = string.Empty;

        public string GuideFileName { get; init; } = string.Empty;

        public bool IsVolumeScoped { get; init; }

        public DriftStrategy Strategy { get; init; }

        public string ChangeFieldName { get; init; } = string.Empty;

        public Func<GuideManager, int, Task<IReadOnlyList<DriftEntityRecord>>> LoadRecentEntitiesAsync { get; init; } = null!;

        public Func<ChapterChanges, HashSet<string>> ExtractDeclaredIds { get; init; } = null!;

        public Func<GuideManager, int, string, string, string, int, Task<string?>> AppendDriftWarningAsync { get; init; } = null!;

        public Action<ChapterChanges, string, string, string>? AutoPatchAction { get; init; }

        public Func<int, string> GuideFileNameForVolume { get; init; } = null!;
    }
}
