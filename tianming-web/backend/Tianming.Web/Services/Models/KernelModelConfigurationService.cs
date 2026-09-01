using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Models;

public sealed record KernelModelFallback(
    string Provider,
    string? BaseUrl,
    string CredentialReference,
    string Model,
    decimal InputPricePerMillion = 0,
    decimal OutputPricePerMillion = 0);

public sealed record KernelModelConfigurationCommand(
    string UserId,
    string ProjectId,
    string KernelName,
    string PresetName,
    string Provider,
    string? BaseUrl,
    string CredentialReference,
    string Model,
    float? Temperature,
    int? MaxOutputTokens,
    int? TimeoutSeconds,
    IReadOnlyList<KernelModelFallback> Fallbacks,
    string CustomInstructions,
    bool AdvancedSettingsEnabled,
    decimal InputPricePerMillion = 0,
    decimal OutputPricePerMillion = 0);

public sealed record ResolvedKernelModelConfiguration(
    string KernelName,
    int? Version,
    string SourceLayer,
    string Provider,
    string? BaseUrl,
    string CredentialReference,
    string Model,
    float Temperature,
    int MaxOutputTokens,
    int TimeoutSeconds,
    IReadOnlyList<KernelModelFallback> Fallbacks,
    string CustomInstructions,
    decimal InputPricePerMillion,
    decimal OutputPricePerMillion);

public interface IKernelModelConfigurationService
{
    Task<ModelKernelConfiguration> SaveProjectConfigurationAsync(
        KernelModelConfigurationCommand command,
        CancellationToken cancellationToken = default);

    Task<ResolvedKernelModelConfiguration> ResolveAsync(
        string userId,
        string projectId,
        string kernelName,
        string presetName,
        string? goalId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModelKernelConfiguration>> ListProjectConfigurationsAsync(
        string userId,
        string projectId,
        CancellationToken cancellationToken = default);
}

public sealed class KernelModelConfigurationService : IKernelModelConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> KernelNames = new(StringComparer.Ordinal)
    {
        "setting",
        "narrative_planning",
        "tianming_writing",
        "continuity_review",
        "literary_review",
        "knowledge",
        "collaboration_memory",
        "rag_query_planner"
    };

    private static readonly IReadOnlyDictionary<string, ModelPreset> Presets =
        new Dictionary<string, ModelPreset>(StringComparer.OrdinalIgnoreCase)
        {
            ["quality"] = new(0.45f, 8192, 180),
            ["balanced"] = new(0.65f, 4096, 120),
            ["cost"] = new(0.5f, 2048, 90)
        };

    private readonly NovelAgentDbContext _db;

    public KernelModelConfigurationService(NovelAgentDbContext db)
    {
        _db = db;
    }

    public async Task<ModelKernelConfiguration> SaveProjectConfigurationAsync(
        KernelModelConfigurationCommand command,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectOwnedAsync(command.UserId, command.ProjectId, cancellationToken);
        var kernelName = ValidateKernel(command.KernelName);
        var preset = GetPreset(command.PresetName);
        var customInstructions = command.CustomInstructions?.Trim() ?? string.Empty;
        if (customInstructions.Length > 0 && !command.AdvancedSettingsEnabled)
            throw new InvalidOperationException("CustomInstructions 只能在高级设置开启时保存。");
        KernelPromptAssembler.ValidateCustomInstructions(customInstructions);
        ValidateConnection(command.Provider, command.BaseUrl, command.CredentialReference, command.Model);
        ValidateFallbacks(command.Fallbacks);
        ValidatePrices(command.InputPricePerMillion, command.OutputPricePerMillion);

        var temperature = command.Temperature ?? preset.Temperature;
        var maxOutputTokens = command.MaxOutputTokens ?? preset.MaxOutputTokens;
        var timeoutSeconds = command.TimeoutSeconds ?? preset.TimeoutSeconds;
        ValidateSampling(temperature, maxOutputTokens, timeoutSeconds);

        var rows = await _db.ModelKernelConfigurations
            .Where(item => item.UserId == command.UserId && item.ProjectId == command.ProjectId && item.KernelName == kernelName)
            .ToListAsync(cancellationToken);
        foreach (var active in rows.Where(item => item.Status == "active"))
            active.Status = "superseded";
        var configuration = new ModelKernelConfiguration
        {
            UserId = command.UserId,
            ProjectId = command.ProjectId,
            KernelName = kernelName,
            Version = rows.Count == 0 ? 1 : rows.Max(item => item.Version) + 1,
            Provider = command.Provider.Trim(),
            BaseUrl = string.IsNullOrWhiteSpace(command.BaseUrl) ? null : command.BaseUrl.Trim(),
            CredentialReference = command.CredentialReference.Trim(),
            Model = command.Model.Trim(),
            Temperature = temperature,
            MaxOutputTokens = maxOutputTokens,
            TimeoutSeconds = timeoutSeconds,
            InputPricePerMillion = command.InputPricePerMillion,
            OutputPricePerMillion = command.OutputPricePerMillion,
            FallbackJson = JsonSerializer.Serialize(command.Fallbacks, JsonOptions),
            CustomInstructions = customInstructions
        };
        _db.ModelKernelConfigurations.Add(configuration);
        await _db.SaveChangesAsync(cancellationToken);
        return configuration;
    }

