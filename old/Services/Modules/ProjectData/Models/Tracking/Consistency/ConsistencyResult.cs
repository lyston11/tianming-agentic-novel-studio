using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace TM.Services.Modules.ProjectData.Models.Tracking
{
    public class ConsistencyResult
    {
        [JsonPropertyName("Success")] public bool Success { get; set; } = true;

        [JsonPropertyName("Issues")] public List<ConsistencyIssue> Issues { get; set; } = new();

        public void AddIssue(ConsistencyIssue issue)
        {
            Issues.Add(issue);
            Success = false;
        }

        public void AddIssue(string entityId, string issueType, string expected, string actual)
        {
            AddIssue(new ConsistencyIssue
            {
                EntityId = entityId,
                IssueType = issueType,
                Expected = expected,
                Actual = actual
            });
        }

        public bool HasIssues => Issues.Count > 0;

        public List<string> GetIssueDescriptions()
        {
            return Issues.Select(i => i.ToString()).ToList();
        }
    }
}
