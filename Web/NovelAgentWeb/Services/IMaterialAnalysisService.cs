using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Services;

public interface IMaterialAnalysisService
{
    Task<MaterialAnalysisResult> AnalyzeAsync(
        string rawText,
        string fileName,
        string materialId,
        IProgress<MaterialAnalysisProgress>? progress,
        CancellationToken ct);
}

public static class AnalysisStages
{
    public static readonly (string Key, string Label, CreativeKnowledgeCategory[] Categories)[] Stages =
    {
        ("genre", "题材风格分析", new[] { CreativeKnowledgeCategory.GenrePrinciple, CreativeKnowledgeCategory.ReaderPromise }),
        ("world", "世界观提取", new[] { CreativeKnowledgeCategory.ThemeDepth }),
        ("character", "角色体系拆解", new[] { CreativeKnowledgeCategory.RelationshipDynamic, CreativeKnowledgeCategory.EmotionArc }),
        ("plot", "情节模式识别", new[] { CreativeKnowledgeCategory.TropePattern, CreativeKnowledgeCategory.AntiTropeStrategy }),
        ("auto", "Agent 自动维度", new[] { CreativeKnowledgeCategory.ProjectUsedPattern }),
    };
}
