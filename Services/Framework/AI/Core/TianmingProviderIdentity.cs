using System;

namespace TM.Services.Framework.AI.Core;

public static class TianmingProviderIdentity
{
    public const string PaidPrefix = "tm-model-";
    public const string PublicPrefix = "tm-public-";

    public const string MaskedAllLabel = "（内置·已隐藏）";
    public const string MaskedEndpointLabel = "（端点·已隐藏）";

    public static bool IsTianmingPrivate(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return false;
        return providerId.StartsWith(PaidPrefix, StringComparison.OrdinalIgnoreCase)
            || providerId.StartsWith(PublicPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static string? ResolveEntryCategory(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId)) return null;
        if (providerId.StartsWith(PaidPrefix, StringComparison.OrdinalIgnoreCase)) return "paid";
        if (providerId.StartsWith(PublicPrefix, StringComparison.OrdinalIgnoreCase)) return "public";
        return null;
    }
}
