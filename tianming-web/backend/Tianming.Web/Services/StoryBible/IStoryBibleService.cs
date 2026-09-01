using TM.Web.NovelAgentWeb.DTOs;

namespace TM.Web.NovelAgentWeb.Services.StoryBible;

/// <summary>
/// Service interface for managing story bible components (constitution and characters).
/// </summary>
public interface IStoryBibleService
{
    Task<StoryBibleResponse> GetStoryBibleByProjectAsync(string projectId, CancellationToken ct = default);

    // Story Constitution operations
    Task<StoryConstitutionResponse> CreateConstitutionAsync(CreateStoryConstitutionRequest request, CancellationToken ct = default);
    Task<StoryConstitutionResponse?> GetConstitutionByProjectAsync(string projectId, CancellationToken ct = default);
    Task<StoryConstitutionResponse> UpdateConstitutionAsync(string constitutionId, UpdateStoryConstitutionRequest request, CancellationToken ct = default);
    Task DeleteConstitutionAsync(string constitutionId, CancellationToken ct = default);

    // Character operations
    Task<CharacterResponse> CreateCharacterAsync(CreateCharacterRequest request, CancellationToken ct = default);
    Task<List<CharacterResponse>> ListCharactersAsync(string projectId, CancellationToken ct = default);
    Task<CharacterResponse> GetCharacterAsync(string characterId, CancellationToken ct = default);
    Task<CharacterResponse> UpdateCharacterAsync(string characterId, UpdateCharacterRequest request, CancellationToken ct = default);
    Task DeleteCharacterAsync(string characterId, CancellationToken ct = default);
}
