namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class KnowledgeCatalogState
{
    public string UserId { get; set; } = null!;
    public long Revision { get; set; }
    public int ActiveEntryCount { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
