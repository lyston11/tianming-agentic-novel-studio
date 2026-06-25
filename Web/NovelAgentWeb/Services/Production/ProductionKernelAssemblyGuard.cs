using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class ProductionKernelAssemblyGuard : IProductionKernelAssemblyGuard
{
    private const string CurrentKernelNamespace = "TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel";

    public void EnsureCurrentRepositoryKernel(Type kernelType)
    {
        if (kernelType.Namespace is null ||
            !kernelType.Namespace.StartsWith(CurrentKernelNamespace, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Production kernel type must live in current project namespace. Actual: {kernelType.FullName}");
        }

        if (kernelType.Assembly != typeof(ITianmingProductionKernel).Assembly)
        {
            throw new InvalidOperationException(
                $"Production kernel implementation must be compiled with the current project kernel contract. Actual assembly: {kernelType.Assembly.FullName}");
        }

        var assemblyLocation = kernelType.Assembly.Location;
        var originalProjectMarker = string.Concat("tianming", "-novel-ai", "-writer");
        if (assemblyLocation.Contains(originalProjectMarker, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Production kernel assembly cannot load from original project path: {assemblyLocation}");
        }

        if (assemblyLocation.Contains($"{Path.DirectorySeparatorChar}tmp{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Production kernel assembly cannot load from temporary path: {assemblyLocation}");
        }
    }
}
