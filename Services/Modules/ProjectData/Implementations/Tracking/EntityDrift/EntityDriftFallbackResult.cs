using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public sealed class EntityDriftFallbackResult
    {
        public int AutoPatchedCount { get; set; }

        public int WarnOnlyCount { get; set; }

        public HashSet<string> DirtyGuideFiles { get; } = new();

        public List<string> Details { get; } = new();

        public bool PatchedChanges => AutoPatchedCount > 0;

        public bool HasAnyDrift => AutoPatchedCount > 0 || WarnOnlyCount > 0;
    }
}