    public async Task<ResolvedKernelModelConfiguration> ResolveAsync(
        string userId,
        string projectId,
        string kernelName,
        string presetName,
        string? goalId,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectOwnedAsync(userId, projectId, cancellationToken);
        kernelName = ValidateKernel(kernelName);
        var preset = GetPreset(presetName);
        ModelKernelConfiguration? selected;
        var sourceLayer = "system";
        if (!string.IsNullOrWhiteSpace(goalId))
        {
            var snapshot = await _db.GoalContextSnapshots.AsNoTracking().SingleOrDefaultAsync(item =>
                item.UserId == userId && item.ProjectId == projectId && item.GoalId == goalId,
                cancellationToken) ?? throw new KeyNotFoundException("Goal 上下文快照不存在或不属于当前项目。");
            var versions = JsonSerializer.Deserialize<Dictionary<string, int>>(
                    snapshot.ModelConfigVersionsJson,
                    JsonOptions)
                ?? throw new InvalidOperationException("Goal 模型配置版本快照无效。");
            if (versions.TryGetValue(kernelName, out var version))
            {
                selected = await _db.ModelKernelConfigurations.AsNoTracking().SingleOrDefaultAsync(item =>
                    item.UserId == userId && item.ProjectId == projectId &&
                    item.KernelName == kernelName && item.Version == version,
                    cancellationToken) ?? throw new InvalidOperationException("Goal 冻结的模型配置版本不存在。");
                sourceLayer = "goal";
            }
            else
            {
                selected = null;
            }
        }
        else
        {
            selected = await _db.ModelKernelConfigurations.AsNoTracking().SingleOrDefaultAsync(item =>
                item.UserId == userId && item.ProjectId == projectId &&
                item.KernelName == kernelName && item.Status == "active",
                cancellationToken);
            if (selected != null)
                sourceLayer = "project";
        }

        if (selected == null)
        {
            return new ResolvedKernelModelConfiguration(
                kernelName,
                null,
                sourceLayer,
                string.Empty,
                null,
                string.Empty,
                string.Empty,
                preset.Temperature,
                preset.MaxOutputTokens,
                preset.TimeoutSeconds,
                [],
                string.Empty,
                0,
                0);
        }

        var fallbacks = JsonSerializer.Deserialize<KernelModelFallback[]>(selected.FallbackJson, JsonOptions)
            ?? throw new InvalidOperationException("模型 fallback 配置无效。");
        return new ResolvedKernelModelConfiguration(
            kernelName,
            selected.Version,
            sourceLayer,
            selected.Provider,
            selected.BaseUrl,
            selected.CredentialReference,
            selected.Model,
            selected.Temperature,
            selected.MaxOutputTokens,
            selected.TimeoutSeconds,
            fallbacks,
            selected.CustomInstructions,
            selected.InputPricePerMillion,
            selected.OutputPricePerMillion);
    }

    public async Task<IReadOnlyList<ModelKernelConfiguration>> ListProjectConfigurationsAsync(
        string userId,
        string projectId,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectOwnedAsync(userId, projectId, cancellationToken);
        return await _db.ModelKernelConfigurations.AsNoTracking()
            .Where(item => item.UserId == userId && item.ProjectId == projectId && item.Status == "active")
            .OrderBy(item => item.KernelName)
            .ToArrayAsync(cancellationToken);
    }

    private async Task EnsureProjectOwnedAsync(
        string userId,
        string projectId,
        CancellationToken cancellationToken)
    {
        var owned = await _db.NovelProjects.AsNoTracking().AnyAsync(
            project => project.Id == projectId && project.UserId == userId,
            cancellationToken);
        if (!owned)
            throw new KeyNotFoundException("项目不存在或不属于当前用户。");
    }

    private static string ValidateKernel(string kernelName)
    {
        kernelName = kernelName?.Trim() ?? string.Empty;
        if (!KernelNames.Contains(kernelName))
            throw new ArgumentException("未知的专业内核。", nameof(kernelName));
        return kernelName;
    }

    private static ModelPreset GetPreset(string presetName)
    {
        if (!Presets.TryGetValue(presetName?.Trim() ?? string.Empty, out var preset))
            throw new ArgumentException("模型 preset 只能是 quality、balanced 或 cost。", nameof(presetName));
        return preset;
    }

    private static void ValidateConnection(
        string provider,
        string? baseUrl,
        string credentialReference,
        string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("模型 Base URL 必须是绝对 HTTP(S) URL。", nameof(baseUrl));
        if (!string.Equals(credentialReference, "user-settings:llm", StringComparison.Ordinal))
            throw new ArgumentException("当前只支持 user-settings:llm 凭据引用。", nameof(credentialReference));
    }

    private static void ValidateFallbacks(IReadOnlyList<KernelModelFallback> fallbacks)
    {
        if (fallbacks.Count > 3)
            throw new ArgumentOutOfRangeException(nameof(fallbacks), "每个内核最多配置 3 个显式 fallback。");
        foreach (var fallback in fallbacks)
        {
            ValidateConnection(fallback.Provider, fallback.BaseUrl, fallback.CredentialReference, fallback.Model);
            ValidatePrices(fallback.InputPricePerMillion, fallback.OutputPricePerMillion);
        }
    }

    private static void ValidatePrices(decimal inputPricePerMillion, decimal outputPricePerMillion)
    {
        if (inputPricePerMillion is < 0 or > 100000 || outputPricePerMillion is < 0 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(inputPricePerMillion), "模型单价必须在 0 到 100000 USD/百万 token 之间。");
    }

    private static void ValidateSampling(float temperature, int maxOutputTokens, int timeoutSeconds)
    {
        if (temperature is < 0f or > 2f)
            throw new ArgumentOutOfRangeException(nameof(temperature));
        if (maxOutputTokens is < 256 or > 200000)
            throw new ArgumentOutOfRangeException(nameof(maxOutputTokens));
        if (timeoutSeconds is < 5 or > 600)
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
    }

    private sealed record ModelPreset(float Temperature, int MaxOutputTokens, int TimeoutSeconds);
}
