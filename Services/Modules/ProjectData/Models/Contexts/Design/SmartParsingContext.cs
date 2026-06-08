using System.Collections.Generic;
using TM.Services.Modules.ProjectData.Models.Design.SmartParsing;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Contexts.Design
{
    public class SmartParsingContext
    {
        [JsonPropertyName("BookAnalyses")]
        public List<BookAnalysisData> BookAnalyses { get; set; } = new();
    }
}
