using Npgsql;

namespace TM.Web.NovelAgentWeb.Services.Health;

public interface IPostgresHealthProbe
{
    Task<PostgresHealthProbeResult> CheckAsync(CancellationToken cancellationToken = default);
}

public sealed record PostgresHealthProbeResult(bool IsHealthy, string? Reason = null)
{
    public static PostgresHealthProbeResult Healthy() => new(true);

    public static PostgresHealthProbeResult Degraded(string reason) => new(false, reason);
}

public sealed class PostgresHealthProbe : IPostgresHealthProbe
{
    private readonly string _connectionString;
    private readonly ILogger<PostgresHealthProbe> _logger;

    public PostgresHealthProbe(IConfiguration configuration, ILogger<PostgresHealthProbe> logger)
    {
        _connectionString = configuration.GetConnectionString("NovelAgentDb")
            ?? throw new InvalidOperationException("ConnectionStrings:NovelAgentDb 未配置。");
        _logger = logger;
    }

    public async Task<PostgresHealthProbeResult> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is int result && result == 1
                ? PostgresHealthProbeResult.Healthy()
                : PostgresHealthProbeResult.Degraded("PostgreSQL probe value mismatch.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PostgreSQL health probe failed");
            return PostgresHealthProbeResult.Degraded("PostgreSQL probe failed.");
        }
    }
}
