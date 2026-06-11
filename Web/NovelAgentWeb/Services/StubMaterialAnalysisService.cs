using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services;

public sealed class StubMaterialAnalysisService : IMaterialAnalysisService
{
    private static readonly AsyncLocal<NovelAgentWorkspace?> _currentWorkspace = new();
    private NovelAgentWorkspace _workspace => _currentWorkspace.Value
        ?? throw new InvalidOperationException("Workspace not set for current request");

    internal static void SetWorkspace(NovelAgentWorkspace workspace) => _currentWorkspace.Value = workspace;
    internal static void ClearWorkspace() => _currentWorkspace.Value = null;

    public StubMaterialAnalysisService() { }

    public async Task<MaterialAnalysisResult> AnalyzeAsync(
        string rawText,
        string fileName,
        string materialId,
        IProgress<MaterialAnalysisProgress>? progress,
        CancellationToken ct)
    {
        var result = new MaterialAnalysisResult
        {
            Success = true,
            MaterialId = materialId,
            FileName = fileName,
        };

        var stubEntries = GetStubEntries(materialId, fileName);

        for (var i = 0; i < AnalysisStages.Stages.Length; i++)
        {
            var (key, label, _) = AnalysisStages.Stages[i];
            var stageEntries = stubEntries.Where(e => GetStageForCategory(e.Category) == key).ToList();

            progress?.Report(new MaterialAnalysisProgress
            {
                Stage = key,
                StageLabel = label,
                StageIndex = i,
                TotalStages = AnalysisStages.Stages.Length,
                Status = "running",
                Message = $"正在分析: {label}...",
            });

            // Simulate analysis delay
            await Task.Delay(500, ct);

            // Write entries to knowledge base
            foreach (var entry in stageEntries)
            {
                await _workspace.CreativeKnowledgeBaseService.AddEntryAsync(entry, ct);
            }

            result.Stages.Add(new MaterialAnalysisStageResult
            {
                Stage = key,
                StageLabel = label,
                EntriesCreated = stageEntries.Count,
                EntryTitles = stageEntries.Select(e => e.Title).ToList(),
            });

            progress?.Report(new MaterialAnalysisProgress
            {
                Stage = key,
                StageLabel = label,
                StageIndex = i,
                TotalStages = AnalysisStages.Stages.Length,
                Status = "completed",
                Message = $"{label}完成，提取 {stageEntries.Count} 条知识",
                Entries = stageEntries,
            });
        }

        result.TotalEntriesCreated = stubEntries.Count;
        return result;
    }

    private static string GetStageForCategory(CreativeKnowledgeCategory category) => category switch
    {
        CreativeKnowledgeCategory.GenrePrinciple or CreativeKnowledgeCategory.ReaderPromise => "genre",
        CreativeKnowledgeCategory.ThemeDepth => "world",
        CreativeKnowledgeCategory.RelationshipDynamic or CreativeKnowledgeCategory.EmotionArc => "character",
        CreativeKnowledgeCategory.TropePattern or CreativeKnowledgeCategory.AntiTropeStrategy => "plot",
        CreativeKnowledgeCategory.ProjectUsedPattern => "auto",
        _ => "auto",
    };

    private static List<CreativeKnowledgeEntry> GetStubEntries(string materialId, string fileName)
    {
        var source = $"Material:{materialId}";
        return new List<CreativeKnowledgeEntry>
        {
            new()
            {
                Id = $"{materialId}-genre-1",
                Category = CreativeKnowledgeCategory.GenrePrinciple,
                Genre = "玄幻",
                Title = $"{fileName}: 升级节奏设计",
                Content = "该作品采用阶梯式升级节奏，每3-5章一个小突破，每卷一个大境界提升，中间穿插战斗和探索作为缓冲。",
                Tags = new List<string> { "升级", "节奏", "玄幻" },
                Weight = 8,
                Source = source,
            },
            new()
            {
                Id = $"{materialId}-genre-2",
                Category = CreativeKnowledgeCategory.ReaderPromise,
                Genre = "玄幻",
                Title = $"{fileName}: 读者期待管理",
                Content = "开篇立下明确的能力成长路线图，让读者对主角未来有清晰预期，同时保留意外转折的空间。",
                Tags = new List<string> { "读者承诺", "成长线" },
                Weight = 7,
                Source = source,
            },
            new()
            {
                Id = $"{materialId}-world-1",
                Category = CreativeKnowledgeCategory.ThemeDepth,
                Genre = "玄幻",
                Title = $"{fileName}: 力量体系代价设计",
                Content = "力量体系的核心规则：每次突破都需要付出对等代价，代价可以延后但不能免除，形成债务累积的压力线。",
                Tags = new List<string> { "力量体系", "代价", "规则" },
                Weight = 9,
                Source = source,
            },
            new()
            {
                Id = $"{materialId}-char-1",
                Category = CreativeKnowledgeCategory.RelationshipDynamic,
                Genre = "玄幻",
                Title = $"{fileName}: 师徒关系张力",
                Content = "导师既是保护者也是潜在的威胁源，当主角的秘密与导师的利益产生冲突时，关系从信任转向博弈。",
                Tags = new List<string> { "师徒", "关系", "张力" },
                Weight = 8,
                Source = source,
            },
            new()
            {
                Id = $"{materialId}-char-2",
                Category = CreativeKnowledgeCategory.EmotionArc,
                Genre = "玄幻",
                Title = $"{fileName}: 胜利的代价情绪",
                Content = "每次胜利后不是纯粹的喜悦，而是伴随对代价的担忧，形成'赢了但亏了'的复合情绪体验。",
                Tags = new List<string> { "情绪", "代价", "复合体验" },
                Weight = 8,
                Source = source,
            },
            new()
            {
                Id = $"{materialId}-plot-1",
                Category = CreativeKnowledgeCategory.TropePattern,
                Genre = "玄幻",
                Title = $"{fileName}: 打脸套路识别",
                Content = "典型的'被嘲笑→展示实力→打脸'三段式结构，重复使用会导致审美疲劳，建议每卷最多使用2次。",
                Tags = new List<string> { "打脸", "套路", "风险" },
                Weight = 6,
                Source = source,
            },
            new()
            {
                Id = $"{materialId}-plot-2",
                Category = CreativeKnowledgeCategory.AntiTropeStrategy,
                Genre = "玄幻",
                Title = $"{fileName}: 反转设计策略",
                Content = "在看似标准的胜利结局前插入代价兑现，让读者预期的'爽点'变成'压力点'，制造意外感。",
                Tags = new List<string> { "反转", "反套路", "代价" },
                Weight = 9,
                Source = source,
            },
            new()
            {
                Id = $"{materialId}-auto-1",
                Category = CreativeKnowledgeCategory.ProjectUsedPattern,
                Genre = "玄幻",
                Title = $"{fileName}: 章末钩子设计",
                Content = "每章结尾留下一个未解决的悬念或新出现的威胁，驱动读者继续阅读。常见模式：新角色登场、秘密被部分揭露、危机预告。",
                Tags = new List<string> { "钩子", "悬念", "节奏" },
                Weight = 7,
                Source = source,
            },
        };
    }
}
