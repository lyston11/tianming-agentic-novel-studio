using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using TM.Scripts.Migration.Models;

namespace TM.Scripts.Migration;

/// <summary>
/// Service to migrate file-based vector embeddings to Qdrant
/// </summary>
public class VectorMigrationService
{
    private readonly NovelAgentDbContext _dbContext;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<VectorMigrationService> _logger;
    private readonly string _appDataPath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly int _vectorDimension;
    private readonly int _batchSize;

    public VectorMigrationService(
        NovelAgentDbContext dbContext,
        IVectorStore vectorStore,
        ILogger<VectorMigrationService> logger,
        string appDataPath,
        int vectorDimension = 512,
        int batchSize = 100)
    {
        _dbContext = dbContext;
        _vectorStore = vectorStore;
        _logger = logger;
        _appDataPath = appDataPath;
        _vectorDimension = vectorDimension;
        _batchSize = batchSize;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
    }

    /// <summary>
    /// Execute vector migration with backup and verification
    /// </summary>
    public async Task<VectorMigrationResult> MigrateAsync(bool createBackup = true, bool force = false)
    {
        var result = new VectorMigrationResult();
        var startTime = DateTime.UtcNow;

        try
        {
            _logger.LogInformation("Starting vector migration from JSON files to Qdrant...");

            // Check if legacy vector files exist
            var chapterEmbeddingsPath = Path.Combine(_appDataPath, "Config/guides/chapter_embeddings.json");
            var chunkEmbeddingsPath = Path.Combine(_appDataPath, "Config/guides/chunk_embeddings.json");

            var hasChapterEmbeddings = File.Exists(chapterEmbeddingsPath);
            var hasChunkEmbeddings = File.Exists(chunkEmbeddingsPath);

            if (!hasChapterEmbeddings && !hasChunkEmbeddings)
            {
                _logger.LogWarning("No legacy vector files found at {ChapterPath} or {ChunkPath}",
                    chapterEmbeddingsPath, chunkEmbeddingsPath);
                result.Warnings.Add("No legacy vector embedding files found - migration skipped");
                result.Success = true;
                result.Duration = DateTime.UtcNow - startTime;
                return result;
            }

            // Create backup if requested
            if (createBackup)
            {
                result.BackupPath = await CreateVectorBackupAsync(hasChapterEmbeddings, hasChunkEmbeddings);
            }

            // Load legacy embeddings
            var chapterEmbeddings = hasChapterEmbeddings
                ? await LoadChapterEmbeddingsAsync(chapterEmbeddingsPath)
                : new LegacyChapterEmbeddingsRoot();

            var chunkEmbeddings = hasChunkEmbeddings
                ? await LoadChunkEmbeddingsAsync(chunkEmbeddingsPath)
                : new LegacyChunkEmbeddingsRoot();

            // Validate vector dimensions
            ValidateVectorDimensions(chapterEmbeddings, chunkEmbeddings);

            // Get projects from database
            var projects = await _dbContext.NovelProjects
                .Include(p => p.Chapters)
                .ToListAsync();

            if (projects.Count == 0)
            {
                throw new InvalidOperationException(
                    "No projects found in database. Run Task 1.4 (JSON to SQLite migration) first.");
            }

            // Build chapter ID mapping (legacy ID -> new GUID)
            var chapterMapping = await BuildChapterMappingAsync(projects);

            // Process each project
            foreach (var project in projects)
            {
                var projectStats = await MigrateProjectVectorsAsync(
                    project,
                    chapterEmbeddings,
                    chunkEmbeddings,
                    chapterMapping,
                    force);

                result.ProjectStats[project.Id] = projectStats;
                result.ProjectsProcessed++;
                result.ChapterVectorsImported += projectStats.ChapterVectors;
                result.ChunkVectorsImported += projectStats.ChunkVectors;
                result.TotalVectorsImported += projectStats.TotalVectors;

                if (projectStats.CollectionCreated)
                {
                    result.CollectionsCreated++;
                }
            }

            // Verify migration
            await VerifyMigrationAsync(result);

            result.Success = true;
            result.Duration = DateTime.UtcNow - startTime;

            _logger.LogInformation("Vector migration completed successfully in {Duration}ms",
                result.Duration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Duration = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "Vector migration failed: {Message}", ex.Message);
        }

        return result;
    }

