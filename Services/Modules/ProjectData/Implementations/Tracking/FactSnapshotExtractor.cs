namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class FactSnapshotExtractor
    {
        private readonly GuideManager _guideManager;

        private GuideContextService? _guideContextService;
        private GuideContextService GuideContextService => _guideContextService ??= ServiceLocator.Get<GuideContextService>();

        #region 构造函数

        public FactSnapshotExtractor(GuideManager guideManager)
        {
            _guideManager = guideManager;
        }

        #endregion

        #region 常量

        private const string CharacterStateGuideFileName = "character_state_guide.json";
        private const string ConflictProgressGuideFileName = "conflict_progress_guide.json";
        private const string ForeshadowingStatusGuideFileName = "foreshadowing_status_guide.json";

        #endregion

    }
}
