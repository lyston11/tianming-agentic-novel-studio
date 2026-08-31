using System.Text.Json;
using OpenAI.Responses;
using Tianming.NovelAgent.Application.Ports;
using Tianming.NovelAgent.Contracts.Models;

namespace Tianming.NovelAgent.Infrastructure.Models;

public sealed class OpenAIResponsesModelAdapter(ResponsesClient client) : IModelProviderAdapter
{
    public string Provider => "openai";

    public ModelProviderCapabilities Capabilities { get; } = new(
        StructuredOutput: true,
        Streaming: true,
        ToolCalling: true,
        UsageReporting: true,
        IdempotencyQuery: false);

    public async Task<ModelResult> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Provider, "openai", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The OpenAI adapter cannot execute provider '{request.Provider}'.");

        var input = string.Join(
            "\n\n",
            request.Messages.Select(message => $"[{message.Role}]\n{message.Content}"));
        var options = new CreateResponseOptions
        {
            Model = request.Model
        };
        if (!string.IsNullOrWhiteSpace(request.JsonSchema))
        {
            options.TextOptions = new ResponseTextOptions
            {
                TextFormat = ResponseTextFormat.CreateJsonSchemaFormat(
                    "model_result",
                    BinaryData.FromString(request.JsonSchema),
                    "Structured result for the requested novel-agent operation",
                    true)
            };
        }
        options.InputItems.Add(ResponseItem.CreateUserMessageItem(input));

        ResponseResult response = await client.CreateResponseAsync(options, cancellationToken);
        var content = string.Concat(
            response.OutputItems
                .OfType<MessageResponseItem>()
                .SelectMany(message => message.Content)
                .Select(part => part.Text));
        var structured = request.JsonSchema is not null && IsJson(content) ? content : null;
        var usage = response.Usage;

        return new ModelResult(
            content,
            structured,
            "openai",
            response.Model ?? request.Model,
            response.Id,
            response.Status?.ToString() ?? "unknown",
            new ModelUsage(
                usage?.InputTokenCount ?? 0,
                usage?.OutputTokenCount ?? 0,
                0m,
                "USD"),
            new Dictionary<string, string>
            {
                ["promptVersion"] = request.PromptVersion,
                ["schemaVersion"] = request.SchemaVersion,
                ["contextHash"] = request.ContextHash
            });
    }

    private static bool IsJson(string value)
    {
        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
