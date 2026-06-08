using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TM.Framework.Common.Helpers.Storage;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign;
using TM.Services.Modules.ProjectData.Models.Generated;

namespace TM.Framework.Common.Models
{
    public interface IEnableable
    {
        bool IsEnabled { get; set; }
    }

    public interface IDataItem : IEnableable
    {
        string Id { get; set; }
        string Name { get; set; }
        string Category { get; set; }
        string CategoryId { get; set; }
    }

    public interface ICategory : IEnableable
    {
        string Id { get; set; }
        string Name { get; }
        string Icon { get; }
        string? ParentCategory { get; }
        int Level { get; }
        int Order { get; set; }
        bool IsBuiltIn { get; set; }
    }

    public interface IDependencyTracked
    {
        string Id { get; set; }
        Dictionary<string, int> DependencyModuleVersions { get; set; }
    }
}

namespace TM.Framework.Common.Helpers.Id
{
    public static class ShortIdGenerator
    {
        private const int TimestampBits = 41;
        private const int RandomBits = 19;
        private const int Base32Bits = 5;
        private const int IdLength = 12;
        private const ulong RandomMask = (1UL << RandomBits) - 1;
        private static readonly char[] Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ".ToCharArray();
        private static readonly object LockObj = new();
        private static long _lastTimestamp;
        private static ulong _lastRandom;

        public static bool IsLikelyId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (Guid.TryParse(value, out _)) return true;
            if (value.Length == 13 && char.IsUpper(value[0])) return true;
            return false;
        }

        public static string New(string prefix) => NormalizePrefix(prefix) + GenerateRandomId();

        public static string NewDeterministic(string prefix, string seed)
        {
            var bytes = Encoding.UTF8.GetBytes($"{NormalizePrefix(prefix)}|{seed}");
            var hash = SHA256.HashData(bytes);
            ulong value = 0;
            for (var i = 0; i < 8; i++) value = (value << 8) | hash[i];
            value &= (1UL << (TimestampBits + RandomBits)) - 1;
            return NormalizePrefix(prefix) + EncodeBase32(value);
        }

        private static string NormalizePrefix(string prefix) =>
            string.IsNullOrWhiteSpace(prefix) ? string.Empty : char.ToUpperInvariant(prefix.Trim()[0]).ToString();

        private static string GenerateRandomId()
        {
            ulong value;
            lock (LockObj)
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (timestamp < _lastTimestamp) timestamp = _lastTimestamp + 1;
                var random = NextRandom19();
                if (timestamp == _lastTimestamp && random == _lastRandom)
                    random = (random + 1) & RandomMask;
                _lastTimestamp = timestamp;
                _lastRandom = random;
                value = ((ulong)timestamp << RandomBits) | random;
            }
            return EncodeBase32(value);
        }

        private static ulong NextRandom19()
        {
            Span<byte> buffer = stackalloc byte[4];
            RandomNumberGenerator.Fill(buffer);
            return BitConverter.ToUInt32(buffer) & RandomMask;
        }

        private static string EncodeBase32(ulong value)
        {
            Span<char> buffer = stackalloc char[IdLength];
            for (var i = IdLength - 1; i >= 0; i--)
            {
                buffer[i] = Alphabet[(int)(value & 31UL)];
                value >>= Base32Bits;
            }
            return new string(buffer);
        }
    }
}

namespace TM.Services.Modules.ProjectData.Implementations
{
    public sealed class GeneratedContentService : IGeneratedContentService
    {
        public async Task SaveChapterAsync(string chapterId, string content)
        {
            var path = GetChapterPath(chapterId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content).ConfigureAwait(false);
        }

        public async Task<string?> GetChapterAsync(string chapterId)
        {
            var path = GetChapterPath(chapterId);
            return File.Exists(path) ? await File.ReadAllTextAsync(path).ConfigureAwait(false) : null;
        }