    private async Task<LegacyChapterEmbeddingsRoot> LoadChapterEmbeddingsAsync(string filePath)
    {
        _logger.LogInformation("Loading chapter embeddings from {Path}", filePath);

        var jsonContent = await File.ReadAllTextAsync(filePath);
        var embeddings = JsonSerializer.Deserialize<LegacyChapterEmbeddingsRoot>(jsonContent, _jsonOptions);

        if (embeddings == null || embeddings.Embeddings == null)
        {
            throw new InvalidOperationException($"Failed to parse chapter embeddings from {filePath}");
        }

        _logger.LogInformation("Loaded {Count} chapter embeddings", embeddings.Embeddings.Count);
        return embeddings;
    }

    private async Task<LegacyChunkEmbeddingsRoot> LoadChunkEmbeddingsAsync(string filePath)
    {
        _logger.LogInformation("Loading chunk embeddings from {Path}", filePath);

        var jsonContent = await File.ReadAllTextAsync(filePath);
        var embeddings = JsonSerializer.Deserialize<LegacyChunkEmbeddingsRoot>(jsonContent, _jsonOptions);

        if (embeddings == null || embeddings.Embeddings == null)
        {
            throw new InvalidOperationException($"Failed to parse chunk embeddings from {filePath}");
        }

        _logger.LogInformation("Loaded {Count} chunk embeddings", embeddings.Embeddings.Count);
        return embeddings;
    }

    private void ValidateVectorDimensions(
        LegacyChapterEmbeddingsRoot chapterEmbeddings,
        LegacyChunkEmbeddingsRoot chunkEmbeddings)
    {
        if (chapterEmbeddings.Dimension > 0 && chapterEmbeddings.Dimension != _vectorDimension)
        {
            throw new InvalidOperationException(
                $"Chapter embeddings dimension {chapterEmbeddings.Dimension} does not match expected {_vectorDimension}");
        }

        if (chunkEmbeddings.Dimension > 0 && chunkEmbeddings.Dimension != _vectorDimension)
        {
            throw new InvalidOperationException(
                $"Chunk embeddings dimension {chunkEmbeddings.Dimension} does not match expected {_vectorDimension}");
        }
    }

    private Task<Dictionary<string, string>> BuildChapterMappingAsync(
        List<Web.NovelAgentWeb.Data.Entities.NovelProject> projects)
    {
        var mapping = new Dictionary<string, string>();

        foreach (var project in projects)
        {
            foreach (var chapter in project.Chapters)
            {
                // Map by chapter number as legacy ID
                var legacyId = $"chapter_{chapter.ChapterNumber}";
                mapping[legacyId] = chapter.Id;

                // Also map by storage path if available
                if (!string.IsNullOrEmpty(chapter.ContentPath))
                {
                    var fileName = Path.GetFileNameWithoutExtension(chapter.ContentPath);
                    mapping[fileName] = chapter.Id;
                }
            }
        }

        _logger.LogInformation("Built chapter mapping with {Count} entries", mapping.Count);
        return Task.FromResult(mapping);
    }

    private async Task<ProjectVectorStats> MigrateProjectVectorsAsync(
        Web.NovelAgentWeb.Data.Entities.NovelProject project,
        LegacyChapterEmbeddingsRoot chapterEmbeddings,
        LegacyChunkEmbeddingsRoot chunkEmbeddings,
        Dictionary<string, string> chapterMapping,
        bool force)
    {
        var stats = new ProjectVectorStats
        {
            ProjectId = project.Id,
            ProjectTitle = project.Title
        };

        _logger.LogInformation("Migrating vectors for project {ProjectId}: {Title}", project.Id, project.Title);

        // Check if collection already exists
        var collectionExists = await _vectorStore.CollectionExistsAsync(project.Id);
        if (collectionExists && !force)
        {
            _logger.LogWarning("Collection already exists for project {ProjectId}, skipping (use --force to override)", project.Id);
            return stats;
        }

        // Initialize collection
        await _vectorStore.InitializeProjectCollectionAsync(project.Id);
        stats.CollectionCreated = true;
        _logger.LogInformation("Collection created for project {ProjectId}", project.Id);

        // Filter embeddings for this project
        var projectChapterEmbeddings = chapterEmbeddings.Embeddings
            .Where(e => e.ProjectId == project.StorageProjectName || string.IsNullOrEmpty(e.ProjectId))
            .ToList();

        var projectChunkEmbeddings = chunkEmbeddings.Embeddings
            .Where(e => e.ProjectId == project.StorageProjectName || string.IsNullOrEmpty(e.ProjectId))
            .ToList();

        // Import chapter vectors
        if (projectChapterEmbeddings.Count > 0)
        {
            stats.ChapterVectors = await ImportChapterVectorsAsync(
                project,
                projectChapterEmbeddings,
                chapterMapping);
        }

        // Import chunk vectors
        if (projectChunkEmbeddings.Count > 0)
        {
            stats.ChunkVectors = await ImportChunkVectorsAsync(
                project,
                projectChunkEmbeddings,
                chapterMapping);
        }

        stats.TotalVectors = stats.ChapterVectors + stats.ChunkVectors;

        _logger.LogInformation("Imported {Total} vectors for project {ProjectId} ({Chapters} chapters, {Chunks} chunks)",
            stats.TotalVectors, project.Id, stats.ChapterVectors, stats.ChunkVectors);

        return stats;
    }

