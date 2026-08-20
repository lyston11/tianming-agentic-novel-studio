using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tianming.NovelAgent.Application.Conversation;
using Tianming.NovelAgent.Application.Models;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Application.Workflow;
using Tianming.NovelAgent.Infrastructure.Persistence;
using Tianming.NovelAgent.Infrastructure.Models;
using Tianming.NovelAgent.Infrastructure.Conversation;
using OpenAI.Responses;
using Microsoft.Agents.AI;

namespace Tianming.NovelAgent.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddNovelAgentInfrastructure(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDatabase)
    {
        services.AddSingleton<AgentUserScope>();
        services.AddSingleton<IUserScope>(x => x.GetRequiredService<AgentUserScope>());
        services.AddSingleton<AgentUserScopeConnectionInterceptor>();
        services.AddDbContext<AgentControlDbContext>((serviceProvider, options) =>
        {
            configureDatabase(options);
            options.AddInterceptors(serviceProvider.GetRequiredService<AgentUserScopeConnectionInterceptor>());
        });
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidIdGenerator>();
        services.AddSingleton<IContractHasher, Sha256ContractHasher>();
        services.AddSingleton<ITransientAgentStream, NullTransientAgentStream>();
        services.AddScoped<EfAgentControlStore>();
        services.AddScoped<IAgentUnitOfWork>(x => x.GetRequiredService<EfAgentControlStore>());
        services.AddScoped<IConversationStore>(x => x.GetRequiredService<EfAgentControlStore>());
        services.AddScoped<IAgentToolRegistry, AgentToolRegistry>();
        services.AddScoped<IAgentTool, ConfirmCreativeGoalTool>();
        services.AddScoped<IGoalRepository>(x => x.GetRequiredService<EfAgentControlStore>());
        services.AddScoped<IProductionRepository>(x => x.GetRequiredService<EfAgentControlStore>());
        services.AddScoped<ILegacyControlPlaneCommands, EfLegacyControlPlaneCommands>();
        services.AddScoped<IAgentEventWriter>(x => x.GetRequiredService<EfAgentControlStore>());
        services.AddScoped<IStreamEventReader>(x => x.GetRequiredService<EfAgentControlStore>());
        services.AddScoped<IWorkflowReadModel, EfWorkflowReadModel>();
        services.AddScoped<IContextFreezer>(x => x.GetRequiredService<EfAgentControlStore>());
        services.AddScoped<ICanonLeaseManager>(x => x.GetRequiredService<EfAgentControlStore>());
        services.AddScoped<ICanonMergePort, OutboxCanonMergePort>();
        services.AddScoped<IModelExecutionStore, EfModelExecutionStore>();
        return services;
    }

    public static IServiceCollection AddNovelAgentPostgresInfrastructure(
        this IServiceCollection services,
        string connectionString) =>
        services.AddNovelAgentInfrastructure(options => options.UseNpgsql(
            connectionString,
            postgres => postgres.MigrationsHistoryTable("__AgentControlMigrationsHistory")));

    public static IServiceCollection AddOpenAIResponsesModelGateway(
        this IServiceCollection services,
        ResponsesClient client)
    {
        services.AddSingleton(client);
        services.AddScoped<IModelProviderAdapter, OpenAIResponsesModelAdapter>();
        services.TryAddScoped<IModelGateway, AuditedModelGateway>();
        return services;
    }

    public static IServiceCollection AddMafConversationRuntime(
        this IServiceCollection services,
        AIAgent agent)
    {
        services.AddSingleton(agent);
        services.AddScoped<IMafSessionCheckpointStore, EfMafSessionCheckpointStore>();
        services.AddScoped<IMafAgentInvoker, MafAIAgentInvoker>();
        services.AddScoped<IConversationAgentRuntime, MafConversationAgentRuntime>();
        return services;
    }
}
