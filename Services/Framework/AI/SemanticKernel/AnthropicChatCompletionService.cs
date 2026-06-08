using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using TM.Framework.SystemSettings.Proxy.Services;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public class AnthropicChatCompletionService : IChatCompletionService, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;
        private bool _disposed;
        private readonly string _modelId;
        private readonly string _originalModelId;
        private readonly string _endpoint;
        private readonly string _apiKey;
        private readonly bool _enableLongContext;
        private readonly string? _providerId;
        private readonly JsonSerializerOptions _jsonOptions;

        private static readonly Regex ClaudeMajorRegex = new(@"claude[/\-](\d+)", RegexOptions.Compiled);
        private static readonly Regex ClaudeMinorRegex = new(@"claude[/\-]3[.\-](\d+)", RegexOptions.Compiled);

        private static void DebugLogOnce(string key, Exception ex)
            => TM.Framework.Common.Helpers.InfoLogDedup.DebugLogOnce(key, ex, "AnthropicService");

        public IReadOnlyDictionary<string, object?> Attributes { get; }

        public AnthropicChatCompletionService(
            string apiKey,
            string modelId,
            string? baseUrl = null,
            int timeoutSeconds = 0,
            HttpClient? httpClient = null,
            bool enableLongContext = false,
            string? providerId = null,
            string? configModelIdForCache = null)
        {
            _originalModelId = !string.IsNullOrEmpty(configModelIdForCache)
                ? configModelIdForCache!
                : (modelId ?? string.Empty);
            _modelId = StripLongContextSuffix(modelId ?? string.Empty);
            _enableLongContext = enableLongContext;
            _providerId = providerId;
            _apiKey = apiKey;

            var root = string.IsNullOrWhiteSpace(baseUrl)
                ? "https://api.anthropic.com/v1"
                : baseUrl!;

            _endpoint = NormalizeEndpoint(root);

            string timeoutSource;
            if (httpClient != null)
            {
                _httpClient = httpClient;
                _ownsHttpClient = false;
                timeoutSource = $"外部传入（{httpClient.Timeout}）";
            }
            else
            {
                TimeSpan httpTimeout;
                if (IsLocalEndpoint(root))
                {
                    httpTimeout = System.Threading.Timeout.InfiniteTimeSpan;
                    timeoutSource = "无限（本地端点）";
                }
                else if (timeoutSeconds > 0)
                {
                    httpTimeout = TimeSpan.FromSeconds(timeoutSeconds);
                    timeoutSource = $"{timeoutSeconds}秒（用户配置）";
                }
                else
                {
                    httpTimeout = TimeSpan.FromMinutes(5);
                    timeoutSource = "5分钟（默认兜底）";
                }
                _httpClient = ServiceLocator.Get<ProxyService>().CreateHttpClient(httpTimeout);
                _ownsHttpClient = true;
            }

            _jsonOptions = JsonHelper.Lenient;

            Attributes = new Dictionary<string, object?>
            {
                { "ModelId", _modelId },
                { "Provider", "Anthropic" }
            };

            var endpointForLog = TM.Services.Framework.AI.Core.TianmingProviderIdentity.IsTianmingPrivate(_providerId) ? TM.Services.Framework.AI.Core.TianmingProviderIdentity.MaskedEndpointLabel : _endpoint;
            TM.App.Log($"[AnthropicService] 初始化: Model={_modelId}" +
                (string.Equals(_modelId, _originalModelId, StringComparison.Ordinal) ? string.Empty : $" (cache key={_originalModelId})") +
                $", Endpoint={endpointForLog}, EnableLongContext={_enableLongContext}, Timeout={timeoutSource}");
        }

        private static string StripLongContextSuffix(string modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return modelId;
            if (modelId.EndsWith("[1m]", StringComparison.OrdinalIgnoreCase))
                return modelId.Substring(0, modelId.Length - 4);
            if (modelId.EndsWith(":extended", StringComparison.OrdinalIgnoreCase))
                return modelId.Substring(0, modelId.Length - 9);
            return modelId;
        }

        private static bool IsLocalEndpoint(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            var host = uri.Host.ToLowerInvariant();
            return host == "localhost"
                || host == "127.0.0.1"
                || host == "::1"
                || host.StartsWith("192.168.", StringComparison.Ordinal)
                || host.StartsWith("10.", StringComparison.Ordinal)
                || (host.StartsWith("172.", StringComparison.Ordinal) && System.Net.IPAddress.TryParse(host, out var ip)
                    && ip.GetAddressBytes() is { } b && b[0] == 172 && b[1] >= 16 && b[1] <= 31);
        }

        public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (messages, systemMessage) = ConvertChatHistory(chatHistory);

                var maxTokens = GetMaxTokens(executionSettings);

                var body = new Dictionary<string, object?>
                {
                    ["model"] = _modelId,
                    ["max_tokens"] = maxTokens,
                    ["messages"] = messages
                };

                if (!string.IsNullOrWhiteSpace(systemMessage))
                {
                    body["system"] = systemMessage;
                }

                InjectThinkingIfSupported(body, maxTokens);

                using var request = BuildHttpRequest(body, stream: false);

                TM.App.Log($"[AnthropicService] 发送请求: {messages.Count} 条消息, MaxTokens={maxTokens}, HasSystem={!string.IsNullOrWhiteSpace(systemMessage)}");

                using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    TM.App.Log($"[AnthropicService] 错误 ({(int)response.StatusCode}): {responseJson}");
                    throw new System.Net.Http.HttpRequestException(
                        $"Anthropic API error ({(int)response.StatusCode}): {responseJson}",
                        null,
                        response.StatusCode);
                }

                var content = TryExtractContent(responseJson);
                var stopReason = TryExtractStopReason(responseJson);
                var (inputTokens, outputTokens) = TryExtractUsage(responseJson);
                ChatModeSettings.SyncLastFinishReason(stopReason);

                TM.App.Log($"[AnthropicService] 收到响应: {content.Length} 字符, stop_reason={stopReason ?? "(null)"}, usage=in/{inputTokens} out/{outputTokens}");

                var msgMeta = new Dictionary<string, object?>();
                if (!string.IsNullOrEmpty(stopReason)) msgMeta["FinishReason"] = stopReason;
                if (inputTokens > 0 || outputTokens > 0)
                {
                    msgMeta["Usage"] = new Dictionary<string, int>
                    {
                        ["InputTokens"] = inputTokens,
                        ["OutputTokens"] = outputTokens
                    };
                }
                return new List<ChatMessageContent>
                {
                    new ChatMessageContent(AuthorRole.Assistant, content, metadata: msgMeta.Count > 0 ? msgMeta : null)
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AnthropicService] 错误: {ex.Message}");
                throw;
            }
        }

        public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var (messages, systemMessage) = ConvertChatHistory(chatHistory);

            var maxTokens = GetMaxTokens(executionSettings);

            var body = new Dictionary<string, object?>
            {
                ["model"] = _modelId,
                ["max_tokens"] = maxTokens,
                ["messages"] = messages,
                ["stream"] = true
            };

            if (!string.IsNullOrWhiteSpace(systemMessage))
            {
                body["system"] = systemMessage;
            }

            InjectThinkingIfSupported(body, maxTokens);

            using var request = BuildHttpRequest(body, stream: true);

            TM.App.Log($"[AnthropicService] 流式请求: {messages.Count} 条消息, MaxTokens={maxTokens}, HasSystem={!string.IsNullOrWhiteSpace(systemMessage)}");

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                TM.App.Log($"[AnthropicService] 流式请求失败 ({(int)response.StatusCode}): {errorBody}");
                throw new System.Net.Http.HttpRequestException(
                    $"Anthropic API error ({(int)response.StatusCode}): {errorBody}",
                    null,
                    response.StatusCode);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            int accInputTokens = 0;
            int accOutputTokens = 0;
            var thinkingBuilder = new StringBuilder();
            long? thinkingStartTicks = null;
            long? thinkingEndTicks = null;

            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var delta = TryExtractStreamDeltaWithThinking(line);

                if (!string.IsNullOrEmpty(delta.Content))
                {
                    if (thinkingStartTicks.HasValue && !thinkingEndTicks.HasValue)
                        thinkingEndTicks = DateTime.UtcNow.Ticks;

                    yield return new StreamingChatMessageContent(AuthorRole.Assistant, delta.Content);
                }

                if (!string.IsNullOrEmpty(delta.Thinking))
                {
                    thinkingStartTicks ??= DateTime.UtcNow.Ticks;
                    thinkingBuilder.Append(delta.Thinking);

                    var metadata = new Dictionary<string, object?>
                    {
                        ["Thinking"] = delta.Thinking
                    };

                    var thinkingChunk = new StreamingChatMessageContent(AuthorRole.Assistant, content: string.Empty, metadata: metadata);
                    yield return thinkingChunk;
                }

                if (delta.InputTokens > 0) accInputTokens = delta.InputTokens;
                if (delta.OutputTokens > 0) accOutputTokens = delta.OutputTokens;

                if (!string.IsNullOrEmpty(delta.StopReason))
                {
                    ChatModeSettings.SyncLastFinishReason(delta.StopReason);

                    if (thinkingStartTicks.HasValue && !thinkingEndTicks.HasValue)
                        thinkingEndTicks = DateTime.UtcNow.Ticks;

                    var frMeta = new Dictionary<string, object?> { ["FinishReason"] = delta.StopReason };

                    if (accInputTokens > 0 || accOutputTokens > 0)
                    {
                        frMeta["Usage"] = new Dictionary<string, int>
                        {
                            ["InputTokens"] = accInputTokens,
                            ["OutputTokens"] = accOutputTokens
                        };
                    }
                    if (thinkingBuilder.Length > 0)
                    {
                        frMeta["ThinkingFull"] = thinkingBuilder.ToString();
                        if (thinkingStartTicks.HasValue && thinkingEndTicks.HasValue)
                        {
                            var durationMs = (int)TimeSpan.FromTicks(thinkingEndTicks.Value - thinkingStartTicks.Value).TotalMilliseconds;
                            frMeta["ThinkingMs"] = durationMs > 0 ? durationMs : 0;
                        }
                    }

                    yield return new StreamingChatMessageContent(AuthorRole.Assistant, content: string.Empty, metadata: frMeta);
                }
            }
        }

        #region 辅助方法

        private int GetMaxTokens(PromptExecutionSettings? executionSettings)
        {
            if (executionSettings is Microsoft.SemanticKernel.Connectors.OpenAI.OpenAIPromptExecutionSettings oai
                && oai.MaxTokens.HasValue && oai.MaxTokens.Value > 0)
            {
                var v = oai.MaxTokens.Value;
                ChatModeSettings.SyncLastUsedMaxTokens(v);
                return v;
            }

            if (executionSettings?.ExtensionData != null)
            {
                try
                {
                    if (executionSettings.ExtensionData.TryGetValue("max_tokens", out var value) && value != null)
                    {
                        var parsed = value switch
                        {
                            int i when i > 0 => i,
                            long l when l > 0 => (int)l,
                            double d when d > 0 => (int)d,
                            string s when int.TryParse(s, out var p) && p > 0 => p,
                            _ => 0
                        };
                        if (parsed > 0)
                        {
                            ChatModeSettings.SyncLastUsedMaxTokens(parsed);
                            return parsed;
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[AnthropicService] 解析 max_tokens 失败，回退到安全默认值: {ex.Message}");
                }
            }

            ChatModeSettings.SyncLastUsedMaxTokens(4096);
            return 4096;
        }

        private HttpRequestMessage BuildHttpRequest(Dictionary<string, object?> body, bool stream)
        {
            var json = JsonSerializer.Serialize(body, _jsonOptions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = content
            };

            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Headers.Add("User-Agent", "TianMing-SK-Anthropic/1.0");

            if (_enableLongContext
                && !ChatModeSettings.IsUnsupportedParam(_providerId, _endpoint, _originalModelId, "long_context"))
            {
                request.Headers.Add("anthropic-beta", "context-1m-2025-08-07");
            }

            if (stream)
            {
                request.Headers.Accept.ParseAdd("text/event-stream");
            }

            return request;
        }

        private (List<object> Messages, string? SystemMessage) ConvertChatHistory(ChatHistory chatHistory)
        {
            var messages = new List<object>();
            var systemParts = new List<string>();

            foreach (var msg in chatHistory)
            {
                if (msg.Role == AuthorRole.System)
                {
                    if (!string.IsNullOrWhiteSpace(msg.Content))
                    {
                        systemParts.Add(msg.Content);
                    }
                    continue;
                }

                string role;
                if (msg.Role == AuthorRole.User)
                {
                    role = "user";
                }
                else if (msg.Role == AuthorRole.Assistant)
                {
                    role = "assistant";
                }
                else
                {
                    role = "user";
                }

                var text = msg.Content ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                messages.Add(new
                {
                    role,
                    content = new object[]
                    {
                        new { type = "text", text }
                    }
                });
            }

            if (messages.Count == 0)
            {
                messages.Add(new
                {
                    role = "user",
                    content = new object[] { new { type = "text", text = "Hello" } }
                });
            }

            var systemMessage = systemParts.Count > 0 ? string.Join("\n\n", systemParts) : null;
            return (messages, systemMessage);
        }

        private void InjectThinkingIfSupported(Dictionary<string, object?> body, int maxTokens)
        {
            if (!SupportsExtendedThinking(_modelId))
                return;

            const int minMaxTokensForThinking = 2048;
            if (maxTokens < minMaxTokensForThinking)
                return;

            var budget = Math.Min((int)(maxTokens * 0.75), 32000);

            body["thinking"] = new Dictionary<string, object>
            {
                ["type"] = "enabled",
                ["budget_tokens"] = budget
            };
        }

        private static bool SupportsExtendedThinking(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
                return false;

            var lower = modelId.ToLowerInvariant();
            if (!lower.Contains("claude")) return false;

            var majorMatch = ClaudeMajorRegex.Match(lower);
            if (!majorMatch.Success) return false;

            if (!int.TryParse(majorMatch.Groups[1].Value, out var major)) return false;

            if (major >= 4) return true;

            if (major == 3)
            {
                var minorMatch = ClaudeMinorRegex.Match(lower);
                if (!minorMatch.Success || !int.TryParse(minorMatch.Groups[1].Value, out var minor))
                    return false;

                if (minor >= 7) return true;

                if (minor >= 5 && lower.Contains("sonnet")) return true;
            }

            return false;
        }

        private static string NormalizeEndpoint(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                return string.Empty;
            }

            var trimmed = endpoint.Trim();
            var lower = trimmed.ToLowerInvariant();

            if (lower.Contains("/v1/messages"))
            {
                return trimmed;
            }

            if (lower.EndsWith("/v1", StringComparison.Ordinal) || lower.EndsWith("/v1/", StringComparison.Ordinal))
            {
                var root = trimmed.TrimEnd('/');
                return root + "/messages";
            }

            return trimmed;
        }

        private static string TryExtractStopReason(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson)) return string.Empty;
            try
            {
                using var doc = JsonDocument.Parse(responseJson, new JsonDocumentOptions { AllowTrailingCommas = true });
                if (doc.RootElement.TryGetProperty("stop_reason", out var p))
                    return p.GetString() ?? string.Empty;
            }
            catch { }
            return string.Empty;
        }

        private static (int InputTokens, int OutputTokens) TryExtractUsage(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson)) return (0, 0);
            try
            {
                using var doc = JsonDocument.Parse(responseJson, new JsonDocumentOptions { AllowTrailingCommas = true });
                if (doc.RootElement.TryGetProperty("usage", out var usage)
                    && usage.ValueKind == JsonValueKind.Object)
                {
                    int inTokens = 0, outTokens = 0;
                    if (usage.TryGetProperty("input_tokens", out var i) && i.ValueKind == JsonValueKind.Number)
                        i.TryGetInt32(out inTokens);
                    if (usage.TryGetProperty("output_tokens", out var o) && o.ValueKind == JsonValueKind.Number)
                        o.TryGetInt32(out outTokens);
                    return (inTokens, outTokens);
                }
            }
            catch { }
            return (0, 0);
        }

        private string TryExtractContent(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return string.Empty;
            }

            try
            {
                using var doc = JsonDocument.Parse(responseJson, new JsonDocumentOptions { AllowTrailingCommas = true });
                var root = doc.RootElement;

                if (root.TryGetProperty("content", out var contentArray) &&
                    contentArray.ValueKind == JsonValueKind.Array &&
                    contentArray.GetArrayLength() > 0)
                {
                    var sb = new StringBuilder();
                    foreach (var item in contentArray.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object &&
                            item.TryGetProperty("text", out var textProp))
                        {
                            sb.Append(textProp.GetString());
                        }
                    }

                    var merged = sb.ToString();
                    if (!string.IsNullOrEmpty(merged))
                    {
                        return merged;
                    }
                }

                return string.Empty;
            }
            catch (JsonException ex)
            {
                DebugLogOnce(nameof(TryExtractContent), ex);
                return responseJson;
            }
        }

        private readonly struct StreamDelta
        {
            public StreamDelta(string? thinking, string? content, string? stopReason = null, int inputTokens = 0, int outputTokens = 0)
            {
                Thinking = thinking;
                Content = content;
                StopReason = stopReason;
                InputTokens = inputTokens;
                OutputTokens = outputTokens;
            }

            public string? Thinking { get; }
            public string? Content { get; }
            public string? StopReason { get; }
            public int InputTokens { get; }
            public int OutputTokens { get; }
        }

        private StreamDelta TryExtractStreamDeltaWithThinking(string sseLine)
        {
            if (string.IsNullOrWhiteSpace(sseLine))
            {
                return new StreamDelta(null, null);
            }

            var trimmed = sseLine.Trim();
            if (trimmed.StartsWith("data: ", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(6);
            }

            if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "[DONE]")
            {
                return new StreamDelta(null, null);
            }

            try
            {
                using var doc = JsonDocument.Parse(trimmed, new JsonDocumentOptions { AllowTrailingCommas = true });
                var root = doc.RootElement;

                if (root.TryGetProperty("type", out var typeProp))
                {
                    var eventType = typeProp.GetString();

                    if (eventType == "content_block_delta" && root.TryGetProperty("delta", out var delta))
                    {
                        if (delta.TryGetProperty("type", out var deltaTypeProp))
                        {
                            var deltaType = deltaTypeProp.GetString();

                            if (deltaType == "thinking_delta" && delta.TryGetProperty("thinking", out var thinkingProp))
                            {
                                return new StreamDelta(thinkingProp.GetString(), null);
                            }

                            if (deltaType == "text_delta" && delta.TryGetProperty("text", out var textProp))
                            {
                                return new StreamDelta(null, textProp.GetString());
                            }
                        }
                    }

                    if (eventType == "message_start"
                        && root.TryGetProperty("message", out var msgElem)
                        && msgElem.TryGetProperty("usage", out var msUsage)
                        && msUsage.ValueKind == JsonValueKind.Object)
                    {
                        int inTokens = 0;
                        if (msUsage.TryGetProperty("input_tokens", out var ip) && ip.ValueKind == JsonValueKind.Number)
                            ip.TryGetInt32(out inTokens);
                        if (inTokens > 0)
                            return new StreamDelta(null, null, stopReason: null, inputTokens: inTokens, outputTokens: 0);
                    }

                    if (eventType == "message_delta")
                    {
                        string? sr = null;
                        int outTokens = 0;

                        if (root.TryGetProperty("delta", out var msgDelta)
                            && msgDelta.TryGetProperty("stop_reason", out var stopReasonProp))
                        {
                            sr = stopReasonProp.GetString();
                        }

                        if (root.TryGetProperty("usage", out var mdUsage)
                            && mdUsage.ValueKind == JsonValueKind.Object
                            && mdUsage.TryGetProperty("output_tokens", out var op)
                            && op.ValueKind == JsonValueKind.Number)
                        {
                            op.TryGetInt32(out outTokens);
                        }

                        if (!string.IsNullOrEmpty(sr) || outTokens > 0)
                            return new StreamDelta(null, null, stopReason: sr, inputTokens: 0, outputTokens: outTokens);
                    }
                }
            }
            catch (JsonException ex)
            {
                DebugLogOnce(nameof(TryExtractStreamDeltaWithThinking), ex);
            }

            return new StreamDelta(null, null);
        }

        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                if (_ownsHttpClient)
                {
                    _httpClient.Dispose();
                }
            }
            GC.SuppressFinalize(this);
        }
    }
}
