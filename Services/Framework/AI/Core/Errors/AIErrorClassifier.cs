using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;

namespace TM.Services.Framework.AI.Core.Errors
{
    public static class AIErrorClassifier
    {
        public static AIRequestError Classify(
            Exception ex,
            string? providerId = null,
            string? modelId = null,
            string? endpoint = null,
            CancellationToken cancellationToken = default,
            Guid? runId = null)
        {
            ArgumentNullException.ThrowIfNull(ex);

            var kind = ClassifyKind(ex, cancellationToken);
            var http = TryGetHttpStatus(ex);
            var msg = ex.Message ?? string.Empty;

            return new AIRequestError
            {
                Kind = kind,
                Message = msg,
                UserFriendlyMessage = GetUserFriendlyMessage(kind),
                ProviderId = providerId,
                ModelId = modelId,
                Endpoint = endpoint,
                InnerException = ex,
                HttpStatusCode = http,
                RunId = runId,
            };
        }

        public static AIRequestErrorKind ClassifyKind(Exception ex, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(ex);

            if (ex is TimeoutException) return AIRequestErrorKind.StreamInterrupted;

            if (ex is OperationCanceledException oce)
            {
                if (cancellationToken.IsCancellationRequested) return AIRequestErrorKind.Cancelled;
                if (oce.CancellationToken.IsCancellationRequested) return AIRequestErrorKind.Cancelled;
                return AIRequestErrorKind.StreamInterrupted;
            }

            if (IsToolPermissionDenied(ex)) return AIRequestErrorKind.ToolPermissionDenied;

            var typeName = ex.GetType().FullName ?? string.Empty;
            if (typeName.Contains("BrokenCircuit", StringComparison.Ordinal))
                return AIRequestErrorKind.ProviderInternal;
            if (typeName.Contains("TimeoutRejected", StringComparison.Ordinal))
                return AIRequestErrorKind.StreamInterrupted;

            if (ex is SocketException
                || ex is System.Security.Authentication.AuthenticationException
                || ex.InnerException is SocketException)
                return AIRequestErrorKind.Network;

            var status = TryGetHttpStatus(ex);
            if (status.HasValue)
            {
                if (status == 401 || status == 403) return AIRequestErrorKind.Authentication;
                if (status == 429) return AIRequestErrorKind.RateLimit;
                if (status >= 500 && status < 600) return AIRequestErrorKind.ProviderInternal;

                if (status == 400)
                {
                    if (IsContextOverflow(ex)) return AIRequestErrorKind.ContextOverflow;
                    if (IsModelNotSupported(ex)) return AIRequestErrorKind.ModelNotSupported;
                }
            }

            if (ex is HttpRequestException hre && !hre.StatusCode.HasValue)
            {
                return AIRequestErrorKind.Network;
            }

            if (IsContextOverflow(ex)) return AIRequestErrorKind.ContextOverflow;
            if (IsModelNotSupported(ex)) return AIRequestErrorKind.ModelNotSupported;

            if (typeName.Contains("KernelException", StringComparison.Ordinal)
                || typeName.Contains("KernelFunctionInvocation", StringComparison.Ordinal))
                return AIRequestErrorKind.ToolExecutionError;

            return AIRequestErrorKind.Unknown;
        }

        public static string GetUserFriendlyMessage(AIRequestErrorKind kind) => kind switch
        {
            AIRequestErrorKind.Network => "网络连接失败，请检查网络或 endpoint 是否可达",
            AIRequestErrorKind.Authentication => "API 密钥无效或权限不足，请在模型管理中检查密钥",
            AIRequestErrorKind.RateLimit => "请求过于频繁（429），已自动退避重试；如仍失败请稍后再试",
            AIRequestErrorKind.ModelNotSupported => "当前模型不支持此参数，已自动跳过对应字段（可在模型管理中调整）",
            AIRequestErrorKind.ContextOverflow => "对话上下文已超出模型最大长度，建议清理早期消息或切换长上下文模型",
            AIRequestErrorKind.StreamInterrupted => "响应流中断或长时间无返回，已自动取消；请重试",
            AIRequestErrorKind.ProviderInternal => "服务端错误（5xx 或熔断），请稍后重试或切换其他模型",
            AIRequestErrorKind.Cancelled => "已取消",
            AIRequestErrorKind.ToolExecutionError => "工具执行失败，请查看日志排查原因",
            AIRequestErrorKind.ToolPermissionDenied => "当前模式禁止此工具操作，请切换模式或调整权限",
            _ => "未知错误，请查看日志获取详细信息",
        };

        public static int? TryGetHttpStatus(Exception ex)
        {
            for (var cur = ex; cur != null; cur = cur.InnerException)
            {
                if (cur is HttpRequestException hre && hre.StatusCode.HasValue)
                    return (int)hre.StatusCode.Value;

                if (cur.Data != null)
                {
                    foreach (var key in new[] { "StatusCode", "HttpStatusCode", "Status" })
                    {
                        if (cur.Data.Contains(key))
                        {
                            var v = cur.Data[key];
                            if (v is int i) return i;
                            if (v is HttpStatusCode hs) return (int)hs;
                            if (v is string s && int.TryParse(s, out var parsed)) return parsed;
                        }
                    }
                }
            }
            return null;
        }

        private static bool IsContextOverflow(Exception ex)
        {
            for (var cur = ex; cur != null; cur = cur.InnerException)
            {
                var m = cur.Message ?? string.Empty;
                if (m.Contains("context_length_exceeded", StringComparison.OrdinalIgnoreCase)
                    || m.Contains("context length", StringComparison.OrdinalIgnoreCase)
                    || m.Contains("maximum context length", StringComparison.OrdinalIgnoreCase)
                    || m.Contains("token limit", StringComparison.OrdinalIgnoreCase)
                    || m.Contains("超出最大上下文", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsModelNotSupported(Exception ex)
        {
            for (var cur = ex; cur != null; cur = cur.InnerException)
            {
                var m = cur.Message ?? string.Empty;
                if (m.Contains("unsupported parameter", StringComparison.OrdinalIgnoreCase)
                    || m.Contains("is not enabled", StringComparison.OrdinalIgnoreCase)
                    || m.Contains("not supported by this model", StringComparison.OrdinalIgnoreCase)
                    || m.Contains("unrecognized request argument", StringComparison.OrdinalIgnoreCase)
                    || m.Contains("invalid_request_error", StringComparison.OrdinalIgnoreCase)
                    || m.Contains("不支持的参数", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsToolPermissionDenied(Exception ex)
        {
            for (var cur = ex; cur != null; cur = cur.InnerException)
            {
                var m = cur.Message ?? string.Empty;
                if (m.Contains("禁止调用", StringComparison.Ordinal)
                    || m.Contains("模式禁止", StringComparison.Ordinal)
                    || m.Contains("permission denied", StringComparison.OrdinalIgnoreCase)
                    || m.Contains("not allowed in", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
