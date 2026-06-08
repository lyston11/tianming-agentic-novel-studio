using System.Text.Encodings.Web;
using System.Text.Json;

namespace TM
{
    public static class App
    {
        public static List<string> Logs { get; } = new();

        public static void Log(string message)
        {
            Logs.Add(message);
        }
    }
}

namespace TM.Framework.Common.Helpers
{
    public static class JsonHelper
    {
        public static JsonSerializerOptions Default { get; } = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public static JsonSerializerOptions CnDefault { get; } = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }
}

namespace TM.Framework.Common.Helpers.Storage
{
    public static class StoragePathHelper
    {
        private static string _currentProjectName = "RegressionProject";

        public static event Action<string, string>? CurrentProjectChanged;

        public static string TestStorageRoot { get; set; } =
            Path.Combine(Path.GetTempPath(), "tianming-agentic-novel-regression", Guid.NewGuid().ToString("N"));

        public static string CurrentProjectName
        {
            get => _currentProjectName;
            set
            {
                if (string.IsNullOrWhiteSpace(value) || string.Equals(_currentProjectName, value, StringComparison.Ordinal))
                    return;

                var old = _currentProjectName;
                _currentProjectName = value;
                CurrentProjectChanged?.Invoke(old, value);
            }
        }

        public static string GetStorageRoot()
        {
            Directory.CreateDirectory(TestStorageRoot);
            return TestStorageRoot;
        }

        public static string GetFilePath(string layer, string subPath, string fileName)
        {
            var path = Path.Combine(TestStorageRoot, "Projects", CurrentProjectName, layer, NormalizeSubPath(subPath));
            Directory.CreateDirectory(path);
            return Path.Combine(path, fileName);
        }

        public static void Reset(string projectName)
        {
            TestStorageRoot = Path.Combine(Path.GetTempPath(), "tianming-agentic-novel-regression", Guid.NewGuid().ToString("N"));
            CurrentProjectName = projectName;
            Directory.CreateDirectory(TestStorageRoot);
        }

        private static string NormalizeSubPath(string subPath)
        {
            return string.IsNullOrWhiteSpace(subPath)
                ? string.Empty
                : subPath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        }
    }
}

namespace TM.Framework.Common.Services
{
    public static class ServiceLocator
    {
        public static T Get<T>() where T : class
        {
            return Activator.CreateInstance<T>();
        }

        public static T? TryGet<T>() where T : class
        {
            try
            {
                return Activator.CreateInstance<T>();
            }
            catch
            {
                return null;
            }
        }
    }
}

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    public sealed class WriterPlugin
    {
        public Task<string> GenerateChapterAsync(CancellationToken ct, string chapterId = "")
        {
            return Task.FromResult($"Regression writer generated {chapterId}.");
        }
    }
}

namespace TM.Services.Framework.AI.NovelAgent.Services
{
    using TM.Services.Framework.AI.NovelAgent.Models;

    public sealed class ChapterPostGenerationReviewer
    {
        public Task<NovelAgentPostGenerationReview> ReviewAsync(NovelAgentRun run, CancellationToken ct = default)
        {
            return Task.FromResult(new NovelAgentPostGenerationReview
            {
                ChapterId = run.TargetChapterId,
                OverallResult = "Pass",
                QualityScore = 92,
                Summary = "Regression reviewer accepted the generated chapter.",
                RequiresRewrite = false
            });
        }
    }

    public sealed class NovelAgentRewriteLoopService
    {
        public Task<NovelAgentRewriteAttempt> RewriteOnceAsync(NovelAgentRun run, CancellationToken ct = default)
        {
            return Task.FromResult(new NovelAgentRewriteAttempt
            {
                ChapterId = run.TargetChapterId,
                Success = true,
                BeforeQualityScore = run.PostGenerationReview?.QualityScore ?? 0,
                AfterQualityScore = 95,
                ReviewAfterRewrite = new NovelAgentPostGenerationReview
                {
                    ChapterId = run.TargetChapterId,
                    OverallResult = "Pass",
                    QualityScore = 95,
                    Summary = "Regression rewrite passed.",
                    RequiresRewrite = false
                }
            });
        }
    }
}

namespace TM.Tests.NovelAgentRegression
{
    internal sealed class RegressionAssertException : Exception
    {
        public RegressionAssertException(string message) : base(message)
        {
        }
    }

    internal static class Check
    {
        public static void True(bool condition, string message)
        {
            if (!condition)
                throw new RegressionAssertException(message);
        }

        public static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new RegressionAssertException($"{message} Expected: {expected}; Actual: {actual}");
        }

        public static void Contains(string expectedFragment, string actual, string message)
        {
            if (actual?.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase) != true)
                throw new RegressionAssertException($"{message} Missing fragment: {expectedFragment}; Actual: {actual}");
        }
    }
}
