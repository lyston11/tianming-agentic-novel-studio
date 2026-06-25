using Microsoft.Extensions.DependencyInjection;
using TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel;
using TM.Web.NovelAgentWeb.Services.Production;
using Xunit;

namespace Tests.Unit.Services.Production;

public sealed class ProductionKernelServiceCollectionExtensionsTests
{
    [Fact]
    public void AddTianmingProductionKernelServices_RegistersCurrentRepositoryKernelComponentsAndGuard()
    {
        var services = new ServiceCollection();

        services.AddTianmingProductionKernelServices();

        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType == typeof(ITianmingProductionKernel));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRepositoryBoundaryGuard)
            && descriptor.ImplementationType == typeof(RepositoryBoundaryGuard));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IProductionKernelAssemblyGuard)
            && descriptor.ImplementationType == typeof(ProductionKernelAssemblyGuard));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IWorkspaceProductionRuntimeBuilder)
            && descriptor.ImplementationType == typeof(WorkspaceProductionRuntimeBuilder));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IChapterDirectiveBuilder)
            && descriptor.ImplementationType == typeof(ChapterDirectiveBuilder));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IChapterPromptBuilder)
            && descriptor.ImplementationType == typeof(ChapterPromptBuilder));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IChapterGatekeeper)
            && descriptor.ImplementationType == typeof(ChapterGatekeeper));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IChapterFactWriter)
            && descriptor.ImplementationType == typeof(ChapterFactWriter));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IChapterRewriter)
            && descriptor.ImplementationType == typeof(ChapterRewriter));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IChapterPackageBuilder)
            && descriptor.ImplementationType == typeof(ChapterPackageBuilder));

        using var provider = services.BuildServiceProvider();
        var guard = provider.GetRequiredService<IProductionKernelAssemblyGuard>();
        guard.EnsureCurrentRepositoryKernel(typeof(HardcoreWritingProductionKernel));
        Assert.IsType<WorkspaceProductionRuntimeBuilder>(provider.GetRequiredService<IWorkspaceProductionRuntimeBuilder>());
        Assert.IsType<ChapterDirectiveBuilder>(provider.GetRequiredService<IChapterDirectiveBuilder>());
        Assert.IsType<ChapterPromptBuilder>(provider.GetRequiredService<IChapterPromptBuilder>());
        Assert.IsType<ChapterGatekeeper>(provider.GetRequiredService<IChapterGatekeeper>());
        Assert.IsType<ChapterFactWriter>(provider.GetRequiredService<IChapterFactWriter>());
        Assert.IsType<ChapterRewriter>(provider.GetRequiredService<IChapterRewriter>());
        Assert.IsType<ChapterPackageBuilder>(provider.GetRequiredService<IChapterPackageBuilder>());
    }

    [Fact]
    public void RepositoryBoundaryGuard_FlagsExternalProductionKernelIncludes()
    {
        var assembly = typeof(ProductionKernelAssemblyGuard).Assembly;
        var guardType = assembly.GetType("TM.Web.NovelAgentWeb.Services.Production.RepositoryBoundaryGuard");
        Assert.NotNull(guardType);

        var tempRoot = Path.Combine(Path.GetTempPath(), "repository-boundary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var projectPath = Path.Combine(tempRoot, "BorrowedKernel.csproj");
        var originalProjectMarker = string.Concat("tianming", "-novel-ai", "-writer");
        File.WriteAllText(projectPath, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <Compile Include="../{originalProjectMarker}/Kernel/Borrowed.cs" />
              </ItemGroup>
            </Project>
            """);

        try
        {
            var guard = Activator.CreateInstance(guardType!);
            var scan = guardType!.GetMethod("Scan", new[] { typeof(string) });
            Assert.NotNull(scan);

            var report = scan!.Invoke(guard, new object[] { tempRoot });
            Assert.NotNull(report);

            var violations = (System.Collections.IEnumerable)report!.GetType()
                .GetProperty("Violations")!
                .GetValue(report)!;
            var messages = violations
                .Cast<object>()
                .Select(item => item.GetType().GetProperty("Message")!.GetValue(item)?.ToString() ?? string.Empty)
                .ToList();

            Assert.Contains(messages, message =>
                message.Contains("Compile", StringComparison.Ordinal)
                && message.Contains(originalProjectMarker, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
