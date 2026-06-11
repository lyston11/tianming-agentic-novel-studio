using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TM.Web.NovelAgentWeb.Support;

public interface ILlmToolCallingClient
{
    Task<AgentAction?> PlanToolActionAsync(
        UserSettings settings,
        AgentObservationContext context,
        string systemPrompt,
        CancellationToken ct);
}

public sealed class ProviderToolCallingClient : ILlmToolCallingClient
{
    private readonly HttpClient _http;
    private readonly JsonActionFallbackClient _fallback = new();

    public ProviderToolCallingClient(HttpClient http) => _http = http;

    public async Task<AgentAction?> PlanToolActionAsync(
        UserSettings settings,
        AgentObservationContext context,
        string systemPrompt,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.LlmBaseUrl) ||
            string.IsNullOrWhiteSpace(settings.LlmModel) ||
            string.IsNullOrWhiteSpace(settings.LlmApiKey))
            return null;

        var provider = settings.LlmProvider.Trim().ToLowerInvariant();
        if (provider.Contains("anthropic", StringComparison.OrdinalIgnoreCase) ||
            provider.Contains("mimo", StringComparison.OrdinalIgnoreCase) ||
            provider.Contains("xiaomi", StringComparison.OrdinalIgnoreCase))
        {
            return await CallAnthropicToolsAsync(_http, settings, context, systemPrompt, provider.Contains("mimo"), ct)
                .ConfigureAwait(false);
        }

        if (!provider.Contains("ollama", StringComparison.OrdinalIgnoreCase))
            return await CallOpenAiToolsAsync(_http, settings, context, systemPrompt, ct).ConfigureAwait(false);

        return await _fallback.PlanToolActionAsync(settings, context, systemPrompt, ct).ConfigureAwait(false);
    }

    private static async Task<AgentAction?> CallOpenAiToolsAsync(
        HttpClient http,
        UserSettings settings,
        AgentObservationContext context,
        string systemPrompt,
        CancellationToken ct)
    {
        var payload = new
        {
            model = NormalizeModel(settings.LlmModel),
            temperature = settings.LlmTemperature,
            max_tokens = settings.LlmMaxTokens,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = BuildPlannerPayload(context) },
            },
            tools = context.AvailableTools.Select(ToOpenAiTool).ToArray(),
            tool_choice = "auto",
        };

        var body = await PostJsonWithAuthAsync(http, BuildChatCompletionsUrl(settings.LlmBaseUrl), settings.LlmApiKey, payload, ct)
            .ConfigureAwait(false);
        return ParseOpenAiAction(body);
    }

    private static async Task<AgentAction?> CallAnthropicToolsAsync(
        HttpClient http,
        UserSettings settings,
        AgentObservationContext context,
        string systemPrompt,
        bool mimoCompatible,
        CancellationToken ct)
    {
        var payload = new
        {
            model = NormalizeModel(settings.LlmModel),
            system = systemPrompt,
            max_tokens = settings.LlmMaxTokens,
            temperature = settings.LlmTemperature,
            tools = context.AvailableTools.Select(ToAnthropicTool).ToArray(),
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new[] { new { type = "text", text = BuildPlannerPayload(context) } }
                }
            }
        };

        var body = await PostJsonAnthropicAsync(http, BuildAnthropicMessagesUrl(settings.LlmBaseUrl), settings.LlmApiKey, payload, ct)
            .ConfigureAwait(false);
        var action = ParseAnthropicAction(body);
        if (action != null)
            action.Source = mimoCompatible ? "mimo_anthropic_tool_calling" : "anthropic_tool_calling";
        return action;
    }

    private static object ToOpenAiTool(AgentToolDefinition tool)
    {
        var properties = tool.Arguments.ToDictionary(
            arg => arg,
            _ => new { type = "string", description = "structured tool argument" },
            StringComparer.OrdinalIgnoreCase);
        return new
        {
            type = "function",
            function = new
            {
                name = tool.Name,
                description = $"{tool.Description} Risk={tool.Risk}; Autopilot=true",
                parameters = new
                {
                    type = "object",
                    properties,
                    additionalProperties = false,
                }
            }
        };
    }

    private static object ToAnthropicTool(AgentToolDefinition tool)
    {
        var properties = tool.Arguments.ToDictionary(
            arg => arg,
            _ => new { type = "string", description = "structured tool argument" },
            StringComparer.OrdinalIgnoreCase);
        return new
        {
            name = tool.Name,
            description = $"{tool.Description} Risk={tool.Risk}; Autopilot=true",
            input_schema = new
            {
                type = "object",
                properties,
                additionalProperties = false,
            }
        };
    }

    private static string BuildPlannerPayload(AgentObservationContext context)
    {
        var payload = new
        {
            instruction = "Use a native tool call when a tool is needed. For chat/clarify/final replies, call QueryProjectStatus only when status is needed; otherwise answer in text if your provider supports it. Never put raw user text into userGoal.",
            user_turn = context.UserTurn,
            user_message = context.UserMessage,
            project = new { context.ProjectId, context.ProjectTitle, context.Phase, context.ActiveRunId },
            mission_plan = context.MissionPlan,
            rag = context.Rag,
            recent_messages = context.RecentMessages,
            recent_observations = context.RecentObservations,
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public static AgentAction? ParseOpenAiActionForDiagnostics(string json) => ParseOpenAiAction(json);

    public static AgentAction? ParseAnthropicActionForDiagnostics(string json, bool mimoCompatible = false)
    {
        var action = ParseAnthropicAction(json);
        if (action != null && mimoCompatible)
            action.Source = "mimo_anthropic_tool_calling";
        return action;
    }

    private static AgentAction? ParseOpenAiAction(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            return null;
        var first = choices.EnumerateArray().FirstOrDefault();
        if (first.ValueKind == JsonValueKind.Undefined ||
            !first.TryGetProperty("message", out var message))
            return null;
        if (message.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
        {
            var call = calls.EnumerateArray().FirstOrDefault();
            if (call.ValueKind != JsonValueKind.Undefined &&
                call.TryGetProperty("function", out var function))
            {
                return BuildToolAction(
                    ReadString(function, "name"),
                    ReadString(function, "arguments"),
                    "openai_tool_calling");
            }
        }
        if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            return BuildTextAction(content.GetString() ?? string.Empty, "openai_tool_calling");
        return null;
    }

    private static AgentAction? ParseAnthropicAction(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var item in content.EnumerateArray())
        {
            if (ReadString(item, "type") == "tool_use")
            {
                var args = item.TryGetProperty("input", out var input) ? input.GetRawText() : "{}";
                return BuildToolAction(ReadString(item, "name"), args, "anthropic_tool_calling");
            }
        }
        var text = string.Join("\n", content.EnumerateArray()
            .Where(item => ReadString(item, "type") == "text")
            .Select(item => ReadString(item, "text"))
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(text) ? null : BuildTextAction(text, "anthropic_tool_calling");
    }

    private static AgentAction? BuildToolAction(string name, string rawArgs, string source)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        var call = new AgentToolCall { Name = name.Trim() };
        try
        {
            using var args = JsonDocument.Parse(string.IsNullOrWhiteSpace(rawArgs) ? "{}" : rawArgs);
            if (args.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in args.RootElement.EnumerateObject())
                    call.Arguments[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? string.Empty
                        : prop.Value.ToString();
            }
        }
        catch
        {
            return null;
        }

        return new AgentAction
        {
            Type = AgentActionType.ToolCall,
            Intent = "native_tool_call",
            ToolCall = call,
            Source = source,
            Confidence = 0.85,
        };
    }

    private static AgentAction BuildTextAction(string text, string source)
    {
        var trimmed = text.Trim();
        var action = TryParseActionTextEnvelope(trimmed, source);
        if (action != null)
            return action;

        return new AgentAction
        {
            Type = AgentActionType.ChatReply,
            Intent = "chat_reply",
            Reply = trimmed,
            Source = source,
            Confidence = 0.75,
        };
    }

    private static AgentAction? TryParseActionTextEnvelope(string text, string source)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            !text.Contains("action_type", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(ExtractJsonObject(text));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("action_type", out _))
                return null;

            var action = new AgentAction
            {
                Type = ParseActionType(ReadString(root, "action_type", "chat_reply")),
                Intent = ReadString(root, "intent", "free_chat"),
                Reply = ReadString(root, "reply"),
                Brief = ReadString(root, "brief"),
                Risk = ReadString(root, "risk", "Low"),
                RequiresConfirmation = ReadBool(root, "requires_confirmation"),
                Confidence = ReadDouble(root, "confidence", 0.75),
                Source = source,
            };

            if (root.TryGetProperty("rag_queries", out var queries) && queries.ValueKind == JsonValueKind.Array)
                action.RagQueries = queries.EnumerateArray()
                    .Select(item => item.GetString() ?? string.Empty)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToList();

            if (root.TryGetProperty("suggestions", out var suggestions) && suggestions.ValueKind == JsonValueKind.Array)
                action.Suggestions = suggestions.EnumerateArray()
                    .Select(item => item.GetString() ?? string.Empty)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToArray();

            if (root.TryGetProperty("tool_call", out var tool) && tool.ValueKind == JsonValueKind.Object)
            {
                var call = new AgentToolCall { Name = ReadString(tool, "name") };
                if (tool.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in args.EnumerateObject())
                        call.Arguments[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                            ? prop.Value.GetString() ?? string.Empty
                            : prop.Value.ToString();
                }

                if (!string.IsNullOrWhiteSpace(call.Name))
                    action.ToolCall = call;
            }

            NormalizeAction(action);
            return action;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> PostJsonWithAuthAsync(HttpClient http, string url, string apiKey, object payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"模型接口返回 {(int)response.StatusCode}: {body}");
        return body;
    }

    private static async Task<string> PostJsonAnthropicAsync(HttpClient http, string url, string apiKey, object payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"模型接口返回 {(int)response.StatusCode}: {body}");
        return body;
    }

    private static string BuildChatCompletionsUrl(string baseUrl)
    {
        var url = baseUrl.Trim().TrimEnd('/');
        return url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) ? url : $"{url}/chat/completions";
    }

    private static string BuildAnthropicMessagesUrl(string baseUrl)
    {
        var url = baseUrl.Trim().TrimEnd('/');
        if (url.EndsWith("/v1/messages", StringComparison.OrdinalIgnoreCase) || url.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
            return url;
        if (url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            return $"{url}/messages";
        return $"{url}/v1/messages";
    }

    private static string NormalizeModel(string model)
    {
        var value = model.Trim();
        if (value.EndsWith("[1m]", StringComparison.OrdinalIgnoreCase))
            value = value[..^4].Trim();
        if (value.EndsWith(":extended", StringComparison.OrdinalIgnoreCase))
            value = value[..^9].Trim();
        return value;
    }

    private static void NormalizeAction(AgentAction action)
    {
        if (string.IsNullOrWhiteSpace(action.Intent))
            action.Intent = "free_chat";
        if (action.ToolCall != null && string.IsNullOrWhiteSpace(action.ToolCall.Name))
            action.ToolCall = null;
        if (action.Type == AgentActionType.ToolCall && action.ToolCall == null)
            action.Type = AgentActionType.ChatReply;
        if (action.Type == AgentActionType.ConfirmRequest && action.ToolCall != null)
            action.Type = AgentActionType.ToolCall;
        action.RequiresConfirmation = false;
    }

    private static AgentActionType ParseActionType(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "clarify" => AgentActionType.Clarify,
            "retrieve" => AgentActionType.Retrieve,
            "tool_call" => AgentActionType.ToolCall,
            "confirm_request" => AgentActionType.ConfirmRequest,
            "reflect" => AgentActionType.Reflect,
            "final_reply" => AgentActionType.FinalReply,
            _ => AgentActionType.ChatReply,
        };

    private static string ExtractJsonObject(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        return start >= 0 && end > start ? raw[start..(end + 1)] : raw;
    }

    private static string ReadString(JsonElement root, string name, string fallback = "") =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static bool ReadBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        value.GetBoolean();

    private static double ReadDouble(JsonElement root, string name, double fallback) =>
        root.TryGetProperty(name, out var value) && value.TryGetDouble(out var d) ? d : fallback;
}

public sealed class JsonActionFallbackClient : ILlmToolCallingClient
{
    public Task<AgentAction?> PlanToolActionAsync(
        UserSettings settings,
        AgentObservationContext context,
        string systemPrompt,
        CancellationToken ct) => Task.FromResult<AgentAction?>(null);
}
