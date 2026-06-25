namespace TM.Web.NovelAgentWeb.Services.Production;

public interface IWritingModelCompletionService
{
    Task<string> CompleteAsync(
        string userId,
        string system,
        string user,
        CancellationToken ct = default);
}
