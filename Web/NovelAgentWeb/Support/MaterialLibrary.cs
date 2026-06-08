using System.Text.Json;
using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Support;

public static class MaterialLibrary
{
    public static async Task<MaterialLibraryDocument> LoadAsync(
        NovelAgentWorkspace workspace,
        CancellationToken ct = default)
    {
        var indexPath = GetIndexPath(workspace);
        if (!File.Exists(indexPath))
            return new MaterialLibraryDocument();

        await using var stream = File.OpenRead(indexPath);
        return await JsonSerializer.DeserializeAsync<MaterialLibraryDocument>(stream, cancellationToken: ct)
            ?? new MaterialLibraryDocument();
    }

    public static async Task<MaterialLibraryDocument> IngestAsync(
        NovelAgentWorkspace workspace,
        MaterialIngestRequest request,
        CancellationToken ct = default)
    {
        var content = request.Content?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("素材内容为空。");

        var document = await LoadAsync(workspace, ct);
        var material = new MaterialReference
        {
            FileName = string.IsNullOrWhiteSpace(request.FileName)
                ? $"material-{DateTime.Now:yyyyMMddHHmmss}.txt"
                : Path.GetFileName(request.FileName),
            SourceType = string.IsNullOrWhiteSpace(request.SourceType) ? "Text" : request.SourceType.Trim(),
            CharacterCount = content.Length,
            Summary = BuildSummary(content),
            Tags = BuildTags(content),
            WorkflowReferences = BuildWorkflowReferences(content)
        };

        var materialRoot = GetMaterialRoot(workspace);
        Directory.CreateDirectory(materialRoot);
        await File.WriteAllTextAsync(Path.Combine(materialRoot, $"{material.Id}.txt"), content, ct);

        document.Materials.Insert(0, material);
        await SaveAsync(workspace, document, ct);
        return document;
    }

    public static async Task<MaterialReference> IngestWithAnalysisAsync(
        NovelAgentWorkspace workspace,
        string fileName,
        string content,
        string sourceType,
        CancellationToken ct = default)
    {
        var document = await LoadAsync(workspace, ct);
        var material = new MaterialReference
        {
            FileName = string.IsNullOrWhiteSpace(fileName)
                ? $"material-{DateTime.Now:yyyyMMddHHmmss}.txt"
                : Path.GetFileName(fileName),
            SourceType = string.IsNullOrWhiteSpace(sourceType) ? "Text" : sourceType.Trim(),
            CharacterCount = content.Length,
            Summary = BuildSummary(content),
            Tags = BuildTags(content),
            WorkflowReferences = BuildWorkflowReferences(content)
        };

        var materialRoot = GetMaterialRoot(workspace);
        Directory.CreateDirectory(materialRoot);
        await File.WriteAllTextAsync(Path.Combine(materialRoot, $"{material.Id}.txt"), content, ct);

        document.Materials.Insert(0, material);
        await SaveAsync(workspace, document, ct);
        return material;
    }

    public static async Task UpdateAnalysisStatusAsync(
        NovelAgentWorkspace workspace,
        string materialId,
        bool isAnalyzed,
        List<MaterialAnalysisStageResult> results,
        int entriesCreated,
        CancellationToken ct = default)
    {
        var document = await LoadAsync(workspace, ct);
        var material = document.Materials.FirstOrDefault(m => m.Id == materialId);
        if (material == null) return;

        material.IsAnalyzed = isAnalyzed;
        material.AnalysisResults = results;
        material.KnowledgeEntriesCreated = entriesCreated;
        await SaveAsync(workspace, document, ct);
    }

    public static async Task<MaterialLibraryDocument> DeleteAsync(
        NovelAgentWorkspace workspace,
        string materialId,
        CancellationToken ct = default)
    {
        var document = await LoadAsync(workspace, ct);
        var material = document.Materials.FirstOrDefault(m => m.Id == materialId);
        if (material == null) return document;

        // Delete the raw text file
        var materialRoot = GetMaterialRoot(workspace);
        var filePath = Path.Combine(materialRoot, $"{materialId}.txt");
        if (File.Exists(filePath)) File.Delete(filePath);

        // Delete analysis file if exists
        var analysisPath = Path.Combine(materialRoot, $"{materialId}_analysis.json");
        if (File.Exists(analysisPath)) File.Delete(analysisPath);

        document.Materials.Remove(material);
        await SaveAsync(workspace, document, ct);
        return document;
    }

