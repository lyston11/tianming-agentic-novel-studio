using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;
using TM.Scripts.Migration.Models;

namespace TM.Scripts.Migration;

/// <summary>
/// Service to migrate existing JSON data to SQLite database
/// </summary>
public class DataMigrationService
{
    private readonly NovelAgentDbContext _dbContext;
    private readonly ILogger<DataMigrationService> _logger;
    private readonly string _appDataPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public DataMigrationService(
        NovelAgentDbContext dbContext,
        ILogger<DataMigrationService> logger,
        string appDataPath)
    {
        _dbContext = dbContext;
        _logger = logger;
        _appDataPath = appDataPath;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
    }

    /// <summary>
    /// Execute full migration with transaction protection and backup
    /// </summary>
    public async Task<MigrationResult> MigrateAsync(bool createBackup = true, bool force = false)
    {
        var result = new MigrationResult();
        var startTime = DateTime.UtcNow;

        try
        {
            _logger.LogInformation("Starting data migration from JSON to SQLite...");

            // Ensure database is created with schema
            _logger.LogInformation("Ensuring database schema exists...");
            await _dbContext.Database.EnsureCreatedAsync();
            _logger.LogInformation("Database schema ready.");

            // Check if data already exists
            if (!force && await _dbContext.Users.AnyAsync())
            {
                throw new InvalidOperationException(
                    "Database already contains data. Use --force flag to override.");
            }

            // Create backup if requested
            if (createBackup)
            {
                await CreateBackupAsync();
            }

            // Begin transaction
            await using var transaction = await _dbContext.Database.BeginTransactionAsync();

            try
            {
                // Step 1: Create default admin user
                var adminUser = await CreateAdminUserAsync();
                result.UsersCreated++;

                // Step 2: Migrate projects
                var projectMapping = await MigrateProjectsAsync(adminUser.Id);
                result.ProjectsCreated = projectMapping.Count;

                // Step 3: Migrate story bible data for each project
                foreach (var (legacyId, newProjectId) in projectMapping)
                {
                    var storyBibleResult = await MigrateStoryBibleAsync(legacyId, newProjectId);
                    result.CharactersCreated += storyBibleResult.Characters;
                    result.ForeshadowsCreated += storyBibleResult.Foreshadows;
                    result.WorldSettingsCreated += storyBibleResult.WorldSettings;
                    result.VolumesCreated += storyBibleResult.Volumes;
                }

                // Step 4: Migrate chapters
                foreach (var (legacyId, newProjectId) in projectMapping)
                {
                    var chaptersCreated = await MigrateChaptersAsync(legacyId, newProjectId);
                    result.ChaptersCreated += chaptersCreated;
                }

                // Step 5: Migrate user settings
                await MigrateUserSettingsAsync(adminUser.Id);
                result.UserSettingsMigrated = true;

                // Step 6: Migrate agent memories
                foreach (var (legacyId, newProjectId) in projectMapping)
                {
                    var memoriesCreated = await MigrateAgentMemoriesAsync(legacyId, newProjectId, adminUser.Id);
                    result.AgentMemoriesCreated += memoriesCreated;
                }

                // Step 7: Migrate agent sessions
                foreach (var (legacyId, newProjectId) in projectMapping)
                {
                    var sessionsCreated = await MigrateAgentSessionsAsync(legacyId, newProjectId, adminUser.Id);
                    result.AgentSessionsCreated += sessionsCreated;
                }

                await transaction.CommitAsync();
                result.Success = true;
                result.Duration = DateTime.UtcNow - startTime;

                _logger.LogInformation("Migration completed successfully in {Duration}ms",
                    result.Duration.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Migration failed, transaction rolled back");
                throw;
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.Duration = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "Migration failed: {Message}", ex.Message);
        }

        return result;
    }

    private async Task<User> CreateAdminUserAsync()
    {
        _logger.LogInformation("Creating default admin user...");

        var adminUser = new User
        {
            Id = Guid.NewGuid().ToString(),
            Username = "admin",
            Email = "admin@localhost",
            // BCrypt hash of "admin123" - user should change on first login
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
            Role = "Admin",
            StorageQuotaMb = 10240, // 10GB for admin
            ApiCallQuota = 100000,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        await _dbContext.Users.AddAsync(adminUser);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Admin user created with ID: {UserId}", adminUser.Id);
        return adminUser;
    }

    private async Task<Dictionary<string, string>> MigrateProjectsAsync(string userId)
    {
        _logger.LogInformation("Migrating projects...");

        var projectsJsonPath = Path.Combine(_appDataPath, "Projects/AgenticNovelStudio/NovelProjects/projects.json");
        if (!File.Exists(projectsJsonPath))
        {
            _logger.LogWarning("Projects file not found: {Path}", projectsJsonPath);
            return new Dictionary<string, string>();
        }

        var jsonContent = await File.ReadAllTextAsync(projectsJsonPath);
        var legacyProjects = JsonSerializer.Deserialize<LegacyProjectsRoot>(jsonContent, _jsonOptions);

        if (legacyProjects?.Projects == null || legacyProjects.Projects.Count == 0)
        {
            _logger.LogWarning("No projects found in projects.json");
            return new Dictionary<string, string>();
        }

        var projectMapping = new Dictionary<string, string>();

        foreach (var legacyProject in legacyProjects.Projects)
        {
            var newProjectId = Guid.NewGuid().ToString();
            var project = new NovelProject
            {
                Id = newProjectId,
                UserId = userId,
                Title = legacyProject.Title,
                Genre = legacyProject.Genre,
                SubGenre = legacyProject.SubGenre,
                CoreHook = legacyProject.CoreHook,
                Status = MapProjectStatus(legacyProject.Status),
                StorageProjectName = legacyProject.StorageProjectName,
                WordCount = 0,
                CreatedAt = legacyProject.CreatedAt ?? DateTime.UtcNow,
                UpdatedAt = legacyProject.UpdatedAt ?? DateTime.UtcNow
            };

            await _dbContext.NovelProjects.AddAsync(project);
            projectMapping[legacyProject.Id] = newProjectId;

            _logger.LogInformation("Mapped project {LegacyId} -> {NewId}: {Title}",
                legacyProject.Id, newProjectId, project.Title);
        }

        await _dbContext.SaveChangesAsync();
        return projectMapping;
    }

    private async Task<StoryBibleMigrationResult> MigrateStoryBibleAsync(string legacyProjectId, string newProjectId)
    {
        _logger.LogInformation("Migrating story bible for project {ProjectId}...", newProjectId);

        var result = new StoryBibleMigrationResult();
        var project = await _dbContext.NovelProjects.FindAsync(newProjectId);
        if (project == null)
        {
            _logger.LogWarning("Project not found: {ProjectId}", newProjectId);
            return result;
        }

        var storyBiblePath = GetStoryBiblePath(project.StorageProjectName);
        if (!File.Exists(storyBiblePath))
        {
            _logger.LogWarning("Story bible not found: {Path}", storyBiblePath);
            return result;
        }

        var jsonContent = await File.ReadAllTextAsync(storyBiblePath);
        var storyBible = JsonSerializer.Deserialize<LegacyStoryBible>(jsonContent, _jsonOptions);

        if (storyBible == null)
        {
            _logger.LogWarning("Failed to parse story bible for project {ProjectId}", newProjectId);
            return result;
        }

        // Migrate volumes from volumeArcs
        if (storyBible.VolumeArcs != null)
        {
            foreach (var volumeArc in storyBible.VolumeArcs)
            {
                if (string.IsNullOrEmpty(volumeArc.Title)) continue;

                var volume = new Volume
                {
                    Id = Guid.NewGuid().ToString(),
                    ProjectId = newProjectId,
                    Title = volumeArc.Title,
                    VolumeNumber = ExtractVolumeNumber(volumeArc.VolumeId) ?? 0,
                    Summary = volumeArc.VolumePromise
                };

                await _dbContext.Volumes.AddAsync(volume);
                result.Volumes++;
            }
        }

        // Migrate characters
        if (storyBible.Characters != null)
        {
            foreach (var legacyChar in storyBible.Characters)
            {
                var character = new Character
                {
                    Id = Guid.NewGuid().ToString(),
                    UserId = project.UserId,
                    ProjectId = newProjectId,
                    Name = legacyChar.Name,
                    Role = legacyChar.Role ?? "supporting",
                    Alias = legacyChar.Identity,
                    Personality = legacyChar.Personality,
                    Background = legacyChar.Description,
                    Status = "active",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _dbContext.Characters.AddAsync(character);
                result.Characters++;
            }
        }

        // Migrate foreshadows
        if (storyBible.Foreshadows != null)
        {
            foreach (var legacyForeshadow in storyBible.Foreshadows)
            {
                var foreshadow = new Foreshadow
                {
                    Id = Guid.NewGuid().ToString(),
                    ProjectId = newProjectId,
                    Name = legacyForeshadow.Name,
                    Type = legacyForeshadow.Type,
                    Status = legacyForeshadow.Status ?? "planned",
                    Importance = ParseImportance(legacyForeshadow.Importance),
                    Description = legacyForeshadow.Description,
                    CreatedAt = DateTime.UtcNow
                };

                await _dbContext.Foreshadows.AddAsync(foreshadow);
                result.Foreshadows++;
            }
        }

        // Migrate world settings
        if (storyBible.WorldSettings != null)
        {
            foreach (var legacyWorldSetting in storyBible.WorldSettings)
            {
                var worldSetting = new WorldSettingEntry
                {
                    Id = Guid.NewGuid().ToString(),
                    UserId = project.UserId,
                    ProjectId = newProjectId,
                    Category = legacyWorldSetting.Category ?? "other",
                    Title = legacyWorldSetting.Name ?? "未命名设定",
                    Content = legacyWorldSetting.Description ?? string.Empty,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _dbContext.WorldSettingEntries.AddAsync(worldSetting);
                result.WorldSettings++;
            }
        }

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Story bible migrated: {Characters} characters, {Foreshadows} foreshadows, {WorldSettings} world settings, {Volumes} volumes",
            result.Characters, result.Foreshadows, result.WorldSettings, result.Volumes);

        return result;
    }

    private async Task<int> MigrateChaptersAsync(string legacyProjectId, string newProjectId)
    {
        _logger.LogInformation("Migrating chapters for project {ProjectId}...", newProjectId);

        var project = await _dbContext.NovelProjects.FindAsync(newProjectId);
        if (project == null || string.IsNullOrEmpty(project.StorageProjectName))
        {
            _logger.LogWarning("Project not found or has no storage name: {ProjectId}", newProjectId);
            return 0;
        }

        var chaptersDir = Path.Combine(_appDataPath, $"Projects/{project.StorageProjectName}/Chapters");
        if (!Directory.Exists(chaptersDir))
        {
            _logger.LogWarning("Chapters directory not found: {Path}", chaptersDir);
            return 0;
        }

        var chapterFiles = Directory.GetFiles(chaptersDir, "*.md");
        var chaptersCreated = 0;

        foreach (var chapterFile in chapterFiles)
        {
            var fileName = Path.GetFileName(chapterFile);
            var chapterNumber = ExtractChapterNumber(fileName);

            var chapter = new Chapter
            {
                Id = Guid.NewGuid().ToString(),
                ProjectId = newProjectId,
                Title = $"Chapter {chapterNumber}",
                ChapterNumber = chapterNumber,
                Status = "draft",
                ContentPath = $"Chapters/{fileName}",
                WordCount = await CalculateWordCountAsync(chapterFile),
                CreatedAt = File.GetCreationTimeUtc(chapterFile),
                UpdatedAt = File.GetLastWriteTimeUtc(chapterFile)
            };

            await _dbContext.Chapters.AddAsync(chapter);
            chaptersCreated++;
        }

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Migrated {Count} chapters for project {ProjectId}", chaptersCreated, newProjectId);

        return chaptersCreated;
    }

    private async Task MigrateUserSettingsAsync(string userId)
    {
        _logger.LogInformation("Migrating user settings...");

        var settingsPath = Path.Combine(_appDataPath, "Projects/AgenticNovelStudio/Settings/user_settings.json");
        if (!File.Exists(settingsPath))
        {
            _logger.LogWarning("User settings file not found: {Path}", settingsPath);
            return;
        }

        var jsonContent = await File.ReadAllTextAsync(settingsPath);
        var legacySettings = JsonSerializer.Deserialize<LegacyUserSettings>(jsonContent, _jsonOptions);

        if (legacySettings == null)
        {
            _logger.LogWarning("Failed to parse user settings");
            return;
        }

        // Note: API key should be encrypted, here we'll store it as-is for now
        // TODO: Implement proper encryption in Task 2.1
        var userSettings = new UserSettings
        {
            UserId = userId,
            LlmProvider = legacySettings.LlmProvider,
            LlmApiKeyEncrypted = legacySettings.LlmApiKey, // Should be encrypted
            LlmBaseUrl = legacySettings.LlmBaseUrl,
            LlmModel = legacySettings.LlmModel,
            LlmTemperature = legacySettings.LlmTemperature ?? 0.7f,
            LlmMaxTokens = legacySettings.LlmMaxTokens ?? 4096,
            EmbeddingProvider = legacySettings.EmbeddingProvider ?? "local",
            EmbeddingModel = legacySettings.EmbeddingModel ?? "bge-small-zh-v1.5",
            AgentDefaultRisk = legacySettings.AgentDefaultRisk ?? "Medium",
            AgentAutoContinue = legacySettings.AgentAutoContinue ?? true,
            AgentMaxAutoSteps = legacySettings.AgentMaxAutoSteps ?? 12,
            DefaultGenre = legacySettings.DefaultGenre ?? "玄幻",
            DefaultChapterWordCount = legacySettings.DefaultChapterWordCount ?? 3000,
            Theme = legacySettings.Theme ?? "dark",
            Language = legacySettings.Language ?? "zh-CN"
        };

        await _dbContext.UserSettings.AddAsync(userSettings);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("User settings migrated for user {UserId}", userId);
    }

    private async Task<int> MigrateAgentMemoriesAsync(string legacyProjectId, string newProjectId, string userId)
    {
        _logger.LogInformation("Migrating agent memories for project {ProjectId}...", newProjectId);

        var project = await _dbContext.NovelProjects.FindAsync(newProjectId);
        if (project == null || string.IsNullOrEmpty(project.StorageProjectName))
        {
            return 0;
        }

        var agentDir = Path.Combine(_appDataPath, $"Projects/{project.StorageProjectName}/Agent");
        if (!Directory.Exists(agentDir))
        {
            _logger.LogWarning("Agent directory not found: {Path}", agentDir);
            return 0;
        }

        var memoryFiles = new[] { "project_memory.json", "execution_memory.json", "author_memory.json" };
        var memoriesCreated = 0;

        foreach (var memoryFile in memoryFiles)
        {
            var memoryPath = Path.Combine(agentDir, memoryFile);
            if (!File.Exists(memoryPath)) continue;

            var memoryType = Path.GetFileNameWithoutExtension(memoryFile);
            var jsonContent = await File.ReadAllTextAsync(memoryPath);

            var agentMemory = new AgentMemory
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                ProjectId = newProjectId,
                MemoryType = memoryType,
                Content = jsonContent,
                UpdatedAt = File.GetLastWriteTimeUtc(memoryPath)
            };

            await _dbContext.AgentMemories.AddAsync(agentMemory);
            memoriesCreated++;
        }

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Migrated {Count} agent memories for project {ProjectId}", memoriesCreated, newProjectId);

        return memoriesCreated;
    }

    private async Task<int> MigrateAgentSessionsAsync(string legacyProjectId, string newProjectId, string userId)
    {
        _logger.LogInformation("Migrating agent sessions for project {ProjectId}...", newProjectId);

        var project = await _dbContext.NovelProjects.FindAsync(newProjectId);
        if (project == null || string.IsNullOrEmpty(project.StorageProjectName))
        {
            return 0;
        }

        var sessionsPath = Path.Combine(_appDataPath, $"Projects/{project.StorageProjectName}/Agent/sessions.json");
        if (!File.Exists(sessionsPath))
        {
            // Try the global sessions file
            sessionsPath = Path.Combine(_appDataPath, "Projects/AgenticNovelStudio/Agent/sessions.json");
            if (!File.Exists(sessionsPath))
            {
                _logger.LogWarning("Sessions file not found for project {ProjectId}", newProjectId);
                return 0;
            }
        }

        var jsonContent = await File.ReadAllTextAsync(sessionsPath);
        var legacySessions = JsonSerializer.Deserialize<List<LegacySession>>(jsonContent, _jsonOptions);

        if (legacySessions == null || legacySessions.Count == 0)
        {
            return 0;
        }

        var sessionsCreated = 0;
        foreach (var legacySession in legacySessions)
        {
            // Only migrate sessions for this project
            if (legacySession.ActiveProjectId != legacyProjectId && legacySession.ActiveProjectId != "default")
            {
                continue;
            }

            var agentSession = new AgentSession
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                ProjectId = newProjectId,
                SessionData = JsonSerializer.Serialize(legacySession, _jsonOptions),
                CreatedAt = DateTime.UtcNow
            };

            await _dbContext.AgentSessions.AddAsync(agentSession);
            sessionsCreated++;
        }

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Migrated {Count} agent sessions for project {ProjectId}", sessionsCreated, newProjectId);

        return sessionsCreated;
    }

    private async Task CreateBackupAsync()
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupDir = Path.Combine(_appDataPath, $"Backup/{timestamp}");

        _logger.LogInformation("Creating backup at: {BackupDir}", backupDir);
        Directory.CreateDirectory(backupDir);

        var sourceDir = Path.Combine(_appDataPath, "Projects");
        await CopyDirectoryAsync(sourceDir, Path.Combine(backupDir, "Projects"));

        _logger.LogInformation("Backup created successfully");
    }

