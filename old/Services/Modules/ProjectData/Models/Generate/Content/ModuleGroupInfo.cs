using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Generate.Content
{
    public class ModuleGroupInfo
    {
        [JsonPropertyName("ModuleType")]
        public string ModuleType { get; set; } = string.Empty;

        [JsonPropertyName("DisplayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("Icon")]
        public string Icon { get; set; } = "Icon.Clipboard";

        [JsonPropertyName("Cards")]
        public ObservableCollection<ModuleCardInfo> Cards { get; set; } = new();

        public string EnabledCountText => $"{Cards.Where(c => c.IsEnabled).Count()}/{Cards.Count}";
    }
}
