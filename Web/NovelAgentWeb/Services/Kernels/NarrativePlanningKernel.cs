namespace TM.Web.NovelAgentWeb.Services.Kernels;

public sealed class NarrativePlanningKernel : IKernel
{
    private readonly IKernelStructuredModelClient _model;

    public NarrativePlanningKernel(IKernelStructuredModelClient model)
    {
        _model = model;
    }

    public string Name => "narrative_planning";

    public async Task<KernelExecutionOutput> ExecuteAsync(
        KernelExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var json = await _model.GenerateAsync(Name, context, cancellationToken);
        var artifactType = context.Claim.TaskType switch
        {
            "AnalyzeCreativeRequirements" => "CreativeRequirements",
            "CompileBatchPlan" => "BatchPlan",
            "PlanChapter" => "ChapterPlan",
            "BatchImpactAnalysis" => "BatchImpactReport",
            _ => "NarrativePlanArtifact"
        };
        return KernelOutputFactory.Single(
            context,
            artifactType,
            $"{artifactType}Produced",
            "goal_task",
            context.Claim.TaskId,
            json);
    }
}
