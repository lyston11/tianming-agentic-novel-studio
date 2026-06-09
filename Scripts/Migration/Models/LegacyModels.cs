using System.Text.Json.Serialization;

namespace TM.Scripts.Migration.Models;

/// <summary>
/// Legacy JSON model for projects.json
/// </summary>
public class LegacyProjectsRoot
{
    [JsonPropertyName("activeProjectId")]
    public string? ActiveProjectId { get; set; }

    [JsonPropertyName("projects")]
    public List<LegacyProject> Projects { get; set; } = new();

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}

public class LegacyProject
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = null!;

    [JsonPropertyName("title")]
    public string Title { get; set; } = null!;

    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    [JsonPropertyName("subGenre")]
    public string? SubGenre { get; set; }

    [JsonPropertyName("coreHook")]
    public string? CoreHook { get; set; }

    [JsonPropertyName("readerPromise")]
    public string? ReaderPromise { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("storageProjectName")]
    public string? StorageProjectName { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Legacy JSON model for story_bible.json
/// </summary>
public class LegacyStoryBible
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("constitution")]
    public LegacyConstitution? Constitution { get; set; }

    [JsonPropertyName("volumeArcs")]
    public List<LegacyVolumeArc> VolumeArcs { get; set; } = new();

    [JsonPropertyName("characters")]
    public List<LegacyCharacter> Characters { get; set; } = new();

    [JsonPropertyName("foreshadows")]
    public List<LegacyForeshadow> Foreshadows { get; set; } = new();

    [JsonPropertyName("worldSettings")]
    public List<LegacyWorldSetting> WorldSettings { get; set; } = new();
}

public class LegacyConstitution
{
    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    [JsonPropertyName("subGenre")]
    public string? SubGenre { get; set; }

    [JsonPropertyName("readerPromise")]
    public string? ReaderPromise { get; set; }

    [JsonPropertyName("coreHook")]
    public string? CoreHook { get; set; }

    [JsonPropertyName("coreTheme")]
    public string? CoreTheme { get; set; }

    [JsonPropertyName("worldCoreRule")]
    public string? WorldCoreRule { get; set; }
}

public class LegacyVolumeArc
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("volumeId")]
    public string? VolumeId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("startChapterId")]
    public string? StartChapterId { get; set; }

    [JsonPropertyName("endChapterId")]
    public string? EndChapterId { get; set; }

    [JsonPropertyName("expectedChapterCount")]
    public int? ExpectedChapterCount { get; set; }

    [JsonPropertyName("status")]
    public int? Status { get; set; }

    [JsonPropertyName("volumePromise")]
    public string? VolumePromise { get; set; }
}

public class LegacyCharacter
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("identity")]
    public string? Identity { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("personality")]
    public string? Personality { get; set; }

    [JsonPropertyName("firstAppearance")]
    public string? FirstAppearance { get; set; }

    [JsonPropertyName("detailJsonPath")]
    public string? DetailJsonPath { get; set; }
}

public class LegacyForeshadow
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("setupChapter")]
    public string? SetupChapter { get; set; }

    [JsonPropertyName("payoffChapter")]
    public string? PayoffChapter { get; set; }

    [JsonPropertyName("importance")]
    public string? Importance { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class LegacyWorldSetting
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = null!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("rules")]
    public string? Rules { get; set; }
}

/// <summary>
/// Legacy JSON model for user_settings.json
/// </summary>
public class LegacyUserSettings
{
    [JsonPropertyName("llmProvider")]
    public string? LlmProvider { get; set; }

    [JsonPropertyName("llmApiKey")]
    public string? LlmApiKey { get; set; }

    [JsonPropertyName("llmBaseUrl")]
    public string? LlmBaseUrl { get; set; }

    [JsonPropertyName("llmModel")]
    public string? LlmModel { get; set; }

    [JsonPropertyName("llmTemperature")]
    public float? LlmTemperature { get; set; }

    [JsonPropertyName("llmMaxTokens")]
    public int? LlmMaxTokens { get; set; }

    [JsonPropertyName("embeddingProvider")]
    public string? EmbeddingProvider { get; set; }

    [JsonPropertyName("embeddingModel")]
    public string? EmbeddingModel { get; set; }

    [JsonPropertyName("agentDefaultRisk")]
    public string? AgentDefaultRisk { get; set; }

    [JsonPropertyName("agentAutoContinue")]
    public bool? AgentAutoContinue { get; set; }

    [JsonPropertyName("agentMaxAutoSteps")]
    public int? AgentMaxAutoSteps { get; set; }

    [JsonPropertyName("defaultGenre")]
    public string? DefaultGenre { get; set; }

    [JsonPropertyName("defaultChapterWordCount")]
    public int? DefaultChapterWordCount { get; set; }

    [JsonPropertyName("theme")]
    public string? Theme { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }
}

/// <summary>
/// Legacy JSON model for agent memory files
/// </summary>
public class LegacyAgentMemory
{
    [JsonPropertyName("projectId")]
    public string? ProjectId { get; set; }

    [JsonPropertyName("longTermGoal")]
    public string? LongTermGoal { get; set; }

    [JsonPropertyName("readerPromise")]
    public string? ReaderPromise { get; set; }

    [JsonPropertyName("tone")]
    public string? Tone { get; set; }

    [JsonPropertyName("constraints")]
    public List<string> Constraints { get; set; } = new();

    [JsonPropertyName("unresolvedThreads")]
    public List<string> UnresolvedThreads { get; set; } = new();

    [JsonPropertyName("styleLikes")]
    public List<string> StyleLikes { get; set; } = new();

    [JsonPropertyName("styleDislikes")]
    public List<string> StyleDislikes { get; set; } = new();

    [JsonPropertyName("confirmationTolerance")]
    public string? ConfirmationTolerance { get; set; }

    [JsonPropertyName("genreHabits")]
    public List<string> GenreHabits { get; set; } = new();
}

/// <summary>
/// Legacy JSON model for sessions.json
/// </summary>
public class LegacySession
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = null!;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("phase")]
    public string? Phase { get; set; }

    [JsonPropertyName("activeProjectId")]
    public string? ActiveProjectId { get; set; }

    [JsonPropertyName("activeRunId")]
    public string? ActiveRunId { get; set; }

    [JsonPropertyName("isArchived")]
    public bool IsArchived { get; set; }

    [JsonPropertyName("chatHistory")]
    public List<object> ChatHistory { get; set; } = new();

    [JsonPropertyName("workingMemory")]
    public object? WorkingMemory { get; set; }
}
