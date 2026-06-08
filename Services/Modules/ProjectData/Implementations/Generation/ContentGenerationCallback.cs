using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using TM.Services.Modules.ProjectData.Implementations.Guides;

namespace TM.Services.Modules.ProjectData.Implementations
{
    [Obfuscation(Feature = "controlflow", Exclude = true, ApplyToMembers = true)]
    public partial class ContentGenerationCallback
    {
        private readonly GenerationGate _generationGate;
        private readonly GuideManager _guideManager;
        private readonly LedgerTrimService _ledgerTrim;
        private readonly ChapterSummaryStore _summaryStore;
        private readonly ChapterMilestoneStore _milestoneStore;
        private readonly CharacterStateService _characterStateService;
        private readonly ConflictProgressService _conflictProgressService;
        private readonly PlotPointsIndexService _plotPointsIndexService;
        private readonly ForeshadowingStatusService _foreshadowingStatusService;
        private readonly LocationStateService _locationStateService;
        private readonly FactionStateService _factionStateService;
        private readonly TimelineService _timelineService;
        private readonly ItemStateService _itemStateService;
        private readonly SecretRevealService _secretRevealService;
        private readonly PledgeConstraintService _pledgeConstraintService;
        private readonly DeadlineConstraintService _deadlineConstraintService;
        private readonly ChapterChangesWalStore _changesWalStore;
        private readonly ChapterKeyEventStore _keyEventStore;
        private readonly EntityDriftFallbackPatcher _driftFallbackPatcher;

        private sealed record NameMapCacheEntry(Dictionary<string, string> Map, DateTime Expires);
        private volatile NameMapCacheEntry? _nameMapCache;

        public void ClearNameMapCache() => _nameMapCache = null;

        public ContentGenerationCallback(
            GenerationGate generationGate,
            GuideManager guideManager,
            LedgerTrimService ledgerTrimService,
            ChapterSummaryStore summaryStore,
            ChapterMilestoneStore milestoneStore,
            CharacterStateService characterStateService,
            ConflictProgressService conflictProgressService,
            PlotPointsIndexService plotPointsIndexService,
            ForeshadowingStatusService foreshadowingStatusService,
            LocationStateService locationStateService,
            FactionStateService factionStateService,
            TimelineService timelineService,
            ItemStateService itemStateService,
            SecretRevealService secretRevealService,
            PledgeConstraintService pledgeConstraintService,
            DeadlineConstraintService deadlineConstraintService,
            ChapterChangesWalStore changesWalStore,
            ChapterKeyEventStore keyEventStore,
            EntityDriftFallbackPatcher driftFallbackPatcher)
        {
            _generationGate = generationGate;
            _guideManager = guideManager;
            _ledgerTrim = ledgerTrimService;
            _summaryStore = summaryStore;
            _milestoneStore = milestoneStore;
            _characterStateService = characterStateService;
            _conflictProgressService = conflictProgressService;
            _plotPointsIndexService = plotPointsIndexService;
            _foreshadowingStatusService = foreshadowingStatusService;
            _locationStateService = locationStateService;
            _factionStateService = factionStateService;
            _timelineService = timelineService;
            _itemStateService = itemStateService;
            _secretRevealService = secretRevealService;
            _pledgeConstraintService = pledgeConstraintService;
            _deadlineConstraintService = deadlineConstraintService;
            _changesWalStore = changesWalStore;
            _keyEventStore = keyEventStore;
            _driftFallbackPatcher = driftFallbackPatcher;

            StoragePathHelper.CurrentProjectChanged += (_, _) => _nameMapCache = null;
        }
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
    }
}