        public async Task<List<ChapterInfo>> GetGeneratedChaptersAsync()
        {
            var dir = StoragePathHelper.GetProjectChaptersPath();
            var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.md") : Array.Empty<string>();
            var chapters = new List<ChapterInfo>();
            foreach (var file in files)
            {
                var id = Path.GetFileNameWithoutExtension(file);
                var content = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                var (vn, cn) = ParseChapterId(id);
                chapters.Add(new ChapterInfo
                {
                    Id = id,
                    Title = content.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("#", StringComparison.Ordinal))?.Trim(' ', '#') ?? id,
                    VolumeNumber = vn,
                    ChapterNumber = cn,
                    WordCount = content.Length,
                    CreatedTime = File.GetCreationTime(file),
                    ModifiedTime = File.GetLastWriteTime(file),
                    FilePath = file
                });
            }
            return chapters.OrderBy(c => c.VolumeNumber).ThenBy(c => c.ChapterNumber).ToList();
        }

        public Task<bool> DeleteChapterAsync(string chapterId)
        {
            var path = GetChapterPath(chapterId);
            if (!File.Exists(path)) return Task.FromResult(false);
            File.Delete(path);
            return Task.FromResult(true);
        }

        public bool ChapterExists(string chapterId) => File.Exists(GetChapterPath(chapterId));

        public string GetChapterPath(string chapterId) =>
            Path.Combine(StoragePathHelper.GetProjectChaptersPath(), $"{chapterId}.md");

        public Task<bool> VolumeExistsAsync(int volumeNumber) =>
            Task.FromResult(Directory.GetFiles(StoragePathHelper.GetProjectChaptersPath(), "*.md")
                .Any(f => ParseChapterId(Path.GetFileNameWithoutExtension(f)).Volume == volumeNumber));

        public Task<string> GenerateNextChapterIdFromSourceAsync(string sourceChapterId)
        {
            var (_, chapter) = ParseChapterId(sourceChapterId);
            return Task.FromResult($"chapter-{chapter + 1:000}");
        }

        private static (int Volume, int Chapter) ParseChapterId(string id)
        {
            var digits = new string((id ?? string.Empty).Where(char.IsDigit).ToArray());
            var chapter = int.TryParse(digits, out var n) ? n : 1;
            var volume = Math.Max(1, (chapter - 1) / 50 + 1);
            return (volume, chapter);
        }
    }
}

namespace TM.Modules.Generate.Elements.VolumeDesign.Services
{
    public sealed class VolumeDesignService
    {
        private List<VolumeDesignData>? _cache;

        public int CurrentVolumeNumber => GetAllVolumeDesigns().FirstOrDefault()?.VolumeNumber ?? 1;

        public Task InitializeAsync()
        {
            _cache = LoadVolumeDesigns();
            return Task.CompletedTask;
        }

        public IEnumerable<VolumeDesignData> GetAllVolumeDesigns() => _cache ??= LoadVolumeDesigns();

        public Task<int> GetTotalVolumeCountAsync() => Task.FromResult(GetAllVolumeDesigns().Count());

        public Task<int> GetVolumeMaxChapterAsync(int volumeNumber)
        {
            var design = GetAllVolumeDesigns().FirstOrDefault(v => v.VolumeNumber == volumeNumber);
            if (design?.EndChapter > 0) return Task.FromResult(design.EndChapter);
            if (design?.StartChapter > 0 && design.TargetChapterCount > 0)
                return Task.FromResult(design.StartChapter + design.TargetChapterCount - 1);
            return Task.FromResult(volumeNumber * 50);
        }

        public Task AddVolumeDesignAsync(VolumeDesignData data)
        {
            if (data == null) return Task.CompletedTask;
            var list = GetAllVolumeDesigns().ToList();
            list.RemoveAll(v => v.VolumeNumber == data.VolumeNumber);
            list.Add(data);
            _cache = list.OrderBy(v => v.VolumeNumber).ToList();
            return Task.CompletedTask;
        }

        public void ClearAllVolumeDesigns() => _cache = new List<VolumeDesignData>();

