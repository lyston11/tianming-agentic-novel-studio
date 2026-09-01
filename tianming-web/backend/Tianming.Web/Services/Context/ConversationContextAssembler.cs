using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TM.Web.NovelAgentWeb.Data;
using TM.Web.NovelAgentWeb.Services.Memory;

namespace TM.Web.NovelAgentWeb.Services.Context;

public sealed class ConversationContextAssembler : IConversationContextAssembler
{
    private static readonly IReadOnlyList<ConversationCapability> GeneralCapabilities =
    [
        new("conversation.respond", true),
        new("project.catalog.read", true)
    ];

    private const string UnboundInstructions =
        "Help the user discuss and clarify their writing intent without assuming a project. " +
        "Only the supplied minimal project catalog may be used for project discovery; do not infer or activate a binding.";

    private const string BoundInstructions =
        "Use the server-bound project snapshot and its versioned sources. " +
        "Only invoke the project tools explicitly listed in this context.";

    private readonly NovelAgentDbContext _db;
    private readonly IChatHistoryRepository _history;
    private readonly IAgentContextAssembler _projectContexts;

    public ConversationContextAssembler(
        NovelAgentDbContext db,
        IChatHistoryRepository history,
        IAgentContextAssembler projectContexts)
    {
        _db = db;
        _history = history;
        _projectContexts = projectContexts;
    }

    public async Task<ConversationContext> BuildAsync(
        ConversationContextRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            throw new ArgumentException("A user id is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.SessionId))
            throw new ArgumentException("A session id is required.", nameof(request));

        var binding = await _db.AgentSessions.AsNoTracking()
            .Where(item => item.Id == request.SessionId && item.UserId == request.UserId)
            .Select(item => new
            {
                item.Id,
                item.ProjectId,
                item.BindingVersion,
                item.IsArchived,
                ProjectAccessible = item.ProjectId == null || _db.NovelProjects.Any(project =>
                    project.Id == item.ProjectId && project.UserId == request.UserId)
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Session {request.SessionId} was not found for the current user.");

        if (binding.IsArchived)
            throw new InvalidOperationException($"Session {request.SessionId} is archived.");
        if (!binding.ProjectAccessible)
            throw new KeyNotFoundException($"The project bound to session {request.SessionId} is unavailable to the current user.");

        var transcript = await _history.GetPromptWindowAsync(
                request.UserId,
                binding.ProjectId,
                request.SessionId,
                cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(binding.ProjectId))
        {
            var projects = await _db.NovelProjects.AsNoTracking()
                .Where(item => item.UserId == request.UserId)
                .OrderByDescending(item => item.UpdatedAt)
                .Select(item => new AccessibleProjectContext(item.Id, item.Title, item.Status, item.UpdatedAt))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            return new UnboundConversationContext
            {
                UserId = request.UserId,
                SessionId = request.SessionId,
                BindingVersion = binding.BindingVersion,
                SystemInstructions = UnboundInstructions,
                Transcript = transcript,
                GeneralCapabilities = GeneralCapabilities,
                AccessibleProjects = projects
            };
        }

        var bindingVersion = binding.BindingVersion.ToString(CultureInfo.InvariantCulture);
        var snapshot = await _projectContexts.BuildProjectSnapshotAsync(
                new ProjectContextSnapshotRequest(
                    request.UserId,
                    binding.ProjectId,
                    request.SessionId,
                    null,
                    request.Query,
                    request.KnowledgeLimit,
                    bindingVersion),
                cancellationToken)
            .ConfigureAwait(false);

        return new BoundConversationContext
        {
            UserId = request.UserId,
            SessionId = request.SessionId,
            BindingVersion = binding.BindingVersion,
            SystemInstructions = BoundInstructions,
            Transcript = transcript,
            GeneralCapabilities = GeneralCapabilities,
            Binding = new ConversationBindingPointer(binding.ProjectId, bindingVersion),
            ProjectSnapshot = snapshot
        };
    }
}
