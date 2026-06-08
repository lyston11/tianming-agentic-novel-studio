namespace TM.Services.Framework.AI.Core;

public record KeySelection(string KeyId, string ApiKey, string? Remark);

public enum KeyUseResult
{
    Success,
    AuthFailure, Forbidden, RateLimited, QuotaExhausted, ServerError, NetworkError, ModelNotFound, InvalidRequest, ContentFiltered, StreamNotSupported, Unknown
}
