using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.SemanticKernel.ChatCompletion;
using TM.Services.Framework.AI.Interfaces.AI;

namespace TM.Services.Framework.AI.Core;

public sealed partial class AIService : IAIConfigurationService, IAILibraryService, IAITextGenerationService
{

    private List<AICategory> _categories = new();
    private List<AIProvider> _providers = new();
    private List<AIModel> _models = new();
    private List<UserConfiguration> _userConfigurations = new();
    private readonly object _userConfigurationsLock = new();
    private readonly SemaphoreSlim _userConfigurationsSaveLock = new(1, 1);

    public event EventHandler? ConfigurationsChanged;

    private readonly HashSet<string> _compatibilityFallbackModels = new(StringComparer.OrdinalIgnoreCase);

    private static string BaseDeveloperMessage => SemanticKernel.Prompts.PromptLibrary.GetDeveloperPrompt();

    private readonly string _libraryPath;
    private readonly string _configurationsPath;
    private readonly JsonSerializerOptions _jsonOptions;

    private sealed class BusinessSessionState
    {
        public string Key { get; }
        public ChatHistory History { get; }
        public bool IsInitialized { get; set; }
        public bool IsDirty { get; set; }
        public SemaphoreSlim Gate { get; }
        private int _referenceCount;
        private int _removed;
        private int _disposed;

        public BusinessSessionState(string key)
        {
            Key = key;
            History = new ChatHistory();
            Gate = new SemaphoreSlim(1, 1);
        }

        public void AddReference()
        {
            Interlocked.Increment(ref _referenceCount);
        }

        public void ReleaseReference()
        {
            Interlocked.Decrement(ref _referenceCount);
        }

        public void MarkRemoved()
        {
            Volatile.Write(ref _removed, 1);
        }

        public void TryDisposeGateIfUnused()
        {
            if (Volatile.Read(ref _removed) == 0 || Volatile.Read(ref _referenceCount) != 0)
            {
                return;
            }

            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Gate.Dispose();
        }
    }

    private readonly Dictionary<string, BusinessSessionState> _businessSessions = new(StringComparer.Ordinal);
    private string? _lastDirtyBusinessSessionKey;

    public System.Threading.Tasks.Task InitializedAsync { get; }

