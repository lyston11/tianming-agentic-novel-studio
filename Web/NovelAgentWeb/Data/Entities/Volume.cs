namespace TM.Web.NovelAgentWeb.Data.Entities;

public class Volume
{
    public string Id { get; set; } = null!;
    public string ProjectId { get; set; } = null!;
    public string Title { get; set; } = null!;
    public int VolumeNumber { get; set; }
    public string? Summary { get; set; }

    // Navigation properties
    public NovelProject Project { get; set; } = null!;
    public ICollection<Chapter> Chapters { get; set; } = new List<Chapter>();
}
