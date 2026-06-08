using System;

namespace TM.Services.Framework.AI.Core.Errors
{

    public enum AIRequestErrorKind
    {
        Unknown = 0,

        Network = 1,

        Authentication = 2,

        RateLimit = 3,

        ModelNotSupported = 4,

        ContextOverflow = 5,

        StreamInterrupted = 6,

        ProviderInternal = 7,

        Cancelled = 8,

        ToolExecutionError = 9,

        ToolPermissionDenied = 10,
    }

    public sealed record AIRequestError
    {
        public required AIRequestErrorKind Kind { get; init; }

        public required string Message { get; init; }

        public string? UserFriendlyMessage { get; init; }

        public string? ProviderId { get; init; }

        public string? ModelId { get; init; }

        public string? Endpoint { get; init; }

        public Exception? InnerException { get; init; }

        public int? HttpStatusCode { get; init; }

        public Guid? RunId { get; init; }

        public string ToDiagnosticString()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append('[').Append(Kind).Append("] ").Append(Message);
            if (!string.IsNullOrEmpty(ProviderId) || !string.IsNullOrEmpty(ModelId))
            {
                sb.Append(" | ").Append(ProviderId ?? string.Empty).Append('/').Append(ModelId ?? string.Empty);
            }
            if (HttpStatusCode.HasValue)
            {
                sb.Append(" | http=").Append(HttpStatusCode.Value);
            }
            if (InnerException != null)
            {
                sb.Append(" | inner=").Append(InnerException.GetType().Name).Append(": ").Append(InnerException.Message);
            }
            return sb.ToString();
        }
    }
}
