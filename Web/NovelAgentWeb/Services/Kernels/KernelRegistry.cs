namespace TM.Web.NovelAgentWeb.Services.Kernels;

public sealed class KernelRegistry
{
    private readonly IReadOnlyDictionary<string, IKernel> _kernels;

    public KernelRegistry(IEnumerable<IKernel> kernels)
    {
        _kernels = kernels.ToDictionary(kernel => kernel.Name, StringComparer.Ordinal);
    }

    public IKernel GetRequired(string name) =>
        _kernels.TryGetValue(name, out var kernel)
            ? kernel
            : throw new KeyNotFoundException($"未注册专业内核：{name}");
}
