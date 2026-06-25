using System.Xml.Linq;

namespace TM.Web.NovelAgentWeb.Services.Production;

public sealed class RepositoryBoundaryGuard : IRepositoryBoundaryGuard
{
    private static readonly string OriginalProjectMarker = string.Concat("tianming", "-novel-ai", "-writer");
    private static readonly string[] ProjectFileExtensions = { ".csproj", ".props", ".targets" };
    private static readonly HashSet<string> LocalIncludeItems = new(StringComparer.Ordinal)
    {
        "ProjectReference",
        "Compile",
        "Content",
        "None",
        "EmbeddedResource"
    };

    private static readonly string[] ProductionRoots =
    {
        "Web",
        "Services"
    };

    private static readonly string[] SourceExtensions =
    {
        ".cs",
        ".csproj",
        ".props",
        ".targets",
        ".json",
        ".md",
        ".txt",
        ".prompt",
        ".yaml",
        ".yml"
    };

    public RepositoryBoundaryReport Scan(string repositoryRoot)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
            throw new ArgumentException("Repository root is required.", nameof(repositoryRoot));

        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Repository root does not exist: {root}");

        var violations = new List<RepositoryBoundaryViolation>();
        foreach (var file in EnumerateBoundaryFiles(root))
        {
            if (IsProjectMetadataFile(file))
            {
                violations.AddRange(ScanProjectFile(root, file));
            }

            if (IsScannableProductionText(file) && IsInsideProductionRoot(root, file))
            {
                violations.AddRange(ScanTextFile(root, file));
            }
        }

        return new RepositoryBoundaryReport(violations);
    }

    public void EnsureClean(string repositoryRoot)
    {
        var report = Scan(repositoryRoot);
        if (report.IsClean)
            return;

        var details = string.Join(Environment.NewLine, report.Violations.Select(v => $"- {v.Message}"));
        throw new InvalidOperationException($"Repository boundary check failed:{Environment.NewLine}{details}");
    }

    public static string ResolveRepositoryRoot(string startPath)
    {
        var current = Directory.Exists(startPath)
            ? new DirectoryInfo(Path.GetFullPath(startPath))
            : new FileInfo(Path.GetFullPath(startPath)).Directory;

        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, "tianming-agentic-novel-studio.sln")) ||
                Directory.Exists(Path.Combine(current.FullName, "Web")) && Directory.Exists(Path.Combine(current.FullName, "Services")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Path.GetFullPath(startPath);
    }

    private static IEnumerable<string> EnumerateBoundaryFiles(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (!IsIgnoredPath(file))
                yield return file;
        }
    }

    private static IReadOnlyList<RepositoryBoundaryViolation> ScanProjectFile(string repositoryRoot, string file)
    {
        var violations = new List<RepositoryBoundaryViolation>();
        XDocument document;
        try
        {
            document = XDocument.Load(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            violations.Add(new RepositoryBoundaryViolation(
                Path.GetRelativePath(repositoryRoot, file),
                "project_xml_parse",
                $"Cannot parse project metadata file {Path.GetRelativePath(repositoryRoot, file)}: {ex.Message}"));
            return violations;
        }

        var fileDirectory = Path.GetDirectoryName(file)!;
        foreach (var item in document.Descendants())
        {
            var itemName = item.Name.LocalName;
            if (!LocalIncludeItems.Contains(itemName))
                continue;

            var include = item.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include))
                continue;

            if (ContainsOriginalProjectMarker(include))
            {
                violations.Add(new RepositoryBoundaryViolation(
                    Path.GetRelativePath(repositoryRoot, file),
                    "original_project_include",
                    $"{itemName} Include points at forbidden original project path: {include}"));
                continue;
            }

            if (ContainsMsBuildProperty(include))
                continue;

            var normalizedInclude = include
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var fullIncludePath = Path.GetFullPath(Path.Combine(fileDirectory, normalizedInclude));
            if (!IsInsideDirectory(fullIncludePath, repositoryRoot))
            {
                violations.Add(new RepositoryBoundaryViolation(
                    Path.GetRelativePath(repositoryRoot, file),
                    "external_local_include",
                    $"{itemName} Include leaves current repository: {include}"));
            }
        }

        return violations;
    }

    private static IEnumerable<RepositoryBoundaryViolation> ScanTextFile(string repositoryRoot, string file)
    {
        var text = File.ReadAllText(file);
        if (ContainsOriginalProjectMarker(text))
        {
            yield return new RepositoryBoundaryViolation(
                Path.GetRelativePath(repositoryRoot, file),
                "original_project_path",
                $"Production runtime file contains forbidden original project marker: {Path.GetRelativePath(repositoryRoot, file)}");
        }
    }

    private static bool IsProjectMetadataFile(string file) =>
        ProjectFileExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);

    private static bool IsScannableProductionText(string file) =>
        SourceExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);

    private static bool IsInsideProductionRoot(string repositoryRoot, string file)
    {
        foreach (var productionRoot in ProductionRoots)
        {
            var root = Path.Combine(repositoryRoot, productionRoot);
            if (Directory.Exists(root) && IsInsideDirectory(file, root))
                return true;
        }

        return false;
    }

    private static bool ContainsOriginalProjectMarker(string value) =>
        value.Contains(OriginalProjectMarker, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsMsBuildProperty(string value) =>
        value.Contains("$(", StringComparison.Ordinal);

    private static bool IsInsideDirectory(string path, string directory)
    {
        var normalizedPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return normalizedPath.Equals(normalizedDirectory, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(normalizedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIgnoredPath(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(part =>
            part is "bin" or "obj" or "node_modules" or "dist" or ".git" or ".idea" ||
            string.Equals(part, "assets", StringComparison.OrdinalIgnoreCase) &&
            path.Contains($"{Path.DirectorySeparatorChar}wwwroot{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
    }
}
