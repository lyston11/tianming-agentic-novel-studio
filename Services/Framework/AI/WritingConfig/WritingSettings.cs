using System.Text.Json.Serialization;

namespace TM.Services.Framework.AI.WritingConfig;

public class WritingSettings
{
    [JsonPropertyName("BackupChatConfigId")]
    public string? BackupChatConfigId { get; set; }

    [JsonPropertyName("PolishConfigId")]
    public string? PolishConfigId { get; set; }

    [JsonPropertyName("HumanizePickerEnabled")]
    public bool HumanizePickerEnabled { get; set; } = true;

    [JsonPropertyName("HumanizeGuardCosineThreshold")]
    public float HumanizeGuardCosineThreshold { get; set; } = 0.85f;

    [JsonPropertyName("HumanizeGuardWindowChars")]
    public int HumanizeGuardWindowChars { get; set; } = 50;

    [JsonPropertyName("HumanizePickerChapterTimeoutMs")]
    public int HumanizePickerChapterTimeoutMs { get; set; } = 15000;
}
