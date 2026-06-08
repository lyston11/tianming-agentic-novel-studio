#pragma warning disable SKEXP0130

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.Core.Capabilities;
using TM.Services.Framework.AI.SemanticKernel.Plugins;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Thinking;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public partial class SKChatService
    {
        #region Kernel 管理

        private void CheckDirectKernelRecovery()
        {
            if (!_useDirectKernel && _directKernelDisabledAt.HasValue
                && DateTime.UtcNow - _directKernelDisabledAt.Value >= DirectKernelRetryAfter)
            {
                _useDirectKernel = true;
                _directKernelDisabledAt = null;
                InvalidateAllBundles();
                LogIfPublic(null, $"[SKChatService] 代理模式已运行 {DirectKernelRetryAfter.TotalMinutes:F0} 分钟，自动尝试恢复直连");
            }
        }

        private KernelBundle? EnsureKernelInitialized(UserConfiguration? config = null)
        {
            config ??= AI.GetActiveConfiguration();
            if (config == null)
            {
                TM.App.Log("[SKChatService] 无激活配置");
                return null;
            }
            return EnsureKernelInitialized(config, config.ApiKey ?? string.Empty);
        }

        private KernelBundle? EnsureKernelInitialized(UserConfiguration config, string explicitApiKey)
        {
            if (config == null) return null;

            CheckDirectKernelRecovery();

            var key = BuildKernelConfigKey(config, explicitApiKey);

            if (_kernelBundles.TryGetValue(key, out var cached))
            {
                UpdateSnapshotFields(cached);
                return cached;
            }

            lock (_kernelLock)
            {
                if (_kernelBundles.TryGetValue(key, out cached))
                {
                    UpdateSnapshotFields(cached);
                    return cached;
                }

                _skipThinkingInjection = false;
                var bundle = BuildKernel(config, explicitApiKey, key);
                if (bundle != null)
                {
                    _kernelBundles[key] = bundle;
                    UpdateSnapshotFields(bundle);
                    EvictStaleSiblingBundles(key);
                }
                return bundle;
            }
        }

        private void UpdateSnapshotFields(KernelBundle bundle)
        {
            _kernel = bundle.Kernel;
            _chatService = bundle.ChatService;
            _novelAgent = bundle.NovelAgent;
            _kernelHttpClient = bundle.HttpClient;
            _currentProviderType = bundle.ProviderType;
            _lastKernelConfigKey = bundle.ConfigKey;
        }

        private KernelBundle? BuildKernel(UserConfiguration config, string explicitApiKey, string key)
        {
            HttpClient? httpClient = null;
            try
            {
                var builder = Kernel.CreateBuilder();

                var provider = AI.GetProviderById(config.ProviderId);
                var model = AI.GetModelById(config.ModelId);

                if (provider == null)
                {
                    LogIfPublic(config, $"[SKChatService] 未找到供应商: {config.ProviderId}");
                    return null;
                }

                var modelName = model?.Name ?? config.ModelId;
                if (string.IsNullOrWhiteSpace(modelName))
                {
                    LogIfPublic(config, $"[SKChatService] 模型名称为空: {config.ModelId}");
                    return null;
                }

                modelName = StripModelNamePrefix(modelName);

                if (model == null)
                {
                    LogIfPublic(config, $"[SKChatService] 使用自定义模型: {modelName}");
                }
                var apiKey = explicitApiKey;
                var baseUrl = config.CustomEndpoint;

                var isPrivateProvider = IsTianmingPrivateProvider(config.ProviderId) || IsTianmingPrivateProvider(provider.Id);
                var kernelInfoKey = isPrivateProvider ? "Kernel|private" : $"Kernel|{provider.Name}|{modelName}|{baseUrl}";
                if (InfoLogDedup.ShouldLog(kernelInfoKey))
                    LogIfPublic(config, $"[SKChatService] 构建 Kernel: Provider={provider.Name}, Model={modelName}");

                HttpMessageHandler innerHandler;
                if (_useDirectKernel)
                {
                    innerHandler = new SocketsHttpHandler
                    {
                        UseProxy = false,
                        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                        MaxConnectionsPerServer = 10,
                        EnableMultipleHttp2Connections = true,
                        ConnectTimeout = TimeSpan.FromSeconds(15),
                        KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
                        KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                        KeepAlivePingTimeout = TimeSpan.FromSeconds(10)
                    };
                }
                else
                {
                    innerHandler = Proxy.CreateHttpMessageHandler();
                }

                string timeoutSource;
                if (IsLocalEndpoint(baseUrl))
                    timeoutSource = "HttpClient=无限 + 流式 idle / 非流式 CTS 控制（本地端点）";
                else if (config.TimeoutSeconds > 0)
                    timeoutSource = $"HttpClient=无限 + 非流式总超时={config.TimeoutSeconds}秒（用户配置）+ 流式 idle 由 SK 控制";
                else
                    timeoutSource = "HttpClient=无限 + 非流式总超时=120秒（默认兜底）+ 流式 idle 由 SK 控制";

                httpClient = new HttpClient(
                    new CherryStudioDelegatingHandler(apiKey ?? string.Empty, innerHandler),
                    disposeHandler: true)
                { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
                if (InfoLogDedup.ShouldLog($"{kernelInfoKey}|Timeout|{config.TimeoutSeconds}"))
                    LogIfPublic(config, $"[SKChatService] HTTP超时策略: {timeoutSource}");

                builder.Services.AddSingleton<IFunctionInvocationFilter, PlanModeFilter>();

                var protocol = ResolveProtocol(baseUrl, provider.Name);
                if (InfoLogDedup.ShouldLog($"{kernelInfoKey}|Protocol|{protocol}"))
                {
                    LogIfPublic(config, $"[SKChatService] 协议推断: Provider={provider.Name}, BaseUrl={baseUrl}, Protocol={protocol}");
                }

                string providerType;
                Kernel kernelLocal;
                IChatCompletionService chatServiceLocal;

                switch (protocol)
                {
                    case "anthropic":
                        var anthropicEnableLong = config.EnableLongContext == true && config.SupportsLongContext;
                        var anthropicService = new AnthropicChatCompletionService(
                            apiKey ?? string.Empty,
                            modelName,
                            baseUrl,
                            config.TimeoutSeconds,
                            httpClient,
                            enableLongContext: anthropicEnableLong,
                            providerId: config.ProviderId,
                            configModelIdForCache: config.ModelId);
                        builder.Services.AddSingleton<IChatCompletionService>(anthropicService);
                        kernelLocal = builder.Build();
                        chatServiceLocal = anthropicService;
                        providerType = "Anthropic";
                        RegisterPlugins(kernelLocal);
                        if (InfoLogDedup.ShouldLog($"{kernelInfoKey}|ProviderOk|Anthropic"))
                            LogIfPublic(config, "[SKChatService] provider ok (Anthropic)");
                        break;

                    case "gemini":
                        builder.AddGoogleAIGeminiChatCompletion(
                            modelId: modelName,
                            apiKey: apiKey ?? string.Empty,
                            httpClient: httpClient);
                        providerType = "Google";
                        kernelLocal = builder.Build();
                        chatServiceLocal = kernelLocal.GetRequiredService<IChatCompletionService>();
                        RegisterPlugins(kernelLocal);
                        if (InfoLogDedup.ShouldLog($"{kernelInfoKey}|ProviderOk|Gemini"))
                            LogIfPublic(config, "[SKChatService] provider ok (Gemini)");
                        break;

                    case "azure-openai":
                        builder.AddAzureOpenAIChatCompletion(
                            deploymentName: modelName,
                            endpoint: baseUrl ?? "",
                            apiKey: apiKey ?? string.Empty,
                            httpClient: httpClient);
                        providerType = "TagBased";
                        kernelLocal = builder.Build();
                        chatServiceLocal = kernelLocal.GetRequiredService<IChatCompletionService>();
                        RegisterPlugins(kernelLocal);
                        if (InfoLogDedup.ShouldLog($"{kernelInfoKey}|ProviderOk|Azure"))
                            LogIfPublic(config, "[SKChatService] provider ok (Azure)");
                        break;

                    default:
                        var openAiEnableLong = config.EnableLongContext == true
                            && config.SupportsLongContext
                            && !ChatModeSettings.IsUnsupportedParam(config.ProviderId, baseUrl, config.ModelId ?? modelName, "long_context");
                        var isOpenRouterRequest = baseUrl?.Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase) == true;
                        var isDefaultLongContextModel = TM.Services.Framework.AI.Core.ModelFamilyClassifier
                            .IsDefaultLongContextModel(config.ModelId ?? modelName, config.ProviderId);
                        var longCtxSuffix = isOpenRouterRequest ? ":extended" : "[1m]";
                        var alreadyHasLongCtxSuffix = modelName.EndsWith("[1m]", StringComparison.OrdinalIgnoreCase)
                            || modelName.EndsWith(":extended", StringComparison.OrdinalIgnoreCase);
                        var shouldAppendLongCtxSuffix = openAiEnableLong
                            && !alreadyHasLongCtxSuffix
                            && !isDefaultLongContextModel;
                        var openAiModelName = shouldAppendLongCtxSuffix
                            ? modelName + longCtxSuffix
                            : modelName;
                        if (shouldAppendLongCtxSuffix
                            && InfoLogDedup.ShouldLog($"{kernelInfoKey}|LongCtxSuffix"))
                            LogIfPublic(config, $"[SKChatService] OpenAI 分支自动追加 {longCtxSuffix} 后缀: {modelName} → {openAiModelName}");
                        else if (openAiEnableLong && isDefaultLongContextModel
                            && InfoLogDedup.ShouldLog($"{kernelInfoKey}|LongCtxDefault"))
                            LogIfPublic(config, $"[SKChatService] OpenAI 分支跳过后缀（默认派 1M 模型）: {modelName}");
                        if (!string.IsNullOrEmpty(baseUrl))
                        {
                            var chatBaseUrl = EnsureApiVersion(baseUrl);
                            builder.AddOpenAIChatCompletion(
                                modelId: openAiModelName,
                                endpoint: new Uri(chatBaseUrl),
                                apiKey: apiKey ?? string.Empty,
                                httpClient: httpClient);
                        }
                        else
                        {
                            builder.AddOpenAIChatCompletion(
                                modelId: openAiModelName,
                                apiKey: apiKey ?? string.Empty,
                                httpClient: httpClient);
                        }
                        providerType = "TagBased";
                        kernelLocal = builder.Build();
                        chatServiceLocal = kernelLocal.GetRequiredService<IChatCompletionService>();
                        RegisterPlugins(kernelLocal);
                        if (InfoLogDedup.ShouldLog($"{kernelInfoKey}|ProviderOk|OpenAI-compat"))
                            LogIfPublic(config, "[SKChatService] provider ok (OpenAI-compat)");
                        break;
                }

                var novelAgentLocal = new Agents.NovelAgent(kernelLocal, providerType);

                if (InfoLogDedup.ShouldLog($"{kernelInfoKey}|KernelBuilt"))
                    LogIfPublic(config, "[SKChatService] Kernel 构建成功");

                return new KernelBundle(
                    kernelLocal,
                    chatServiceLocal,
                    novelAgentLocal,
                    httpClient,
                    providerType,
                    key);
            }
            catch (Exception ex)
            {
                LogIfPublic(config, $"[SKChatService] Kernel 构建失败: {ex.Message}");
                try { httpClient?.Dispose(); } catch { }
                return null;
            }
        }

        private static string BuildKernelConfigKey(UserConfiguration config, string explicitApiKey)
        {
            return string.Join(KernelKeySeparator,
                config.ProviderId ?? string.Empty,
                config.ModelId ?? string.Empty,
                config.CustomEndpoint ?? string.Empty,
                explicitApiKey ?? string.Empty,
                config.TimeoutSeconds.ToString(),
                config.EnableLongContext == true ? "L1M" : "L0");
        }

        public void SetSystemPrompt(string systemPrompt)
        {
            _chatHistory = new ChatHistory(systemPrompt);
            TM.App.Log("[SKChatService] 系统提示词已设置");
        }

        private void RegisterPlugins(Kernel kernel)
        {
            if (kernel == null) return;

            RegisterSinglePlugin(() => kernel.Plugins.AddFromObject(ServiceLocator.Get<WriterPlugin>(), "Writer"), "Writer");
            RegisterSinglePlugin(() => kernel.Plugins.AddFromObject(new SystemPlugin(), "System"), "System");
            RegisterSinglePlugin(() => kernel.Plugins.AddFromObject(new DataLookupPlugin(), "DataLookup"), "DataLookup");
            RegisterSinglePlugin(() => kernel.Plugins.AddFromObject(new DataEditPlugin(), "DataEdit"), "DataEdit");
            RegisterSinglePlugin(() => kernel.Plugins.AddFromObject(new ContentEditPlugin(), "ContentEdit"), "ContentEdit");
            RegisterSinglePlugin(() => kernel.Plugins.AddFromObject(new WorkspacePlugin(), "Workspace"), "Workspace");
            RegisterSinglePlugin(() => kernel.Plugins.AddFromObject(ServiceLocator.Get<TM.Services.Framework.AI.NovelAgent.Plugins.NovelAgentPlugin>(), "NovelAgent"), "NovelAgent");

            if (InfoLogDedup.ShouldLog($"Plugins|Count|{kernel.Plugins.Count}"))
                TM.App.Log($"[SKChatService] 已注册 {kernel.Plugins.Count} 个 Plugin");
        }

        private static void RegisterSinglePlugin(Action register, string name)
        {
            try
            {
                register();
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SKChatService] Plugin '{name}' 注册跳过: {ex.Message}");
            }
        }

        #endregion

        #region ChatMode 管理

        public TM.Framework.UI.Workspace.RightPanel.Modes.ChatMode CurrentMode
        {
            get => _currentMode;
            set
            {
                if (_currentMode != value)
                {
                    _currentMode = value;
                    PlanModeFilter.IsEnabled = ChatModeSettings.RequiresFunctionConfirmation(value);
                    var sessionId = Sessions.GetCurrentSessionIdOrNull();
                    if (!string.IsNullOrEmpty(sessionId))
                    {
                        Sessions.UpdateSessionMode(sessionId, ((int)value).ToString());
                    }
                    TM.App.Log($"[SKChatService] 切换模式: {value}, Filter={PlanModeFilter.IsEnabled}");
                }
            }
        }

        public void BeginDraftSession()
        {
            Sessions.ResetCurrentSession();
            _chatHistory = new ChatHistory();
            _turnIndex = 0;
            _isSessionCompressed = false;
            TM.App.Log("[SKChatService] 已进入草稿会话状态");
        }

        public void DeleteCurrentSession()
        {
            var sessionId = Sessions.GetCurrentSessionIdOrNull();
            if (string.IsNullOrEmpty(sessionId))
            {
                _chatHistory = new ChatHistory();
                _turnIndex = 0;
                _isSessionCompressed = false;
                return;
            }

            Sessions.DeleteSession(sessionId);
            Sessions.ResetCurrentSession();
            _chatHistory = new ChatHistory();
            _turnIndex = 0;
            _isSessionCompressed = false;
            TM.App.Log($"[SKChatService] 当前会话已删除: {sessionId}");
        }

        public PromptExecutionSettings GetCurrentModeSettings(int? overrideMaxTokens = null, Microsoft.SemanticKernel.Kernel? currentKernel = null)
        {
            var settings = ChatModeSettings.GetExecutionSettings(_currentMode, _chatHistory, overrideMaxTokens: overrideMaxTokens);

            var effectiveKernel = currentKernel ?? _kernel;

            if (_currentRunType == RunType.Chat)
            {
                settings.FunctionChoiceBehavior = null;
            }
            else if (_forcedFunctionNames is { Length: > 0 } && settings.FunctionChoiceBehavior != null && effectiveKernel != null)
            {
                var allowed = effectiveKernel.Plugins
                    .Where(p => string.Equals(p.Name, "DataLookup", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(p => p)
                    .Where(f => _forcedFunctionNames.Contains(f.Name, StringComparer.OrdinalIgnoreCase))
                    .ToList();
                if (allowed.Count > 0)
                {
                    settings.FunctionChoiceBehavior = FunctionChoiceBehavior.Required(allowed, autoInvoke: true);
                }
            }

            try
            {
                var config = AI.GetActiveConfiguration();
                if (config != null && settings.FunctionChoiceBehavior != null)
                {
                    var toolsResolved = CapabilityServices.DefaultResolver.Resolve(
                        providerId: config.ProviderId,
                        modelId: config.ModelId,
                        endpoint: config.CustomEndpoint,
                        userHint: new UserCapabilityHint
                        {
                            IsCompatibilityFallback = AI.IsCompatibilityFallbackEnabled(config.ProviderId, config.ModelId),
                        });
                    if (toolsResolved.IsCompatibilityFallback || !toolsResolved.Tools.SupportsNativeToolUse)
                    {
                        LogIfPublic(config, $"[SKChatService] 兼容回退已启用，禁用函数调用: {config.ProviderId}/{config.ModelId}");
                        settings.FunctionChoiceBehavior = null;
                    }
                }
            }
            catch (Exception ex)
            {
                LogIfPublic(null, $"[SKChatService] 检查函数调用支持失败: {ex.Message}");
            }

            return settings;
        }

        internal bool IsThinkingInjectionSuppressed(UserConfiguration? config)
            => _skipThinkingInjection;

        internal string GetCurrentProviderType() => _currentProviderType;

        #endregion

        #region Inner Types

        private sealed class CherryStudioDelegatingHandler : DelegatingHandler
        {
            private const string ChromeUA =
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36";

            private readonly string _apiKey;

            public CherryStudioDelegatingHandler(string apiKey, HttpMessageHandler inner) : base(inner)
            {
                _apiKey = apiKey;
            }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                request.Headers.Remove("User-Agent");
                request.Headers.TryAddWithoutValidation("User-Agent", ChromeUA);

                if (!request.Headers.Contains("HTTP-Referer"))
                    request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://cherry-ai.com");

                if (!request.Headers.Contains("X-Title"))
                    request.Headers.TryAddWithoutValidation("X-Title", "Cherry Studio");

                if (!string.IsNullOrWhiteSpace(_apiKey) && !request.Headers.Contains("X-Api-Key"))
                    request.Headers.TryAddWithoutValidation("X-Api-Key", _apiKey);

                await TryInjectExtensionDataIntoBodyAsync(request, cancellationToken).ConfigureAwait(false);

                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }

            private static async Task TryInjectExtensionDataIntoBodyAsync(
                HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            {
                var ext = ThinkingRequestAmbientContext.CurrentExtensionData;
                if (ext == null || ext.Count == 0) return;
                if (request.Content == null) return;

                var contentType = request.Content.Headers.ContentType?.MediaType;
                if (string.IsNullOrEmpty(contentType)
                    || !contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
                    return;

                string body;
                try
                {
                    body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ExtensionData透传] 读取请求 body 失败（不阻断）: {ex.Message}");
                    return;
                }

                if (string.IsNullOrWhiteSpace(body)) return;

                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                        return;

                    var existingKeys = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var prop in doc.RootElement.EnumerateObject())
                        existingKeys.Add(prop.Name);

                    var pendingAdditions = new List<KeyValuePair<string, object>>();
                    foreach (var kv in ext)
                    {
                        if (string.IsNullOrEmpty(kv.Key)) continue;
                        if (existingKeys.Contains(kv.Key)) continue;
                        pendingAdditions.Add(kv);
                    }
                    if (pendingAdditions.Count == 0) return;

                    using var ms = new System.IO.MemoryStream();
                    using (var writer = new System.Text.Json.Utf8JsonWriter(ms))
                    {
                        writer.WriteStartObject();
                        foreach (var prop in doc.RootElement.EnumerateObject())
                            prop.WriteTo(writer);
                        foreach (var kv in pendingAdditions)
                        {
                            writer.WritePropertyName(kv.Key);
                            System.Text.Json.JsonSerializer.Serialize(writer, kv.Value);
                        }
                        writer.WriteEndObject();
                    }

                    var newBytes = ms.ToArray();
                    var newContent = new ByteArrayContent(newBytes);
                    newContent.Headers.ContentType = request.Content.Headers.ContentType;
                    request.Content.Dispose();
                    request.Content = newContent;

                    if (InfoLogDedup.ShouldLog($"ExtensionData透传|{string.Join(",", pendingAdditions.Select(p => p.Key))}"))
                        TM.App.Log($"[ExtensionData透传] 已注入 {pendingAdditions.Count} 个字段: {string.Join(",", pendingAdditions.Select(p => p.Key))}");
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[ExtensionData透传] 合并失败（保留原 body）: {ex.Message}");
                }
            }
        }

        #endregion
    }
}
