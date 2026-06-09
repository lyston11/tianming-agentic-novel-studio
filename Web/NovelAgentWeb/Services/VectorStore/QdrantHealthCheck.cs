using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TM.Web.NovelAgentWeb.Services.VectorStore;

/// <summary>
/// Background service that periodically checks Qdrant health status.
/// </summary>
public class QdrantHealthCheck : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<QdrantHealthCheck> _logger;
    private readonly string _qdrantHealthUrl;
    private readonly TimeSpan _checkInterval;

    public QdrantHealthCheck(
        IHttpClientFactory httpClientFactory,
        ILogger<QdrantHealthCheck> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        var qdrantBaseUrl = configuration["Qdrant:BaseUrl"] ?? "http://localhost:6333";
        _qdrantHealthUrl = $"{qdrantBaseUrl}/healthz";

        var intervalSeconds = configuration.GetValue<int>("Qdrant:HealthCheckIntervalSeconds", 60);
        _checkInterval = TimeSpan.FromSeconds(intervalSeconds);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("QdrantHealthCheck service started. Checking health at {Url} every {Interval} seconds",
            _qdrantHealthUrl, _checkInterval.TotalSeconds);

        // Initial delay to allow application startup
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckHealthAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Qdrant health check");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("QdrantHealthCheck service stopped");
    }

    private async Task CheckHealthAsync(CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);

        try
        {
            var response = await client.GetAsync(_qdrantHealthUrl, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogDebug("Qdrant health check passed: {Status}", content);
            }
            else
            {
                _logger.LogWarning("Qdrant health check failed with status code: {StatusCode}", response.StatusCode);
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to connect to Qdrant at {Url}. Ensure Qdrant container is running.", _qdrantHealthUrl);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Qdrant health check timed out at {Url}", _qdrantHealthUrl);
        }
    }

    /// <summary>
    /// Performs a synchronous health check and returns the result.
    /// </summary>
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);

        try
        {
            var response = await client.GetAsync(_qdrantHealthUrl, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
