using System.Text.Json;

namespace TM.Web.NovelAgentWeb.Services.Production;

internal static class WritingModelProviderResponseParser
{
    public static WritingModelCompletionResult ParseOpenAi(
        string json,
        string provider,
        string model,
        decimal inputPricePerMillion,
        decimal outputPricePerMillion)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var text = root.GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? json;
        return Build(
            root,
            text,
            provider,
            model,
            inputPricePerMillion,
            outputPricePerMillion,
            "prompt_tokens",
            "completion_tokens");
    }

    public static WritingModelCompletionResult ParseAnthropic(
        string json,
        string provider,
        string model,
        decimal inputPricePerMillion,
        decimal outputPricePerMillion)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var text = root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array
            ? string.Join("\n", content.EnumerateArray()
                .Select(item => item.TryGetProperty("text", out var value) ? value.GetString() : null)
                .Where(value => !string.IsNullOrWhiteSpace(value)))
            : json;
        return Build(
            root,
            text,
            provider,
            model,
            inputPricePerMillion,
            outputPricePerMillion,
            "input_tokens",
            "output_tokens");
    }

    public static string NormalizeModelId(string value)
    {
        value = (value ?? string.Empty).Trim();
        if (value.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            value = value["models/".Length..];
        if (value.EndsWith("[1m]", StringComparison.OrdinalIgnoreCase))
            value = value[..^4].Trim();
        if (value.EndsWith(":extended", StringComparison.OrdinalIgnoreCase))
            value = value[..^9].Trim();
        return value;
    }

    private static WritingModelCompletionResult Build(
        JsonElement root,
        string text,
        string provider,
        string model,
        decimal inputPricePerMillion,
        decimal outputPricePerMillion,
        string inputTokenProperty,
        string outputTokenProperty)
    {
        var requestId = root.TryGetProperty("id", out var id) ? id.GetString() : null;
        var hasInputUsage = TryReadUsage(root, inputTokenProperty, out var inputTokens);
        var hasOutputUsage = TryReadUsage(root, outputTokenProperty, out var outputTokens);
        return new WritingModelCompletionResult(
            text,
            provider,
            NormalizeModelId(model),
            requestId,
            inputTokens,
            outputTokens,
            inputPricePerMillion,
            outputPricePerMillion,
            hasInputUsage && hasOutputUsage);
    }

    private static bool TryReadUsage(JsonElement root, string propertyName, out int tokens)
    {
        if (!root.TryGetProperty("usage", out var usage) ||
            !usage.TryGetProperty(propertyName, out var value) ||
            !value.TryGetInt32(out tokens) ||
            tokens < 0)
        {
            tokens = 0;
            return false;
        }
        return true;
    }
}
