using TM.Services.Framework.AI.NovelAgent.Services;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;

namespace TM.Web.NovelAgentWeb.Services.Production;

public static class ProductionKernelServiceCollectionExtensions
{
    public static IServiceCollection AddTianmingProductionKernelServices(this IServiceCollection services)
    {
        services.AddSingleton<IRepositoryBoundaryGuard, RepositoryBoundaryGuard>();
        services.AddSingleton<IProductionKernelAssemblyGuard, ProductionKernelAssemblyGuard>();
        services.AddSingleton<IWorkspaceProductionRuntimeBuilder, WorkspaceProductionRuntimeBuilder>();
        services.AddSingleton<IChapterDirectiveBuilder, ChapterDirectiveBuilder>();
        services.AddSingleton<IChapterPromptBuilder, ChapterPromptBuilder>();
        services.AddSingleton<IChapterGatekeeper, ChapterGatekeeper>();
        services.AddSingleton<IChapterFactWriter, ChapterFactWriter>();
        services.AddSingleton<IChapterRewriter, ChapterRewriter>();
        services.AddSingleton<IChapterPackageBuilder, ChapterPackageBuilder>();
        services.AddScoped<IChapterPackageEnrichmentService, ChapterPackageEnrichmentService>();

        return services;
    }
}
