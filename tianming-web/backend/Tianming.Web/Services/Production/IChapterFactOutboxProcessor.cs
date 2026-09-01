using TM.Web.NovelAgentWeb.Data.Entities;

namespace TM.Web.NovelAgentWeb.Services.Production;

public interface IChapterFactOutboxProcessor
{
    Task ProcessAsync(OutboxEvent evt, CancellationToken ct = default);
}
