using System;
using System.Reflection;
using System.Text.Json.Serialization;
using TM.Framework.Common.Models;

namespace TM.Services.Modules.ProjectData.Models.Common
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    public abstract class BusinessDataBase : IDataItem
    {
        [JsonPropertyName("Id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("Name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("Category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("CategoryId")]
        public string CategoryId { get; set; } = string.Empty;

        [JsonPropertyName("IsEnabled")]
        public bool IsEnabled { get; set; } = true;

        [JsonPropertyName("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [JsonPropertyName("UpdatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
