using System;
using System.Text.Json.Serialization;
using TM.Framework.Common.Helpers.Id;

namespace TM.Services.Framework.AI.Core;

public class ApiKeyEntry
{
    [JsonPropertyName("Id")] public string Id { get; set; } = ShortIdGenerator.New("K");
    [JsonPropertyName("Key")] public string Key { get; set; } = string.Empty;
    [JsonPropertyName("Remark")] public string Remark { get; set; } = string.Empty;
    [JsonPropertyName("IsEnabled")] public bool IsEnabled { get; set; } = true;
    [JsonPropertyName("CreatedAt")] public DateTime CreatedAt { get; set; } = DateTime.Now;
}
