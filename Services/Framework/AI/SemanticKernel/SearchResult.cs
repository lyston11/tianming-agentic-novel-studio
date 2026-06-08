namespace TM.Services.Framework.AI.SemanticKernel
{
    public class SearchResult
    {
        public string ChapterId { get; set; } = string.Empty;
        public int Position { get; set; }
        public string Content { get; set; } = string.Empty;
        public double Score { get; set; }
    }
}
