namespace TM.Web.NovelAgentWeb.Data.Entities;

public sealed class KnowledgeDocumentBlob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string MimeType { get; set; } = "application/octet-stream";
    public byte[] Data { get; set; } = [];
    public string ContentHash { get; set; } = string.Empty;
    public long KnowledgeVersion { get; set; }
    public string Status { get; set; } = "uploaded";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
