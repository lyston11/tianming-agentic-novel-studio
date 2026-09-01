using Npgsql;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Auth;

namespace TM.Web.NovelAgentWeb.Services.Execution;

public sealed class ModelExecutionRecoveryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ModelExecutionRecoveryWorker> _logger;
    private readonly IBackgroundClaimConnectionFactory _claimConnections;

    public ModelExecutionRecoveryWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IBackgroundClaimConnectionFactory claimConnections,
        ILogger<ModelExecutionRecoveryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _claimConnections = claimConnections;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_configuration.GetValue("TargetArchitecture:ExecutionEnabled", false))
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            try
            {
                await RecoverOneAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to recover stale model execution");
            }
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task RecoverOneAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _claimConnections.OpenAsync(cancellationToken);
        var settlementBatchSize = Math.Clamp(
            _configuration.GetValue("TargetArchitecture:ModelSettlementBatchSize", 10),
            1,
            100);
        for (var index = 0; index < settlementBatchSize; index++)
        {
            await using var settlementCommand = connection.CreateCommand();
            settlementCommand.CommandText = "SELECT execution_id FROM settle_next_model_execution()";
            if (await settlementCommand.ExecuteScalarAsync(cancellationToken) is not string)
                break;
        }

        string? executionId = null;
        string? userId = null;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM claim_stale_model_execution(@stale_seconds)";
            command.Parameters.Add(new NpgsqlParameter(
                "stale_seconds",
                _configuration.GetValue("TargetArchitecture:ModelExecutionStaleSeconds", 120)));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                executionId = reader.GetString(reader.GetOrdinal("execution_id"));
                userId = reader.GetString(reader.GetOrdinal("user_id"));
            }
        }
        if (executionId == null || userId == null)
            return;

        await using var recoveryScope = _scopeFactory.CreateAsyncScope();
        var backgroundUser = recoveryScope.ServiceProvider.GetRequiredService<IBackgroundUserContext>();
        using var _ = backgroundUser.Push(userId);
        var recovery = recoveryScope.ServiceProvider.GetRequiredService<IModelExecutionRecoveryService>();
        await recovery.RecoverAsync(userId, executionId, cancellationToken);
    }
}
