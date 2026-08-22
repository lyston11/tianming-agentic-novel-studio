using System.Collections.Generic;
using TM.Services.Modules.ProjectData.Models.Design.Templates;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Contexts.Design
{
    public class TemplatesContext
    {
        [JsonPropertyName("CreativeMaterials")]
        public List<CreativeMaterialData> CreativeMaterials { get; set; } = new();
    }
}
