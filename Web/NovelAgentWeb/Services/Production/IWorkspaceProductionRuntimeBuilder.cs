using TM.Services.Framework.AI.Embedding;
using TM.Services.Framework.AI.NovelAgent.Services;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Web.NovelAgentWeb.Services.Auth;
using TM.Web.NovelAgentWeb.Services.Memory;
using TM.Web.NovelAgentWeb.Services.VectorStore;
using TM.Web.NovelAgentWeb.Support;

namespace TM.Web.NovelAgentWeb.Services.Production;

public interface IWorkspaceProductionRuntimeBuilder
{
    WorkspaceProductionRuntime Build(WorkspaceProductionRuntimeRequest request);
}

public sealed record WorkspaceProductionRuntimeRequest(
    string UserId,
    string ProjectId,
    StoryBibleService StoryBibleService,
    CreativeKnowledgeBaseService CreativeKnowledgeBaseService,
    UserSettingsManager SettingsManager,
    IVectorStore? VectorStore,
    IMicroEmbeddingService? EmbeddingService,
    ICurrentUserService? CurrentUserService,
    IAgentMemoryRepository? MemoryRepository,
    IServiceScopeFactory ScopeFactory,
    IUnifiedValidationService UnifiedValidationService,
    IGuideContextService? GuideContextService = null,
    IContentChunkSearchService? ContentChunkSearchService = null,
    IGeneratedContentService? GeneratedContentService = null);

public sealed record WorkspaceProductionRuntime(
    NovelAgentOrchestrator Orchestrator,
    ITianmingProductionKernel ProductionKernel,
    IReadOnlyList<WorkspaceServiceRegistration> ServiceRegistrations);

public sealed record WorkspaceServiceRegistration(Type Type, object Instance);