    private async Task CopyDirectoryAsync(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir))
        {
            return;
        }

        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var fileName = Path.GetFileName(file);
            var destFile = Path.Combine(destDir, fileName);
            File.Copy(file, destFile, true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var dirName = Path.GetFileName(dir);
            await CopyDirectoryAsync(dir, Path.Combine(destDir, dirName));
        }
    }

    private string GetStoryBiblePath(string? storageProjectName)
    {
        if (string.IsNullOrEmpty(storageProjectName))
        {
            return Path.Combine(_appDataPath, "Projects/AgenticNovelStudio/Services/Framework/AI/NovelAgent/story_bible.json");
        }

        return Path.Combine(_appDataPath, $"Projects/{storageProjectName}/Services/Framework/AI/NovelAgent/story_bible.json");
    }

    private static string MapProjectStatus(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
            "planning" => "planning",
            "drafting" => "drafting",
            "writing" => "writing",
            "editing" => "editing",
            "published" => "published",
            _ => "draft"
        };
    }

    private static int ExtractChapterNumber(string fileName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(fileName, @"(\d+)");
        return match.Success ? int.Parse(match.Value) : 0;
    }

    private static int? ExtractVolumeNumber(string? volumeId)
    {
        if (string.IsNullOrEmpty(volumeId)) return null;
        var match = System.Text.RegularExpressions.Regex.Match(volumeId, @"(\d+)");
        return match.Success ? int.Parse(match.Value) : null;
    }

    private static int? ParseImportance(string? importance)
    {
        if (string.IsNullOrEmpty(importance)) return null;

        // Try direct integer parse
        if (int.TryParse(importance, out var intValue))
        {
            return intValue;
        }

        // Map text values to numbers
        return importance.ToLowerInvariant() switch
        {
            "low" => 1,
            "medium" => 2,
            "high" => 3,
            "critical" => 4,
            _ => null
        };
    }

    private static async Task<int> CalculateWordCountAsync(string filePath)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath);
            // Simple word count - split by whitespace
            var words = content.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return words.Length;
        }
        catch
        {
            return 0;
        }
    }
}

