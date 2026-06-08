using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Web.NovelAgentWeb.DTOs;
using TM.Web.NovelAgentWeb.Services;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Controllers;

[ApiController]
[Route("api")]
public class MaterialController : ControllerBase
{
    private readonly NovelAgentWorkspace _workspace;
    private readonly IMaterialAnalysisService _analysisService;

    public MaterialController(NovelAgentWorkspace workspace, IMaterialAnalysisService analysisService)
    {
        _workspace = workspace;
        _analysisService = analysisService;
    }

    [HttpGet("materials")]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await MaterialLibrary.LoadAsync(_workspace, ct));

    [HttpPost("materials")]
    public async Task<IActionResult> Ingest([FromBody] MaterialIngestRequest request, CancellationToken ct) =>
        Ok(await MaterialLibrary.IngestAsync(_workspace, request, ct));

    [HttpPost("materials/upload")]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            throw new InvalidOperationException("未选择文件。");

        await using var stream = file.OpenReadStream();
        var content = await FileParsers.ParseAsync(stream, file.FileName);

        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("文件内容为空。");

        var material = await MaterialLibrary.IngestWithAnalysisAsync(
            _workspace, file.FileName, content, "File", ct);

        // Auto-trigger analysis in background
        _ = Task.Run(async () =>
        {
            try
            {
                var chunkedText = FileParsers.ChunkForAnalysis(content);
                await _analysisService.AnalyzeAsync(chunkedText, file.FileName, material.Id, null, CancellationToken.None);
                await MaterialLibrary.UpdateAnalysisStatusAsync(
                    _workspace, material.Id, true,
                    new List<MaterialAnalysisStageResult>(),
                    0, CancellationToken.None);
            }
            catch { /* background task, swallow errors */ }
        }, ct);

        return Ok(new { materialId = material.Id, fileName = file.FileName, characterCount = content.Length });
    }

    [HttpPost("materials/analyze")]
    public async Task<IActionResult> Analyze([FromBody] MaterialAnalysisRequest request, CancellationToken ct)
    {
        var content = request.Content?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("分析内容为空。");

        var material = await MaterialLibrary.IngestWithAnalysisAsync(
            _workspace, request.FileName, content, request.SourceType, ct);

        var chunkedText = FileParsers.ChunkForAnalysis(content);
        var result = await _analysisService.AnalyzeAsync(chunkedText, request.FileName, material.Id, null, ct);

        await MaterialLibrary.UpdateAnalysisStatusAsync(
            _workspace, material.Id, true, result.Stages, result.TotalEntriesCreated, ct);

        result.Material = material;
        return Ok(result);
    }

    [HttpGet("materials/analyze/{materialId}/stream")]
    public async Task StreamProgress(string materialId, CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        var rawText = await MaterialLibrary.LoadRawTextAsync(_workspace, materialId, ct);
        if (string.IsNullOrWhiteSpace(rawText))
        {
            await WriteSSE(new MaterialAnalysisProgress
            {
                Status = "failed",
                Message = "素材内容为空",
            }, ct);
            return;
        }

        var progress = new Progress<MaterialAnalysisProgress>(async p =>
        {
            try { await WriteSSE(p, ct); }
            catch { /* client disconnected */ }
        });

        var chunkedText = FileParsers.ChunkForAnalysis(rawText);
        var document = await MaterialLibrary.LoadAsync(_workspace, ct);
        var material = document.Materials.FirstOrDefault(m => m.Id == materialId);
        var fileName = material?.FileName ?? "unknown";

        var result = await _analysisService.AnalyzeAsync(chunkedText, fileName, materialId, progress, ct);

        await MaterialLibrary.UpdateAnalysisStatusAsync(
            _workspace, materialId, true, result.Stages, result.TotalEntriesCreated, ct);

        await WriteSSE(new MaterialAnalysisProgress
        {
            Status = "done",
            Message = $"分析完成，共提取 {result.TotalEntriesCreated} 条知识",
        }, ct);
    }

    [HttpDelete("materials/{materialId}")]
    public async Task<IActionResult> Delete(string materialId, CancellationToken ct)
    {
        // Delete associated knowledge entries
        var knowledgeDoc = await _workspace.CreativeKnowledgeBaseService.LoadAsync(ct);
        var entriesToRemove = knowledgeDoc.Entries
            .Where(e => e.Source == $"Material:{materialId}")
            .ToList();

        foreach (var entry in entriesToRemove)
        {
            await _workspace.CreativeKnowledgeBaseService.DeleteEntryAsync(entry.Id, ct);
        }

        var result = await MaterialLibrary.DeleteAsync(_workspace, materialId, ct);
        return Ok(result);
    }

    [HttpPatch("materials/{materialId}")]
    public async Task<IActionResult> Update(string materialId, [FromBody] MaterialUpdateRequest request, CancellationToken ct) =>
        Ok(await MaterialLibrary.UpdateAsync(_workspace, materialId, request, ct));

    [HttpGet("materials/{materialId}/content")]
    public async Task<IActionResult> GetContent(string materialId, CancellationToken ct) =>
        Ok(new { content = await MaterialLibrary.LoadRawTextAsync(_workspace, materialId, ct) });

    [HttpGet("creative-knowledge/entries")]
    public async Task<IActionResult> ListKnowledgeEntries(CancellationToken ct)
    {
        var doc = await _workspace.CreativeKnowledgeBaseService.LoadAsync(ct);
        return Ok(doc.Entries);
    }

    [HttpDelete("creative-knowledge/entries/{entryId}")]
    public async Task<IActionResult> DeleteKnowledgeEntry(string entryId, CancellationToken ct) =>
        Ok(await _workspace.CreativeKnowledgeBaseService.DeleteEntryAsync(entryId, ct));

    [HttpPatch("creative-knowledge/entries/{entryId}")]
    public async Task<IActionResult> UpdateKnowledgeEntry(string entryId, [FromBody] CreativeKnowledgeEntry request, CancellationToken ct) =>
        Ok(await _workspace.CreativeKnowledgeBaseService.UpdateEntryAsync(entryId, request, ct));

    private async Task WriteSSE(MaterialAnalysisProgress data, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        await Response.WriteAsync($"data: {json}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}

public sealed record MaterialAnalysisRequest(
    string FileName = "",
    string Content = "",
    string SourceType = "Text");