    private async Task<int> ImportChapterVectorsAsync(
        Web.NovelAgentWeb.Data.Entities.NovelProject project,
        List<LegacyChapterEmbedding> embeddings,
        Dictionary<string, string> chapterMapping)
    {
        var vectorDataList = new List<VectorData>();

        foreach (var embedding in embeddings)
        {
            // Validate vector
            if (embedding.Embedding == null || embedding.Embedding.Length != _vectorDimension)
            {
                _logger.LogWarning("Skipping chapter {ChapterId} - invalid vector dimension", embedding.ChapterId);
                continue;
            }

            // Try to find mapped chapter ID
            string? newChapterId = null;
            if (chapterMapping.TryGetValue(embedding.ChapterId, out var mappedId))
            {
                newChapterId = mappedId;
            }
            else if (chapterMapping.TryGetValue($"chapter_{embedding.ChapterNumber}", out var mappedByNumber))
            {
                newChapterId = mappedByNumber;
            }

            // If no mapping found, generate new ID (orphaned vector)
            if (string.IsNullOrEmpty(newChapterId))
            {
                newChapterId = Guid.NewGuid().ToString();
                _logger.LogWarning("No chapter mapping found for {ChapterId}, using new GUID", embedding.ChapterId);
            }

            var vectorData = new VectorData
            {
                Id = newChapterId,
                Vector = embedding.Embedding,
                UserId = project.UserId,
                ProjectId = project.Id,
                SourceType = "chapter",
                SourceId = newChapterId,
                ChapterId = newChapterId,
                Content = embedding.Content ?? embedding.Title ?? "",
                Metadata = new Dictionary<string, object>
                {
                    ["legacy_id"] = embedding.ChapterId,
                    ["chapter_number"] = embedding.ChapterNumber ?? 0,
                    ["title"] = embedding.Title ?? "",
                    ["migrated_at"] = DateTime.UtcNow.ToString("O")
                }
            };

            vectorDataList.Add(vectorData);
        }

        if (vectorDataList.Count > 0)
        {
            await _vectorStore.UpsertVectorsAsync(project.Id, vectorDataList);
        }

        return vectorDataList.Count;
    }

    private async Task<int> ImportChunkVectorsAsync(
        Web.NovelAgentWeb.Data.Entities.NovelProject project,
        List<LegacyChunkEmbedding> embeddings,
        Dictionary<string, string> chapterMapping)
    {
        var vectorDataList = new List<VectorData>();

        foreach (var embedding in embeddings)
        {
            // Validate vector
            if (embedding.Embedding == null || embedding.Embedding.Length != _vectorDimension)
            {
                _logger.LogWarning("Skipping chunk {ChunkId} - invalid vector dimension", embedding.ChunkId);
                continue;
            }

            // Try to find mapped chapter ID
            string? newChapterId = null;
            if (!string.IsNullOrEmpty(embedding.ChapterId) &&
                chapterMapping.TryGetValue(embedding.ChapterId, out var mappedId))
            {
                newChapterId = mappedId;
            }

            var chunkVectorId = Guid.NewGuid().ToString();

            var vectorData = new VectorData
            {
                Id = chunkVectorId,
                Vector = embedding.Embedding,
                UserId = project.UserId,
                ProjectId = project.Id,
                SourceType = "chunk",
                SourceId = chunkVectorId,
                ChapterId = newChapterId,
                ChunkIndex = embedding.ChunkIndex,
                Content = embedding.Content ?? "",
                Metadata = new Dictionary<string, object>
                {
                    ["legacy_chunk_id"] = embedding.ChunkId,
                    ["legacy_chapter_id"] = embedding.ChapterId ?? "",
                    ["chunk_index"] = embedding.ChunkIndex,
                    ["migrated_at"] = DateTime.UtcNow.ToString("O")
                }
            };

            vectorDataList.Add(vectorData);
        }

        if (vectorDataList.Count > 0)
        {
            await _vectorStore.UpsertVectorsAsync(project.Id, vectorDataList);
        }

        return vectorDataList.Count;
    }

