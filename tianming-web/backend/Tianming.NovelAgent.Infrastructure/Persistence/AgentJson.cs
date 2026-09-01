using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tianming.NovelAgent.Infrastructure.Persistence;

internal static class AgentJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string value) =>
        JsonSerializer.Deserialize<T>(value, Options)
        ?? throw new InvalidOperationException($"Unable to deserialize {typeof(T).Name}.");
}
