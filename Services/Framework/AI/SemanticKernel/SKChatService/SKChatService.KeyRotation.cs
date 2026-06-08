#pragma warning disable SKEXP0130

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.WritingConfig;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public partial class SKChatService
    {
        private sealed class AlreadyNotifiedApiException : Exception
        {
            public AlreadyNotifiedApiException(string message, Exception? inner = null)
                : base(message, inner) { }
        }

        #region 密钥轮询 + 统一错误处理

        private async Task<T> InvokeApiWithRotationAsync<T>(
            Func<KernelBundle, CancellationToken, Task<T>> apiCall,
            CancellationToken ct,
            int maxKeyRetries = 0,
            UserConfiguration? config = null,
            bool allowFailover = true,
            IProgress<string>? progress = null)
        {
            config ??= AI.GetActiveConfiguration();
            if (config == null) throw new InvalidOperationException("未配置 AI 模型");
            CheckDirectKernelRecovery();

            if (maxKeyRetries <= 0)
            {
                maxKeyRetries = config.RetryCount > 0 ? config.RetryCount : 2;
            }

            var rotation = ServiceLocator.Get<TM.Services.Framework.AI.Core.ApiKeyRotationService>();
            var excludeKeyIds = new HashSet<string>();
            var rateLimitExcludeKeyIds = new HashSet<string>();
            var failedKeyDetails = new List<string>();
            var rateLimitedCount = 0;
            var serverErrorCount = 0;
            const int maxRateLimitRounds = 3;
            var rateLimitRound = 0;
            var allFailuresAreRateLimited = true;

            var poolSize = rotation.GetPoolStatus(config.ProviderId)?.ActiveKeys ?? maxKeyRetries + 1;
            var effectiveMaxRetries = Math.Max(poolSize - 1, maxKeyRetries);
            var allKeysExhausted = false;

            KeySelection? stickyRetrySelection = null;

            for (int attempt = 0; attempt <= effectiveMaxRetries; attempt++)
            {
                KeySelection? selection;
                bool isStickyRetry;
                if (stickyRetrySelection != null)
                {
                    selection = stickyRetrySelection;
                    stickyRetrySelection = null;
                    isStickyRetry = true;
                    LogIfPublic(config, $"[SKChatService] sticky 同 key 重试: key={selection.KeyId[..Math.Min(10, selection.KeyId.Length)]}...");
                }
                else
                {
                    var combinedExclude = new HashSet<string>(excludeKeyIds);
                    combinedExclude.UnionWith(rateLimitExcludeKeyIds);
                    selection = rotation.GetNextKey(config.ProviderId, combinedExclude);
                    isStickyRetry = false;
                }
                if (selection == null)
                {
                    if (rateLimitExcludeKeyIds.Count > 0 && rateLimitRound < maxRateLimitRounds)
                    {
                        rateLimitRound++;
                        rateLimitedCount++;

                        var minRemaining = rotation.GetMinRemainingCooldownSeconds(config.ProviderId, rateLimitExcludeKeyIds);
                        TimeSpan delay;
                        string delaySource;
                        if (minRemaining.HasValue && minRemaining.Value > 0)
                        {
                            delay = TimeSpan.FromSeconds(Math.Min(minRemaining.Value + 1, RateLimitBackoffMaxSeconds * 4));
                            delaySource = $"池中最早恢复={minRemaining.Value}s";
                        }
                        else
                        {
                            delay = GetExponentialBackoff(rateLimitedCount - 1, RateLimitBackoffBaseSeconds, RateLimitBackoffMaxSeconds);
                            delaySource = $"指数退避 {delay.TotalSeconds:F0}s";
                        }

                        var rateLimitMsg = $"所有密钥均被限流，{delay.TotalSeconds:F0}s 后自动恢复（第 {rateLimitRound}/{maxRateLimitRounds} 轮）";
                        GenerationProgressHub.Report(rateLimitMsg);
                        GlobalToast.Info("端点限速", rateLimitMsg);
                        LogIfPublic(config, $"[SKChatService] 429 端点级等待: 第{rateLimitRound}轮, {delaySource} | {GetConfigSummary(config)}");
                        try { await Task.Delay(delay, ct).ConfigureAwait(false); }
                        catch (OperationCanceledException) { throw; }
                        rateLimitExcludeKeyIds.Clear();
                        attempt--;
                        continue;
                    }
                    allKeysExhausted = true;
                    break;
                }

                var roundBundle = EnsureKernelInitialized(config, selection.ApiKey);
                if (roundBundle == null)
                {
                    LogIfPublic(config, $"[SKChatService] 构建 Kernel 失败，跳过当前 key 重试 | {GetConfigSummary(config)}");
                    excludeKeyIds.Add(selection.KeyId);
                    continue;
                }

                TM.Services.Framework.AI.RateLimiting.ApiRateLimiter.ReleaseHandle releaseHandle;
                try
                {
                    var limiter = ServiceLocator.Get<TM.Services.Framework.AI.RateLimiting.ApiRateLimiter>();
                    releaseHandle = limiter.Acquire(
                        config.ProviderId,
                        rpmLimit: config.RateLimitRPM,
                        tpmLimit: config.RateLimitTPM,
                        maxConcurrency: config.MaxConcurrency,
                        estimatedTokens: 0);
                }
                catch (TM.Services.Framework.AI.RateLimiting.LocalRateLimitException rlex)
                {
                    LogIfPublic(config, $"[SKChatService] 本地速率限制触发: {rlex.Message}");
                    GenerationProgressHub.Report($"本地限流：{rlex.Message}，等待重试...");
                    try { await Task.Delay(Math.Min(rlex.WaitMs, 5000), ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                    attempt--;
                    continue;
                }

                try
                {
                    try
                    {
                        var result = await apiCall(roundBundle, ct).ConfigureAwait(false);

                        if (result is AdaptiveResult adaptive
                            && !string.IsNullOrEmpty(adaptive.Content)
                            && adaptive.Content.StartsWith("[错误]", StringComparison.Ordinal))
                        {
                            var adaptiveErrKind = ClassifyAdaptiveError(adaptive.Content);
                            var keyShort = selection.KeyId[..Math.Min(10, selection.KeyId.Length)];

                            if (adaptiveErrKind == AdaptiveErrorKind.MaxDurationReached
                                || adaptiveErrKind == AdaptiveErrorKind.ServiceUnconfigured)
                            {
                                LogIfPublic(config, $"[SKChatService] AdaptiveResult 终端错误（{adaptiveErrKind}），不再重试: key={keyShort}...");
                                return result;
                            }

                            serverErrorCount++;
                            allFailuresAreRateLimited = false;

                            var adaptiveDelay = GetExponentialBackoff(serverErrorCount - 1, ServerErrorBackoffBaseSeconds, ServerErrorBackoffMaxSeconds);
                            var adaptiveKeyLabel = IsTianmingPrivateProvider(config.ProviderId)
                                ? "内置密钥"
                                : !string.IsNullOrWhiteSpace(selection.Remark)
                                ? selection.Remark
                                : selection.ApiKey.Length > 10 ? selection.ApiKey[..10] + "..." : selection.ApiKey;

                            if (!isStickyRetry)
                            {
                                stickyRetrySelection = selection;
                                LogIfPublic(config, $"[SKChatService] AdaptiveError({adaptiveErrKind})，{adaptiveDelay.TotalSeconds:F0}s 后同 key 重试（不计入 key 健康）: key={keyShort}... | {GetConfigSummary(config)}");
                                GenerationProgressHub.Report($"端点{adaptiveErrKind}，{adaptiveDelay.TotalSeconds:F0}秒后同密钥重试...");
                                try { await Task.Delay(adaptiveDelay, ct).ConfigureAwait(false); }
                                catch (OperationCanceledException) { throw; }
                                attempt--;
                                continue;
                            }

                            if (!IsTianmingPrivateProvider(config.ProviderId))
                                failedKeyDetails.Add($"[{adaptiveKeyLabel}] → {adaptiveErrKind}: {TrimAdaptiveContent(adaptive.Content)}");
                            LogIfPublic(config, $"[SKChatService] AdaptiveError({adaptiveErrKind})，{adaptiveDelay.TotalSeconds:F0}s 后换 key（不计入 key 健康）: key={keyShort}... | {GetConfigSummary(config)}");
                            GenerationProgressHub.Report($"端点{adaptiveErrKind}，{adaptiveDelay.TotalSeconds:F0}秒后切换密钥...");
                            try { await Task.Delay(adaptiveDelay, ct).ConfigureAwait(false); }
                            catch (OperationCanceledException) { throw; }
                            excludeKeyIds.Add(selection.KeyId);
                            continue;
                        }

                        rotation.ReportKeyResult(config.ProviderId, selection.KeyId, TM.Services.Framework.AI.Core.KeyUseResult.Success);
                        return result;
                    }
                    finally
                    {
                        releaseHandle.Dispose();
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    if (IsConnectionError(ex) && _useDirectKernel)
                    {
                        _useDirectKernel = false;
                        _directKernelDisabledAt = DateTime.UtcNow;
                        InvalidateAllBundles();
                        LogIfPublic(config, $"[SKChatService] 直连失败（Rotation），切换代理重试（{DirectKernelRetryAfter.TotalMinutes:F0} 分钟后尝试恢复直连）: {ex.Message} | {GetConfigSummary(config)}");
                        GenerationProgressHub.Report("直连失败，切换代理后重试...");
                        attempt--;
                        continue;
                    }

                    var (useResult, rawMessage) = ClassifyException(ex);
                    if (ChatModeSettings.ShouldAttemptReasoningFallback(ex, useResult)
                        && ChatModeSettings.TryRecordReasoningCapForFailure(config, out var capFamily, out var fromDesc, out var toDesc))
                    {
                        GenerationProgressHub.Report($"推理参数 {fromDesc} 不兼容，已自动降级到 {toDesc} 后重试...");
                        LogIfPublic(config, $"[SKChatService] Rotation 推理参数自动降级: family={capFamily}, {fromDesc} -> {toDesc}, model={config.ModelId}, error={rawMessage}");
                        attempt--;
                        continue;
                    }
                    rotation.ReportKeyResult(config.ProviderId, selection.KeyId, useResult, rawMessage);

                    var keyLabel = IsTianmingPrivateProvider(config.ProviderId)
                        ? "内置密钥"
                        : !string.IsNullOrWhiteSpace(selection.Remark)
                        ? selection.Remark
                        : selection.ApiKey.Length > 10 ? selection.ApiKey[..10] + "..." : selection.ApiKey;
                    if (useResult != TM.Services.Framework.AI.Core.KeyUseResult.RateLimited && !IsTianmingPrivateProvider(config.ProviderId))
                        failedKeyDetails.Add($"[{keyLabel}] → {rawMessage}");

                    if (IsThinkingNotSupportedError(ex) && !_skipThinkingInjection)
                    {
                        _skipThinkingInjection = true;
                        ResetActiveThinkingParams(config);
                        GlobalToast.Error("推理参数不支持", "当前模型不支持思考/推理参数，已记录兼容性降级");
                        LogIfPublic(config, $"[SKChatService] Rotation: 思考参数不支持，已写入兼容性 cap: {ex.Message}");
                        GenerationProgressHub.Report("当前模型不支持思考参数，已记录兼容性降级...");
                        throw new AlreadyNotifiedApiException(rawMessage, ex);
                    }

                    if (useResult == TM.Services.Framework.AI.Core.KeyUseResult.NetworkError)
                    {
                        GenerationProgressHub.Report("网络连接失败，请检查网络或代理设置...");
                        NotifyRealError("网络连接失败", rawMessage, config.ProviderId);
                        throw new AlreadyNotifiedApiException(rawMessage, ex);
                    }

                    if (useResult == TM.Services.Framework.AI.Core.KeyUseResult.ModelNotFound)
                    {
                        GenerationProgressHub.Report("模型不存在，请检查模型名称或端点配置...");
                        NotifyRealError("模型不存在", rawMessage, config.ProviderId);
                        throw new AlreadyNotifiedApiException(rawMessage, ex);
                    }
                    if (useResult == TM.Services.Framework.AI.Core.KeyUseResult.ContentFiltered)
                    {
                        GenerationProgressHub.Report("请求触发内容策略限制，请调整后重试...");
                        NotifyRealError("内容审核拒绝", rawMessage, config.ProviderId);
                        throw new AlreadyNotifiedApiException(rawMessage, ex);
                    }

                    if (useResult == TM.Services.Framework.AI.Core.KeyUseResult.InvalidRequest)
                        throw;

                    if (useResult == TM.Services.Framework.AI.Core.KeyUseResult.RateLimited)
                    {
                        var retryAfterSec = TryExtractRetryAfterSeconds(ex);
                        var cooledSec = rotation.CooldownRateLimitedKey(config.ProviderId, selection.KeyId, retryAfterSec);
                        rateLimitExcludeKeyIds.Add(selection.KeyId);

                        var retryAfterTag = retryAfterSec.HasValue ? $"Retry-After={retryAfterSec}s" : "无 Retry-After";
                        var userMsg = IsTianmingPrivateProvider(config.ProviderId)
                            ? $"当前端点已限速，{cooledSec}s 后自动恢复，期间切换其他密钥..."
                            : $"[{keyLabel}] 已冷却 {cooledSec}s 自动恢复，期间切换其他密钥...";
                        LogIfPublic(config, $"[SKChatService] 429 限流，{retryAfterTag}，冷却 {cooledSec}s | {GetConfigSummary(config)}");
                        GenerationProgressHub.Report(userMsg);
                        continue;
                    }

                    if (useResult is TM.Services.Framework.AI.Core.KeyUseResult.AuthFailure
                        or TM.Services.Framework.AI.Core.KeyUseResult.Forbidden
                        or TM.Services.Framework.AI.Core.KeyUseResult.QuotaExhausted)
                    {
                        allFailuresAreRateLimited = false;
                        excludeKeyIds.Add(selection.KeyId);
                        NotifyKeyError(useResult, keyLabel, rawMessage, config.ProviderId);
                        GenerationProgressHub.Report("当前密钥不可用，切换下一个密钥重试...");
                        continue;
                    }

                    allFailuresAreRateLimited = false;
                    if (useResult == TM.Services.Framework.AI.Core.KeyUseResult.ServerError
                        || useResult == TM.Services.Framework.AI.Core.KeyUseResult.Unknown)
                    {
                        if (ChatModeSettings.IsMaxTokensError(ex) || ChatModeSettings.IsContextWindowError(ex))
                        {
                            LogIfPublic(config, $"[SKChatService] Rotation: 检测到 token 配置错误，跳过 key 轮换直接抛出: {ex.Message[..Math.Min(120, ex.Message.Length)]}");
                            throw;
                        }

                        serverErrorCount++;
                        var delay = GetExponentialBackoff(serverErrorCount - 1, ServerErrorBackoffBaseSeconds, ServerErrorBackoffMaxSeconds);
                        LogIfPublic(config, $"[SKChatService] 服务端错误退避（Rotation）: {useResult}, {delay.TotalSeconds}s | {GetConfigSummary(config)}");
                        GenerationProgressHub.Report($"AI服务暂时异常，{delay.TotalSeconds:F0} 秒后重试...");
                        try { await Task.Delay(delay, ct).ConfigureAwait(false); }
                        catch (OperationCanceledException) { throw; }

                        if (poolSize > 1)
                            excludeKeyIds.Add(selection.KeyId);
                        continue;
                    }

                    NotifyRealError("AI 请求失败", rawMessage, config.ProviderId);
                    throw new AlreadyNotifiedApiException(rawMessage, ex);
                }
            }

            if (!allKeysExhausted && failedKeyDetails.Count > 0 && excludeKeyIds.Count >= poolSize)
                allKeysExhausted = true;

            if (allKeysExhausted && allowFailover)
            {
                try
                {
                    var router = ServiceLocator.Get<WritingApiRouter>();
                    var beforeId = config.Id;

                    var primaryId = AI.GetActiveConfiguration()?.Id ?? beforeId;

                    if (!string.Equals(beforeId, primaryId, StringComparison.Ordinal))
                    {
                        allowFailover = false;
                    }
                    else
                    {
                        GenerationProgressHub.Report("主接口不可用，尝试切换备用接口...");
                        router.TryActivateBackupForFailedConfig(beforeId);
                        var after = AI.GetActiveConfiguration();
                        if (router.IsUsingBackup && after != null && !string.Equals(after.Id, beforeId, StringComparison.Ordinal))
                        {
                            GenerationProgressHub.Report("已切换备用接口，继续重试...");
                            return await InvokeApiWithRotationAsync(apiCall, ct, maxKeyRetries, after, allowFailover: false, progress: progress).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogIfPublic(config, $"[SKChatService] 备用切换失败: {ex.Message}");
                }
            }

            if (allKeysExhausted && allFailuresAreRateLimited && rateLimitRound >= maxRateLimitRounds)
            {
                GenerationProgressHub.Report("端点整体限流，请稍后再试或切换端点...");
                GlobalToast.Warning("端点整体限流", $"所有密钥均被端点限流（已重试 {rateLimitRound} 轮），请稍后再试或切换端点。");
                LogIfPublic(config, $"[SKChatService] 端点整体限流，{rateLimitRound} 轮重试后仍未恢复，并非密钥永久失效 | {GetConfigSummary(config)}");
            }
            else if (allKeysExhausted && poolSize <= 0)
            {
                GenerationProgressHub.Report("当前服务商没有可用密钥，请检查模型管理中的 API Key...");
                GlobalToast.Error("没有可用密钥", "当前服务商没有启用的 API Key，请在模型管理中启用或新增密钥。");
                LogIfPublic(config, $"[SKChatService] 当前服务商没有可用密钥 | {GetConfigSummary(config)}");
            }
            else if (allKeysExhausted && serverErrorCount > 0 && excludeKeyIds.Count == serverErrorCount)
            {
                GenerationProgressHub.Report("AI服务连接失败，请检查网络或代理设置...");
                NotifyRealError("连接失败", "无法连接到 API 端点，请检查网络或代理设置。", config.ProviderId);
                LogIfPublic(config, $"[SKChatService] 连接失败（非密钥问题），ServerError/Unknown 导致 | {GetConfigSummary(config)}");
            }
            else if (allKeysExhausted && failedKeyDetails.Count > 0)
            {
                GenerationProgressHub.Report("所有密钥均不可用，请检查密钥、额度或端点配置...");
                NotifyAllKeysExhausted(failedKeyDetails, config.ProviderId);
            }
            else if (failedKeyDetails.Count > 0)
            {
                GenerationProgressHub.Report("AI请求多次失败，请稍后重试...");
                NotifyRealError("AI 请求失败", $"已重试 {failedKeyDetails.Count} 次，均失败", config.ProviderId);
            }
            var summary = failedKeyDetails.Count > 0
                ? string.Join("\n", failedKeyDetails)
                : poolSize <= 0
                    ? "当前服务商没有可用密钥，请在模型管理中启用或新增 API Key"
                    : $"所有 {poolSize} 个密钥均被端点限流（{rateLimitRound} 轮退避后仍未恢复）";
            throw new AlreadyNotifiedApiException($"所有密钥不可用:\n{summary}");
        }

        private void ResetActiveThinkingParams(UserConfiguration? failedConfig = null)
        {
            try
            {
                var config = failedConfig ?? AI.GetActiveConfiguration();
                if (config == null) return;

                ChatModeSettings.RecordEffortCap(config.ProviderId, config.CustomEndpoint, config.ModelId, string.Empty);
                ChatModeSettings.RecordThinkingDisabled(config.ProviderId, config.CustomEndpoint, config.ModelId);
                LogIfPublic(config, $"[SKChatService] 思考参数兜底关闭（cap，未改用户配置）: model={config.ModelId}");
            }
            catch (Exception ex)
            {
                LogIfPublic(failedConfig, $"[SKChatService] 推理参数兜底处理失败: {ex.Message}");
            }
            finally
            {
                _skipThinkingInjection = false;
            }
        }

        private static bool IsConnectionError(Exception ex)
        {
            var e = ex;
            while (e != null)
            {
                if (e is System.Net.Http.HttpRequestException httpEx && !httpEx.StatusCode.HasValue)
                    return true;
                if (e is System.Net.Sockets.SocketException)
                    return true;

                var msg = e.Message;
                if (msg.Contains("proxy tunnel", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("connection refused", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("No such host", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("Name or service not known", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("SSL", StringComparison.OrdinalIgnoreCase) && msg.Contains("handshake", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("SOCKS", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase))
                    return true;

                e = e.InnerException;
            }
            return false;
        }

        private static bool IsStreamNetworkError(Exception ex)
        {
            var e = ex;
            while (e != null)
            {
                if (e is System.IO.IOException) return true;
                if (e is System.Net.Http.HttpRequestException httpEx && !httpEx.StatusCode.HasValue) return true;
                var msg = e.Message;
                if (msg.Contains("EOF", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("protocol error", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("connection reset", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("connection closed", StringComparison.OrdinalIgnoreCase))
                    return true;
                e = e.InnerException;
            }
            return false;
        }

        private static bool IsThinkingNotSupportedError(Exception ex)
        {
            var e = ex;
            while (e != null)
            {
                var msg = e.Message;
                if (msg.Contains("thinking", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("reasoning_effort", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("budget_tokens", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("enable_thinking", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("thinkingConfig", StringComparison.OrdinalIgnoreCase))
                    return true;
                e = e.InnerException;
            }
            return false;
        }

        private static readonly Regex RetryAfterTryAgainRegex = new(
            @"try\s+again\s+in\s+(\d+(?:\.\d+)?)\s*(ms|millisecond[s]?|s|sec|second[s]?)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RetryAfterKeyedRegex = new(
            @"retry[-_\s]?after[""'\s:=]+(\d+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RetryDelayRegex = new(
            @"retry[\s_]*delay[""'\s:=]+(\d+)\s*s",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static int? TryExtractRetryAfterSeconds(Exception? ex)
        {
            if (ex == null) return null;

            var scan = ex;
            while (scan != null)
            {
                var fromHeader = TryReadRetryAfterFromResponseObject(scan);
                if (fromHeader.HasValue) return fromHeader;
                scan = scan.InnerException;
            }

            scan = ex;
            while (scan != null)
            {
                var m = scan.Message;
                if (!string.IsNullOrEmpty(m))
                {
                    var mm = RetryAfterTryAgainRegex.Match(m);
                    if (mm.Success && double.TryParse(
                            mm.Groups[1].Value,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var raw))
                    {
                        var unit = mm.Groups[2].Value.ToLowerInvariant();
                        var sec = unit.StartsWith("ms", StringComparison.Ordinal)
                            ? (int)Math.Ceiling(raw / 1000.0)
                            : (int)Math.Ceiling(raw);
                        return Math.Max(1, sec);
                    }
                    mm = RetryAfterKeyedRegex.Match(m);
                    if (mm.Success && int.TryParse(mm.Groups[1].Value, out var v2))
                        return Math.Max(1, v2);
                    mm = RetryDelayRegex.Match(m);
                    if (mm.Success && int.TryParse(mm.Groups[1].Value, out var v3))
                        return Math.Max(1, v3);
                }
                scan = scan.InnerException;
            }
            return null;
        }

        private static int? TryReadRetryAfterFromResponseObject(Exception ex)
        {
            try
            {
                var type = ex.GetType();
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                var getRawResponse = type.GetMethod("GetRawResponse", flags, Type.EmptyTypes);
                object? response = getRawResponse?.Invoke(ex, null);

                response ??= type.GetProperty("Response", flags)?.GetValue(ex);
                if (response == null) return null;

                var responseType = response.GetType();
                var headersProp = responseType.GetProperty("Headers", flags);
                var headers = headersProp?.GetValue(response);
                if (headers == null) return null;

                var headersType = headers.GetType();
                foreach (var method in headersType.GetMethods(flags))
                {
                    if (!method.Name.Equals("TryGetValue", StringComparison.Ordinal)) continue;
                    var parameters = method.GetParameters();
                    if (parameters.Length != 2) continue;
                    if (parameters[0].ParameterType != typeof(string)) continue;

                    var args = new object?[] { "Retry-After", null };
                    var found = method.Invoke(headers, args);
                    if (found is bool b && b && args[1] != null)
                    {
                        string? raw = null;
                        if (args[1] is string s) raw = s;
                        else if (args[1] is System.Collections.IEnumerable en)
                            foreach (var item in en) { raw = item?.ToString(); break; }
                        if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out var sec) && sec > 0)
                            return sec;
                    }
                }
            }
            catch { }
            return null;
        }

        private static (TM.Services.Framework.AI.Core.KeyUseResult Type, string RawMessage) ClassifyException(Exception ex)
        {
            var rawMsg = ex.Message;
            var msgLower = rawMsg.ToLowerInvariant();

            if (msgLower.Contains("insufficient_quota")
                || msgLower.Contains("billing_hard_limit")
                || msgLower.Contains("credit balance")
                || msgLower.Contains("quota exhausted")
                || msgLower.Contains("quota has been exhausted")
                || msgLower.Contains("tokenstatusexhausted")
                || msgLower.Contains("token exhausted")
                || rawMsg.Contains("额度已用尽", StringComparison.Ordinal)
                || rawMsg.Contains("额度不足", StringComparison.Ordinal)
                || rawMsg.Contains("余额不足", StringComparison.Ordinal)
                || rawMsg.Contains("配额已用尽", StringComparison.Ordinal))
            {
                return (TM.Services.Framework.AI.Core.KeyUseResult.QuotaExhausted, rawMsg);
            }

            if (ex is ArgumentOutOfRangeException
                && (rawMsg.Contains("ChatFinishReason", StringComparison.Ordinal)
                    || rawMsg.Contains("FinishReason", StringComparison.OrdinalIgnoreCase)))
            {
                return (TM.Services.Framework.AI.Core.KeyUseResult.InvalidRequest, rawMsg);
            }

            int? statusCode = null;
            {
                var scan = ex;
                while (scan != null)
                {
                    if (scan is System.Net.Http.HttpRequestException httpReq && httpReq.StatusCode.HasValue)
                    { statusCode = (int)httpReq.StatusCode.Value; break; }
                    scan = scan.InnerException;
                }

                if (statusCode == null)
                {
                    scan = ex;
                    while (scan != null)
                    {
                        try
                        {
                            var t = scan.GetType();
                            var prop = t.GetProperty("Status", BindingFlags.Public | BindingFlags.Instance)
                                       ?? t.GetProperty("StatusCode", BindingFlags.Public | BindingFlags.Instance);
                            if (prop != null)
                            {
                                var val = prop.GetValue(scan);
                                if (val is int i && i > 0) { statusCode = i; break; }
                                if (val is System.Net.HttpStatusCode hsc) { statusCode = (int)hsc; break; }
                            }
                        }
                        catch { }
                        scan = scan.InnerException;
                    }
                }

                if (statusCode == null)
                {
                    scan = ex;
                    while (scan != null)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(
                            scan.Message,
                            @"(?:Status:\s*|HTTP(?:/\d(?:\.\d)?)?\s+|status\s+code\s+|returned\s+)(\d{3})\b",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (m.Success && int.TryParse(m.Groups[1].Value, out var parsed))
                        { statusCode = parsed; break; }
                        scan = scan.InnerException;
                    }
                }
            }

            if (statusCode.HasValue)
            {
                var code = statusCode.Value;

                if (code == 400 || code == 404 || code == 406)
                {
                    if (ContainsAny(msgLower, "model_not_found", "model not found", "does not exist",
                        "model is not supported", "model_unavailable",
                        "unknown model", "invalid model", "no such model"))
                        return (TM.Services.Framework.AI.Core.KeyUseResult.ModelNotFound, rawMsg);

                    if (ContainsAny(msgLower, "content_filter", "content_policy", "content policy",
                        "content moderation", "flagged", "violat", "harmful", "inappropriate",
                        "safety system", "responsible ai"))
                        return (TM.Services.Framework.AI.Core.KeyUseResult.ContentFiltered, rawMsg);

                    if (ContainsAny(msgLower, "streaming is not supported", "stream is not supported",
                        "does not support streaming", "streaming not available", "text/event-stream",
                        "event-stream not supported", "server-sent events"))
                        return (TM.Services.Framework.AI.Core.KeyUseResult.StreamNotSupported, rawMsg);

                    if (code == 400)
                        return (TM.Services.Framework.AI.Core.KeyUseResult.InvalidRequest, rawMsg);
                }

                var type = code switch
                {
                    401 => TM.Services.Framework.AI.Core.KeyUseResult.AuthFailure,
                    403 => TM.Services.Framework.AI.Core.KeyUseResult.Forbidden,
                    429 => TM.Services.Framework.AI.Core.KeyUseResult.RateLimited,
                    402 => TM.Services.Framework.AI.Core.KeyUseResult.QuotaExhausted,
                    >= 500 => TM.Services.Framework.AI.Core.KeyUseResult.ServerError,
                    _ => TM.Services.Framework.AI.Core.KeyUseResult.Unknown
                };
                return (type, rawMsg);
            }

            if (ex is System.Net.Http.HttpRequestException { InnerException: System.Net.Sockets.SocketException })
                return (TM.Services.Framework.AI.Core.KeyUseResult.NetworkError, ex.Message);

            if (IsStreamNetworkError(ex))
                return (TM.Services.Framework.AI.Core.KeyUseResult.NetworkError, ex.Message);

            return (TM.Services.Framework.AI.Core.KeyUseResult.Unknown, ex.Message);
        }

        private static void NotifyKeyError(TM.Services.Framework.AI.Core.KeyUseResult type, string keyLabel, string rawMessage, string? providerId = null)
        {
            try
            {
                LogIfPublicProviderId(providerId, $"[SKChatService] KeyError: type={type}, key={keyLabel}, msg={rawMessage}");
                var (title, isError) = type switch
                {
                    TM.Services.Framework.AI.Core.KeyUseResult.AuthFailure => ("密钥认证失败", false),
                    TM.Services.Framework.AI.Core.KeyUseResult.Forbidden => ("密钥已被封禁", true),
                    TM.Services.Framework.AI.Core.KeyUseResult.QuotaExhausted => ("密钥额度用完", false),
                    TM.Services.Framework.AI.Core.KeyUseResult.RateLimited => ("密钥限速", false),
                    _ => ("密钥错误", false)
                };
                var body = $"{keyLabel} 已自动禁用并切换到下一个密钥。请在模型管理中检查密钥状态。";
                if (isError) GlobalToast.Error(title, body);
                else GlobalToast.Warning(title, body);
            }
            catch { }
        }

        private static void NotifyRealError(string title, string rawMessage, string? providerId = null)
        {
            try
            {
                var isPrivate = IsTianmingPrivateProvider(providerId);
                var msg = rawMessage.ToLowerInvariant();
                if (ContainsAny(msg, "model_not_found", "model not found", "does not exist", "unknown model", "invalid model", "no such model", "model is not supported"))
                    GlobalToast.Error("模型不存在", $"当前模型在该端点不可用，请检查模型名称是否正确或切换端点。");
                else if (ContainsAny(msg, "content_filter", "content_policy", "content moderation", "flagged", "harmful", "inappropriate", "safety system", "responsible ai"))
                    GlobalToast.Warning("内容审核拒绝", "您的请求被 AI 服务的内容安全策略拦截，请调整输入内容后重试。");
                else if (ContainsAny(msg, "streaming is not supported", "stream is not supported", "does not support streaming", "streaming not available",
                    "text/event-stream", "event-stream not supported", "server-sent events"))
                    GlobalToast.Info("流式不支持", "该端点不支持流式传输，已自动降级为标准模式。");
                else if (ContainsAny(msg, "401", "unauthorized", "invalid api key", "authentication failed"))
                    GlobalToast.Error("认证失败", "API Key 无效或已过期，请在模型配置中更新");
                else if (ContainsAny(msg, "429", "rate limit", "too many requests"))
                    GlobalToast.Warning("频率限制", "请求过于频繁，请稍后重试或切换模型");
                else if (ContainsAny(msg, "context_length", "context length", "token limit", "maximum context"))
                    GlobalToast.Warning("上下文过长", "对话历史超出模型限制，请清理历史记录后重试");
                else if (ContainsAny(msg, "502", "503", "504", "service unavailable", "bad gateway", "overloaded"))
                    GlobalToast.Warning("服务不可用", "AI 服务暂时故障，请稍后重试");
                else if (ContainsAny(msg, "timeout", "timed out"))
                    GlobalToast.Error("请求超时", "请检查网络或代理连接");
                else if (ContainsAny(msg, "error while copying content to a stream", "copying content to a stream"))
                    GlobalToast.Warning("流式连接中断", "流式传输过程中网络/端点连接被中断，请稍后重试或更换端点。");
                else if (ContainsAny(msg, "an error occurred while sending the request", "error occurred while sending", "a connection attempt failed", "no connection could be made", "actively refused", "connection refused", "network is unreachable", "远程服务器强迫关闭", "forcibly closed"))
                    GlobalToast.Warning("网络连接失败", "网络/端点连接异常，请求无法送达。可稍后重试或更换端点。");
                else if (isPrivate)
                    GlobalToast.Error(title, "请求失败，请稍后重试或切换模型。");
                else
                    GlobalToast.Error(title, $"请求失败：{TrimForToast(rawMessage)}（可稍后重试或切换模型）");
            }
            catch { }
        }

        private static void NotifyAllKeysExhausted(List<string> failedDetails, string? providerId = null)
        {
            try
            {
                var isPrivate = IsTianmingPrivateProvider(providerId);
                if (!isPrivate && failedDetails.Count > 0)
                    TM.App.Log($"[SKChatService] 所有密钥不可用：已尝试 {failedDetails.Count} 个密钥，失败明细：\n{string.Join("\n", failedDetails)}");

                string? sampleReason = null;
                if (!isPrivate && failedDetails.Count > 0)
                {
                    var sample = failedDetails.LastOrDefault() ?? failedDetails.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(sample))
                    {
                        var idx = sample.IndexOf("] → ", StringComparison.Ordinal);
                        sampleReason = idx >= 0 ? sample[(idx + 4)..] : sample;
                    }
                }

                var msg = string.IsNullOrWhiteSpace(sampleReason)
                    ? "已尝试所有密钥但均失败，请在模型管理中检查密钥/额度/网络。"
                    : $"已尝试所有密钥但均失败。原因示例：{TrimForToast(sampleReason, 180)}。请在模型管理中检查密钥/额度/网络。";

                GlobalToast.Error("所有密钥不可用", msg);
            }
            catch { }
        }

        #endregion

        private static string TrimForToast(string? value, int maxLen = 200)
        {
            if (string.IsNullOrWhiteSpace(value)) return "未知错误";
            var s = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length <= maxLen ? s : s[..maxLen] + "...";
        }

        private static bool ContainsAny(string source, params string[] keywords)
        {
            foreach (var kw in keywords)
                if (source.Contains(kw, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private enum AdaptiveErrorKind
        {
            Timeout,
            EmptyResponse,
            MaxDurationReached,
            ServiceUnconfigured,
            Unknown
        }

        private static AdaptiveErrorKind ClassifyAdaptiveError(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return AdaptiveErrorKind.Unknown;
            if (content.Contains("AI 服务未配置", StringComparison.Ordinal))
                return AdaptiveErrorKind.ServiceUnconfigured;
            if (content.Contains("模型思考超过", StringComparison.Ordinal))
                return AdaptiveErrorKind.MaxDurationReached;
            if (content.Contains("服务器超过", StringComparison.Ordinal) && content.Contains("秒", StringComparison.Ordinal))
                return AdaptiveErrorKind.Timeout;
            if (content.Contains("标准模式响应超时", StringComparison.Ordinal))
                return AdaptiveErrorKind.Timeout;
            if (content.Contains("超时", StringComparison.Ordinal))
                return AdaptiveErrorKind.Timeout;
            return AdaptiveErrorKind.Unknown;
        }

        private static string TrimAdaptiveContent(string content, int maxLen = 80)
        {
            if (string.IsNullOrWhiteSpace(content)) return string.Empty;
            var s = content.Replace("\r", " ").Replace("\n", " ").Trim();
            if (s.StartsWith("[错误]", StringComparison.Ordinal))
                s = s[4..].Trim();
            return s.Length <= maxLen ? s : s[..maxLen] + "...";
        }
    }
}