    public AIService()
    {
        _libraryPath = StoragePathHelper.GetFilePath("Services", "AI", "Library");
        _configurationsPath = StoragePathHelper.GetFilePath("Services", "AI", "Configurations");

        _jsonOptions = JsonHelper.CnDefault;

        InitializedAsync = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                await LoadLibraryAsync().ConfigureAwait(false);
                await LoadUserConfigurationsAsync().ConfigureAwait(false);
                FillCategoryPrefixes();
                ConfigurationsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex) { TM.App.Log($"[AIService] 初始化失败: {ex.Message}"); }
        });

        TM.App.Log("[AIService] AI核心服务已初始化");
    }

    public bool HasDirtyBusinessSession(string businessSessionKey)
    {
        if (string.IsNullOrWhiteSpace(businessSessionKey))
        {
            return false;
        }

        lock (_businessSessions)
        {
            return _businessSessions.TryGetValue(businessSessionKey, out var state) && state.IsDirty;
        }
    }

    public bool HasDirtyBusinessSessionsByPrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return false;
        }

        var prefixWithUnderscore = prefix + "_";
        lock (_businessSessions)
        {
            foreach (var kv in _businessSessions)
            {
                if (!kv.Value.IsDirty)
                {
                    continue;
                }

                var key = kv.Key;
                if (string.Equals(key, prefix, StringComparison.Ordinal)
                    || key.StartsWith(prefixWithUnderscore, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public bool TryGetDirtyBusinessSessionKey(out string businessSessionKey)
    {
        businessSessionKey = string.Empty;

        lock (_businessSessions)
        {
            if (string.IsNullOrWhiteSpace(_lastDirtyBusinessSessionKey))
            {
                return false;
            }

            if (_businessSessions.TryGetValue(_lastDirtyBusinessSessionKey, out var state) && state.IsDirty)
            {
                businessSessionKey = _lastDirtyBusinessSessionKey;
                return true;
            }

            _lastDirtyBusinessSessionKey = null;
            return false;
        }
    }

    public void EndBusinessSession(string businessSessionKey)
    {
        if (string.IsNullOrWhiteSpace(businessSessionKey))
        {
            return;
        }

        BusinessSessionState? removed = null;
        lock (_businessSessions)
        {
            if (_businessSessions.TryGetValue(businessSessionKey, out removed))
            {
                _businessSessions.Remove(businessSessionKey);
                if (string.Equals(_lastDirtyBusinessSessionKey, businessSessionKey, StringComparison.Ordinal))
                {
                    _lastDirtyBusinessSessionKey = null;
                }
            }
        }
        if (removed != null)
        {
            removed.IsDirty = false;
            removed.IsInitialized = false;
            removed.MarkRemoved();
            removed.TryDisposeGateIfUnused();
        }
    }

    public void ClearAllBusinessSessions()
    {
        List<BusinessSessionState> toDispose;
        lock (_businessSessions)
        {
            toDispose = new List<BusinessSessionState>(_businessSessions.Values);
            _businessSessions.Clear();
            _lastDirtyBusinessSessionKey = null;
        }
        foreach (var s in toDispose)
        {
            s.IsDirty = false;
            s.IsInitialized = false;
            s.MarkRemoved();
            s.TryDisposeGateIfUnused();
        }
        TM.App.Log("[AIService] 已清理所有业务会话");
    }

    public void EndBusinessSessionsByPrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return;

        var prefixWithUnderscore = prefix + "_";
        List<BusinessSessionState> removed = new();

        List<string> allKeys;
        lock (_businessSessions)
            allKeys = _businessSessions.Keys.ToList();

        var keysToRemove = allKeys
            .Where(k => string.Equals(k, prefix, StringComparison.Ordinal) ||
                        k.StartsWith(prefixWithUnderscore, StringComparison.Ordinal))
            .ToList();

        lock (_businessSessions)
        {
            foreach (var key in keysToRemove)
            {
                if (_businessSessions.TryGetValue(key, out var state))
                {
                    _businessSessions.Remove(key);
                    removed.Add(state);
                }
            }

            if (_lastDirtyBusinessSessionKey != null &&
                (string.Equals(_lastDirtyBusinessSessionKey, prefix, StringComparison.Ordinal) ||
                 _lastDirtyBusinessSessionKey.StartsWith(prefixWithUnderscore, StringComparison.Ordinal)))
            {
                _lastDirtyBusinessSessionKey = null;
            }
        }

        if (removed.Count == 0 && keysToRemove.Count == 0) return;

        foreach (var state in removed)
        {
            state.IsDirty = false;
            state.IsInitialized = false;
            state.MarkRemoved();
            state.TryDisposeGateIfUnused();
        }

        if (removed.Count > 0)
            TM.App.Log($"[AIService] 已清理业务会话（前缀={prefix}）：{removed.Count} 个");
    }

    public async System.Threading.Tasks.Task<GenerationResult> GenerateInBusinessSessionAsync(
        string businessSessionKey,
        Func<System.Threading.Tasks.Task<string>>? initialContextProvider,
        string userPrompt,
        System.Threading.CancellationToken ct,
        bool isNavigationGuarded = true,
        string? overrideConfigId = null)
        => await GenerateInBusinessSessionAsync(businessSessionKey, initialContextProvider, userPrompt, null, ct, isNavigationGuarded, overrideConfigId).ConfigureAwait(false);

    public async System.Threading.Tasks.Task<GenerationResult> GenerateInBusinessSessionAsync(
        string businessSessionKey,
        Func<System.Threading.Tasks.Task<string>>? initialContextProvider,
        string userPrompt,
        IProgress<string>? progress,
        System.Threading.CancellationToken ct,
        bool isNavigationGuarded = true,
        string? overrideConfigId = null)
    {
        var result = new GenerationResult();

        if (string.IsNullOrWhiteSpace(businessSessionKey))
        {
            result.Success = false;
            result.ErrorMessage = "BusinessSessionKey为空";
            return result;
        }

        using var _progressRunScope = TM.Services.Framework.AI.SemanticKernel.GenerationProgressHub.BeginRun(Guid.Empty);

        try
        {
            ct.ThrowIfCancellationRequested();

            UserConfiguration? activeConfig = null;
            if (!string.IsNullOrWhiteSpace(overrideConfigId))
            {
                lock (_userConfigurationsLock)
                    activeConfig = _userConfigurations.FirstOrDefault(c => c.Id == overrideConfigId && c.IsEnabled);
                if (activeConfig == null)
                    TM.App.Log($"[AIService] 指定配置 {overrideConfigId} 不存在或未启用，回退全局激活配置");
            }
            activeConfig ??= GetActiveConfiguration();
            if (activeConfig == null)
            {
                result.Success = false;
                result.ErrorMessage = "当前没有激活的AI模型";
                return result;
            }

            var model = GetModelById(activeConfig.ModelId);
            if (model == null && InfoLogDedup.ShouldLog($"AIService:CustomModel:{activeConfig.ModelId}"))
            {
                TM.App.Log($"[AIService] 模型库未收录 {activeConfig.ModelId}，将作为自定义模型继续调用");
            }

            BusinessSessionState state;
            lock (_businessSessions)
            {
                if (!_businessSessions.TryGetValue(businessSessionKey, out state!))
                {
                    state = new BusinessSessionState(businessSessionKey);
                    _businessSessions[businessSessionKey] = state;
                }

                state.AddReference();
            }

            var gateAcquired = false;
            bool sessionTerminated = false;
            try
            {
                await state.Gate.WaitAsync(ct).ConfigureAwait(false);
                gateAcquired = true;

                state.IsDirty = true;
                if (isNavigationGuarded)
                    _lastDirtyBusinessSessionKey = businessSessionKey;

                if (!state.IsInitialized)
                {
                    var developerPrompt = GetEffectiveDeveloperMessage(activeConfig) ?? string.Empty;
                    var initialContext = string.Empty;
                    if (initialContextProvider != null)
                    {
                        initialContext = await initialContextProvider().ConfigureAwait(false);
                    }

                    var systemText = string.IsNullOrWhiteSpace(initialContext)
                        ? developerPrompt
                        : developerPrompt + "\n\n" + initialContext;

                    if (!string.IsNullOrWhiteSpace(systemText))
                    {
                        state.History.AddSystemMessage(systemText);
                    }

                    state.IsInitialized = true;
                }

                var sk = ServiceLocator.Get<TM.Services.Framework.AI.SemanticKernel.SKChatService>();
                var text = await sk.GenerateWithChatHistoryAsync(
                    state.History, userPrompt, progress, ct,
                    !string.IsNullOrWhiteSpace(overrideConfigId) ? activeConfig : null).ConfigureAwait(false);

                if (text.StartsWith("[会话终止]", StringComparison.Ordinal))
                {
                    sessionTerminated = true;
                    result.Success = false;
                    result.ErrorMessage = text;
                    return result;
                }

                var (isCancelled, _) = TM.Services.Framework.AI.SemanticKernel.UIMessageItem.TryExtractCancelledPartial(text);
                if (text.StartsWith("[错误]", StringComparison.Ordinal) || isCancelled)
                {
                    result.Success = false;
                    result.ErrorMessage = isCancelled ? "[已取消]" : text;
                    return result;
                }

                result.Success = true;
                result.Content = text;
                return result;
            }
            finally
            {
                if (gateAcquired)
                {
                    try { state.Gate.Release(); } catch (ObjectDisposedException) { }
                }

                if (sessionTerminated)
                {
                    EndBusinessSession(businessSessionKey);
                }

                state.ReleaseReference();
                state.TryDisposeGateIfUnused();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            TM.App.Log($"[AIService] GenerateInBusinessSessionAsync 调用失败: {ex.Message}");
            result.Success = false;
            result.ErrorMessage = $"AI生成失败：{ex.Message}";
            return result;
        }
    }

    private void FillCategoryPrefixes()
    {
        List<UserConfiguration> snapshot;
        lock (_userConfigurationsLock)
            snapshot = _userConfigurations.ToList();
        foreach (var cfg in snapshot)
            FillCategoryPrefixForSingle(cfg);
    }

    private void FillCategoryPrefixForSingle(UserConfiguration config)
    {
        var provider = _providers.FirstOrDefault(p =>
            string.Equals(p.Id, config.ProviderId, StringComparison.OrdinalIgnoreCase));
        config.CategoryPrefix = (!string.IsNullOrEmpty(provider?.Category))
            ? provider!.Category[0].ToString()
            : string.Empty;
    }

}