    public static async Task<MaterialLibraryDocument> UpdateAsync(
        NovelAgentWorkspace workspace,
        string materialId,
        MaterialUpdateRequest request,
        CancellationToken ct = default)
    {
        var document = await LoadAsync(workspace, ct);
        var material = document.Materials.FirstOrDefault(m => m.Id == materialId);
        if (material == null) return document;

        if (!string.IsNullOrWhiteSpace(request.FileName))
            material.FileName = Path.GetFileName(request.FileName.Trim());

        if (!string.IsNullOrWhiteSpace(request.Summary))
            material.Summary = request.Summary.Trim();

        if (!string.IsNullOrWhiteSpace(request.Tags))
        {
            material.Tags = request.Tags
                .Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(request.Content))
        {
            var content = request.Content.Trim();
            await SaveRawTextAsync(workspace, materialId, content, ct);
            material.CharacterCount = content.Length;
            if (string.IsNullOrWhiteSpace(request.Summary))
                material.Summary = BuildSummary(content);
            if (string.IsNullOrWhiteSpace(request.Tags))
                material.Tags = BuildTags(content);
            material.WorkflowReferences = BuildWorkflowReferences(content);
            material.IsAnalyzed = false;
            material.AnalysisResults = new List<MaterialAnalysisStageResult>();
            material.KnowledgeEntriesCreated = 0;
        }

        if (material.Tags.Count == 0)
            material.Tags.Add("待归类");

        await SaveAsync(workspace, document, ct);
        return document;
    }

    public static async Task SaveRawTextAsync(
        NovelAgentWorkspace workspace,
        string materialId,
        string content,
        CancellationToken ct = default)
    {
        var materialRoot = GetMaterialRoot(workspace);
        Directory.CreateDirectory(materialRoot);
        await File.WriteAllTextAsync(Path.Combine(materialRoot, $"{materialId}.txt"), content, ct);
    }

    public static string GetRawTextPath(NovelAgentWorkspace workspace, string materialId) =>
        Path.Combine(GetMaterialRoot(workspace), $"{materialId}.txt");

    public static async Task<string> LoadRawTextAsync(
        NovelAgentWorkspace workspace,
        string materialId,
        CancellationToken ct = default)
    {
        var path = GetRawTextPath(workspace, materialId);
        if (!File.Exists(path)) return string.Empty;
        return await File.ReadAllTextAsync(path, ct);
    }

    private static async Task SaveAsync(
        NovelAgentWorkspace workspace,
        MaterialLibraryDocument document,
        CancellationToken ct)
    {
        var indexPath = GetIndexPath(workspace);
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        await using var stream = File.Create(indexPath);
        await JsonSerializer.SerializeAsync(stream, document, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }, ct);
    }

    private static string GetMaterialRoot(NovelAgentWorkspace workspace) =>
        Path.Combine(workspace.StorageRoot, "Projects", workspace.ProjectName, "Materials");

    private static string GetIndexPath(NovelAgentWorkspace workspace) =>
        Path.Combine(GetMaterialRoot(workspace), "materials_index.json");

    private static string BuildSummary(string content)
    {
        var normalized = content.Replace("\r", " ").Replace("\n", " ").Trim();
        return normalized.Length <= 180 ? normalized : normalized[..180] + "...";
    }

    private static List<string> BuildTags(string content)
    {
        var tags = new List<string>();
        AddTag(tags, content, "爽", "爽文");
        AddTag(tags, content, "悬疑", "悬疑");
        AddTag(tags, content, "烧脑", "烧脑");
        AddTag(tags, content, "世界观", "世界观");
        AddTag(tags, content, "伏笔", "伏笔");
        AddTag(tags, content, "角色", "角色");
        AddTag(tags, content, "情绪", "情绪");
        AddTag(tags, content, "反转", "反转");
        AddTag(tags, content, "套路", "套路风险");
        AddTag(tags, content, "素材", "素材");
        if (tags.Count == 0) tags.Add("待归类");
        return tags;
    }

    private static List<string> BuildWorkflowReferences(string content)
    {
        var references = new List<string>();
        references.Add(content.Contains("题材") || content.Contains("类型")
            ? "故事地基：提取题材、目标读者、风向和禁区。"
            : "故事地基：可作为核心设定、读者承诺或禁区参考。");
        references.Add(content.Contains("卷") || content.Contains("反转") || content.Contains("高潮")
            ? "卷规划：提取阶段反转、高潮、伏笔投放和回收节奏。"
            : "卷规划：可作为卷级冲突升级和节奏参考。");
        references.Add(content.Contains("章节") || content.Contains("桥段") || content.Contains("场景")
            ? "章节工坊：提取桥段模式、相似风险和反套路策略。"
            : "章节工坊：可作为候选剧情、角色选择和代价设计参考。");
        return references;
    }

    private static void AddTag(List<string> tags, string content, string needle, string tag)
    {
        if (content.Contains(needle, StringComparison.OrdinalIgnoreCase)
            && !tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            tags.Add(tag);
    }
}
