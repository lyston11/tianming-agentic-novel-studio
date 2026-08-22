namespace TM.Web.NovelAgentWeb.Services.Goals;

public static class BookProductionWorkflow
{
    public const string AcceptanceGate = "AcceptanceGate";
    public const string PrefixMerge = "PrefixMerge";

    public const string UserActor = "user";
    public const string AgentPolicyActor = "agent-policy";

    public static string RequireActor(string actor) => actor switch
    {
        UserActor => UserActor,
        AgentPolicyActor => AgentPolicyActor,
        _ => throw new ArgumentException("验收主体只能是 user 或 agent-policy。", nameof(actor))
    };
}