public class MigrationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan Duration { get; set; }
    public int UsersCreated { get; set; }
    public int ProjectsCreated { get; set; }
    public int VolumesCreated { get; set; }
    public int ChaptersCreated { get; set; }
    public int CharactersCreated { get; set; }
    public int ForeshadowsCreated { get; set; }
    public int WorldSettingsCreated { get; set; }
    public int AgentMemoriesCreated { get; set; }
    public int AgentSessionsCreated { get; set; }
    public bool UserSettingsMigrated { get; set; }

    public override string ToString()
    {
        if (!Success)
        {
            return $"Migration failed: {ErrorMessage} (Duration: {Duration.TotalSeconds:F2}s)";
        }

        return $@"Migration completed successfully in {Duration.TotalSeconds:F2}s
- Users: {UsersCreated}
- Projects: {ProjectsCreated}
- Volumes: {VolumesCreated}
- Chapters: {ChaptersCreated}
- Characters: {CharactersCreated}
- Foreshadows: {ForeshadowsCreated}
- World Settings: {WorldSettingsCreated}
- Agent Memories: {AgentMemoriesCreated}
- Agent Sessions: {AgentSessionsCreated}
- User Settings: {(UserSettingsMigrated ? "Yes" : "No")}";
    }
}

internal class StoryBibleMigrationResult
{
    public int Characters { get; set; }
    public int Foreshadows { get; set; }
    public int WorldSettings { get; set; }
    public int Volumes { get; set; }
}
