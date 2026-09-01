namespace TM.Services.Framework.AI.NovelAgent.Models
{
    public sealed record NovelAgentProductionStageDescriptor(string Id, string Label, int Order);

    public static class NovelAgentProductionStages
    {
        public const string ProjectResolved = "ProjectResolved";
        public const string KnowledgeResolved = "KnowledgeResolved";
        public const string KnowledgeClassified = "KnowledgeClassified";
        public const string StoryDesignBuilt = "StoryDesignBuilt";
        public const string VolumePlanBuilt = "VolumePlanBuilt";
        public const string ChapterBlueprintBuilt = "ChapterBlueprintBuilt";
        public const string PackageBuilt = "PackageBuilt";
        public const string DraftGenerated = "DraftGenerated";
        public const string ChangesExtracted = "ChangesExtracted";
        public const string GateValidated = "GateValidated";
        public const string DraftRewritten = "DraftRewritten";
        public const string ReviewCompleted = "ReviewCompleted";
        public const string ChapterCommitted = "ChapterCommitted";
        public const string FactsPersisted = "FactsPersisted";
        public const string IndexUpdated = "IndexUpdated";
        public const string RunCompleted = "RunCompleted";

        public const string ContextPackage = "context_package";
        public const string DraftGeneration = "draft_generation";
        public const string GateValidation = "gate_validation";
        public const string DraftRepair = "draft_repair";
        public const string QualityReview = "quality_review";
        public const string AwaitUserReview = "await_user_review";
        public const string ChapterCommit = "chapter_commit";
        public const string GateValidationOrRepair = "gate_validation_or_repair";

        public static IReadOnlyList<NovelAgentProductionStageDescriptor> CanonicalStages { get; } =
            new[]
            {
                new NovelAgentProductionStageDescriptor(ProjectResolved, "确认项目", 1),
                new NovelAgentProductionStageDescriptor(KnowledgeResolved, "读取项目知识绑定", 2),
                new NovelAgentProductionStageDescriptor(KnowledgeClassified, "知识语义分类完成", 3),
                new NovelAgentProductionStageDescriptor(StoryDesignBuilt, "故事规则 / Story Bible 构建完成", 4),
                new NovelAgentProductionStageDescriptor(VolumePlanBuilt, "分卷设计完成", 5),
                new NovelAgentProductionStageDescriptor(ChapterBlueprintBuilt, "章节蓝图完成", 6),
                new NovelAgentProductionStageDescriptor(PackageBuilt, "章节生产包构建完成", 7),
                new NovelAgentProductionStageDescriptor(DraftGenerated, "正文初稿生成完成", 8),
                new NovelAgentProductionStageDescriptor(ChangesExtracted, "CHANGES 提取完成", 9),
                new NovelAgentProductionStageDescriptor(GateValidated, "门禁校验完成", 10),
                new NovelAgentProductionStageDescriptor(DraftRewritten, "自动修订完成", 11),
                new NovelAgentProductionStageDescriptor(ReviewCompleted, "质量评审完成", 12),
                new NovelAgentProductionStageDescriptor(ChapterCommitted, "章节提交书城", 13),
                new NovelAgentProductionStageDescriptor(FactsPersisted, "FactSnapshot 回写完成", 14),
                new NovelAgentProductionStageDescriptor(IndexUpdated, "正文和知识索引完成", 15),
                new NovelAgentProductionStageDescriptor(RunCompleted, "本轮生产完成", 16)
            };

        public static string StepToolName(string stageId) => $"NovelAgent.Production.{stageId}";

        public static string ToCanonicalStage(string stageId) => stageId switch
        {
            ContextPackage => PackageBuilt,
            DraftGeneration => DraftGenerated,
            GateValidation => GateValidated,
            DraftRepair => DraftRewritten,
            QualityReview => ReviewCompleted,
            ChapterCommit => ChapterCommitted,
            GateValidationOrRepair => GateValidated,
            "post_commit_facts" => FactsPersisted,
            "post_commit_metadata" => FactsPersisted,
            "index_outbox" => IndexUpdated,
            _ => stageId
        };

        public static int Order(string stageId)
        {
            var canonical = ToCanonicalStage(stageId);
            var stage = CanonicalStages.FirstOrDefault(item =>
                string.Equals(item.Id, canonical, StringComparison.OrdinalIgnoreCase));
            return stage?.Order ?? int.MaxValue;
        }

        public static string Label(string stageId) => stageId switch
        {
            ProjectResolved => "确认项目",
            KnowledgeResolved => "读取项目知识绑定",
            KnowledgeClassified => "知识语义分类完成",
            StoryDesignBuilt => "故事规则 / Story Bible 构建完成",
            VolumePlanBuilt => "分卷设计完成",
            ChapterBlueprintBuilt => "章节蓝图完成",
            PackageBuilt => "章节生产包构建完成",
            DraftGenerated => "正文初稿生成完成",
            ChangesExtracted => "CHANGES 提取完成",
            GateValidated => "门禁校验完成",
            DraftRewritten => "自动修订完成",
            ReviewCompleted => "质量评审完成",
            ChapterCommitted => "章节提交书城",
            FactsPersisted => "FactSnapshot 回写完成",
            IndexUpdated => "正文和知识索引完成",
            RunCompleted => "本轮生产完成",
            ContextPackage => "构建上下文包",
            DraftGeneration => "生成章节正文",
            GateValidation => "硬门禁校验",
            DraftRepair => "自动修复草稿",
            QualityReview => "Agent 质量评审",
            AwaitUserReview => "等待用户审阅",
            ChapterCommit => "提交书城",
            GateValidationOrRepair => "校验/修复章节草稿",
            _ => stageId
        };
    }
}
