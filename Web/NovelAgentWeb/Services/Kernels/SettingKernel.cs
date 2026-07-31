using System.Security.Cryptography;
using System.Text;
using TM.Web.NovelAgentWeb.Services.DomainEvents;

namespace TM.Web.NovelAgentWeb.Services.Kernels;

public sealed class SettingKernel : IKernel
{
    private readonly IKernelStructuredModelClient _model;

    public SettingKernel(IKernelStructuredModelClient model)
    {
        _model = model;
    }

    public string Name => "setting";

    public async Task<KernelExecutionOutput> ExecuteAsync(
        KernelExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var json = await _model.GenerateAsync(Name, context, cancellationToken);
        return KernelOutputFactory.Single(
            context,
            "SettingProposal",
            "SettingProposalProduced",
            "project_setting",
            context.Claim.ProjectId,
            json);
    }
}