        private static List<VolumeDesignData> LoadVolumeDesigns()
        {
            var result = new List<VolumeDesignData>();
            try
            {
                var storyBiblePath = Path.Combine(
                    StoragePathHelper.GetCurrentProjectPath(),
                    "Services",
                    "Framework",
                    "AI",
                    "NovelAgent",
                    "story_bible.json");
                if (!File.Exists(storyBiblePath))
                    return BuildDefaultVolumeDesigns();

                var json = File.ReadAllText(storyBiblePath);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("volumeArcs", out var arcs) || arcs.ValueKind != JsonValueKind.Array)
                    return BuildDefaultVolumeDesigns();

                foreach (var arc in arcs.EnumerateArray())
                {
                    var number = ExtractTrailingNumber(ReadString(arc, "volumeId"));
                    if (number <= 0) number = result.Count + 1;
                    var startChapter = ExtractTrailingNumber(ReadString(arc, "startChapterId"));
                    var endChapter = ExtractTrailingNumber(ReadString(arc, "endChapterId"));
                    var expected = ReadInt(arc, "expectedChapterCount");
                    result.Add(new VolumeDesignData
                    {
                        Id = ReadString(arc, "volumeId"),
                        Name = ReadString(arc, "title", $"第{number}卷"),
                        VolumeNumber = number,
                        VolumeTitle = ReadString(arc, "title", $"第{number}卷"),
                        VolumeTheme = ReadString(arc, "centralQuestion"),
                        StageGoal = ReadString(arc, "arcGoal"),
                        TargetChapterCount = expected,
                        StartChapter = startChapter,
                        EndChapter = endChapter,
                        MainConflict = ReadString(arc, "oppositionForce"),
                        KeyEvents = JoinArray(arc, "mustHaveReversals"),
                        ChapterAllocationOverview = JoinArray(arc, "chapterBlueprints"),
                        IsEnabled = true
                    });
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[VolumeDesignService] Web 读取 Story Bible 卷设计失败: {ex.Message}");
            }

            return result.Count > 0
                ? result.OrderBy(v => v.VolumeNumber).ToList()
                : BuildDefaultVolumeDesigns();
        }

        private static List<VolumeDesignData> BuildDefaultVolumeDesigns() =>
            new()
            {
                new VolumeDesignData
                {
                    Id = "volume-001",
                    Name = "第1卷",
                    VolumeNumber = 1,
                    VolumeTitle = "第1卷",
                    TargetChapterCount = 50,
                    StartChapter = 1,
                    EndChapter = 50,
                    IsEnabled = true
                }
            };

        private static string ReadString(JsonElement element, string name, string fallback = "")
        {
            if (!element.TryGetProperty(name, out var prop)) return fallback;
            return prop.ValueKind == JsonValueKind.String ? prop.GetString() ?? fallback : prop.ToString();
        }

        private static int ReadInt(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var prop)) return 0;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value)) return value;
            return int.TryParse(prop.ToString(), out var parsed) ? parsed : 0;
        }

        private static string JoinArray(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.Array) return string.Empty;
            return string.Join("；", prop.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        private static int ExtractTrailingNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            var digits = new string(value.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
            return int.TryParse(digits, out var n) ? n : 0;
        }
    }
}

namespace TM.Services.Modules.ProjectData.Implementations.Guides
{
    public static class MilestoneCondenser
    {
        public static Task<string?> GetEffectiveFilePathAsync(int volumeNumber) => Task.FromResult<string?>(null);

        public static void TryCondenseInBackground(int volumeNumber)
        {
        }
    }
}

namespace TM.Modules.Design.Elements.CharacterRules.Services
{
    public sealed class CharacterRulesService
    {
        public Task<Dictionary<string, (string Name, string Identity)>> BuildDesignCharacterMapAsync() =>
            Task.FromResult(new Dictionary<string, (string Name, string Identity)>(StringComparer.OrdinalIgnoreCase));
    }
}

namespace TM.Services.Framework.AI.NovelAgent.Services
{
    public sealed class ChapterPostGenerationReviewer
    {
        public Task<NovelAgentPostGenerationReview> ReviewAsync(NovelAgentRun run, CancellationToken ct = default)
        {
            return Task.FromResult(new NovelAgentPostGenerationReview
            {
                ChapterId = run.TargetChapterId,
                OverallResult = "Pass",
                QualityScore = 88,
                ContentLength = run.DraftArtifact?.CommittedContent.Length ?? 0,
                ValidationOverallResult = run.GateReport?.Status == "validated" ? "Pass" : "Warning",
                Summary = "Web reviewer adapter: 章节已按硬门禁结果生成复盘摘要。",
                RequiresRewrite = run.GateReport?.Status != "validated"
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
                Success = false,
                BeforeQualityScore = run.PostGenerationReview?.QualityScore ?? 0,
                AfterQualityScore = run.PostGenerationReview?.QualityScore ?? 0,
                ReviewAfterRewrite = run.PostGenerationReview
            });
        }
    }
}