    private async Task VerifyMigrationAsync(VectorMigrationResult result)
    {
        _logger.LogInformation("Verifying migration...");

        foreach (var (projectId, stats) in result.ProjectStats)
        {
            var collectionInfo = await _vectorStore.GetCollectionInfoAsync(projectId);

            if (collectionInfo == null)
            {
                result.Warnings.Add($"Project {projectId}: Collection not found after migration");
                stats.VerificationPassed = false;
                continue;
            }

            if (collectionInfo.VectorCount != stats.TotalVectors)
            {
                result.Warnings.Add(
                    $"Project {projectId}: Vector count mismatch - Expected {stats.TotalVectors}, got {collectionInfo.VectorCount}");
                stats.VerificationPassed = false;
            }
            else
            {
                stats.VerificationPassed = true;
                _logger.LogInformation("Project {ProjectId}: Verification passed - {Count} vectors",
                    projectId, collectionInfo.VectorCount);
            }
        }
    }

    private async Task<string> CreateVectorBackupAsync(bool hasChapterEmbeddings, bool hasChunkEmbeddings)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupDir = Path.Combine(_appDataPath, $"Backup/{timestamp}/VectorIndexes");

        _logger.LogInformation("Creating vector backup at: {BackupDir}", backupDir);
        Directory.CreateDirectory(backupDir);

        if (hasChapterEmbeddings)
        {
            var source = Path.Combine(_appDataPath, "Config/guides/chapter_embeddings.json");
            var dest = Path.Combine(backupDir, "chapter_embeddings.json");
            File.Copy(source, dest, true);
            _logger.LogInformation("Backed up chapter_embeddings.json");
        }

        if (hasChunkEmbeddings)
        {
            var source = Path.Combine(_appDataPath, "Config/guides/chunk_embeddings.json");
            var dest = Path.Combine(backupDir, "chunk_embeddings.json");
            File.Copy(source, dest, true);
            _logger.LogInformation("Backed up chunk_embeddings.json");
        }

        // Create a backup metadata file
        var metadata = new
        {
            timestamp = DateTime.UtcNow,
            has_chapter_embeddings = hasChapterEmbeddings,
            has_chunk_embeddings = hasChunkEmbeddings,
            vector_dimension = _vectorDimension,
            backup_reason = "Pre-Qdrant migration backup (30-day retention)"
        };

        var metadataPath = Path.Combine(backupDir, "backup_metadata.json");
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = true
        }));

        _logger.LogInformation("Vector backup created successfully");
        return backupDir;
    }

    /// <summary>
    /// Test similarity search after migration
    /// </summary>
    public async Task<bool> TestSimilaritySearchAsync(string projectId, float[] queryVector)
    {
        try
        {
            _logger.LogInformation("Testing similarity search for project {ProjectId}", projectId);

            var results = await _vectorStore.SearchSimilarAsync(
                projectId: projectId,
                queryVector: queryVector,
                topK: 5);

            _logger.LogInformation("Similarity search returned {Count} results", results.Count);

            foreach (var result in results)
            {
                var contentPreview = result.Content != null && result.Content.Length > 0
                    ? result.Content.Substring(0, Math.Min(50, result.Content.Length))
                    : "";
                _logger.LogInformation("  - Score: {Score:F4}, SourceType: {SourceType}, Content: {Content}",
                    result.Score, result.SourceType, contentPreview);
            }

            return results.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Similarity search test failed");
            return false;
        }
    }
}
