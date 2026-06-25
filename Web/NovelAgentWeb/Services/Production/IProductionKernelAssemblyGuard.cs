namespace TM.Web.NovelAgentWeb.Services.Production;

public interface IProductionKernelAssemblyGuard
{
    void EnsureCurrentRepositoryKernel(Type kernelType);
}
