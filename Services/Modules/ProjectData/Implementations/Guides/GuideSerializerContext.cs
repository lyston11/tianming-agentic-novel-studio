using System.Text.Json.Serialization;
using TM.Services.Modules.ProjectData.Models.Guides;

namespace TM.Services.Modules.ProjectData.Implementations
{
    [JsonSerializable(typeof(CharacterStateGuide))]
    [JsonSerializable(typeof(ConflictProgressGuide))]
    [JsonSerializable(typeof(LocationStateGuide))]
    [JsonSerializable(typeof(FactionStateGuide))]
    [JsonSerializable(typeof(TimelineGuide))]
    [JsonSerializable(typeof(ItemStateGuide))]
    [JsonSerializable(typeof(ForeshadowingStatusGuide))]
    [JsonSerializable(typeof(OutlineGuide))]
    [JsonSerializable(typeof(PlanningGuide))]
    [JsonSerializable(typeof(BlueprintGuide))]
    [JsonSerializable(typeof(ContentGuide))]
    [JsonSerializable(typeof(PlotPointsIndex))]
    [JsonSourceGenerationOptions(
        PropertyNameCaseInsensitive = true,
        WriteIndented = true)]
    internal partial class GuideSerializerContext : JsonSerializerContext
    {
    }
}
