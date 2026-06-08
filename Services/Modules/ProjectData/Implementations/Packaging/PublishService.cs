using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using TM.Services.Modules.ProjectData.Helpers;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Publishing;

namespace TM.Services.Modules.ProjectData.Implementations
{
    [Obfuscation(Feature = "controlflow", Exclude = true, ApplyToMembers = true)]
    public partial class PublishService : IPublishService
    {
        private static readonly Regex ChapterTitlePrefixRegex = new(@"^\s*第\s*[\d一二三四五六七八九十百千零]+\s*章\s*[：:、\-—–_]*\s*", RegexOptions.Compiled);
        private static readonly Regex PunctuationOnlyRegex = new(@"[\p{P}\p{S}\s]+", RegexOptions.Compiled);

        private readonly IChangeDetectionService _changeDetectionService;
        private readonly IGuideContextService _guideContextService;
        private ManifestInfo? _cachedManifest;

        private static readonly System.Threading.SemaphoreSlim _publishLock = new(1, 1);

        private static readonly object _debugLogLock = new();
        private static readonly System.Collections.Generic.HashSet<string> _debugLoggedKeys = new();

        private static readonly Dictionary<string, (int Words, DateTime Modified)> _wordCountCache = new();
        private static readonly object _wordCountCacheLock = new();

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (_debugLoggedKeys.Count >= 500 || !_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[PublishService] {key}: {ex.Message}");
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public PublishService(
            IChangeDetectionService changeDetectionService,
            IGuideContextService guideContextService)
        {
            _changeDetectionService = changeDetectionService;
            _guideContextService = guideContextService;

            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) =>
                {
                    _cachedManifest = null;
                    lock (_wordCountCacheLock) { _wordCountCache.Clear(); }
                    try
                    {
                        _guideContextService.ClearCache();
                    }
                    catch { }

                    DetectAndCleanupStagingResidue();
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] 订阅项目切换事件失败: {ex.Message}");
            }

            try { DetectAndCleanupStagingResidue(); }
            catch (Exception ex) { TM.App.Log($"[PublishService] 启动自愈检查失败（非致命）: {ex.Message}"); }
        }

        private void DetectAndCleanupStagingResidue()
        {
            try
            {
                var projectPath = StoragePathHelper.GetCurrentProjectPath();
                var stagingDir = System.IO.Path.Combine(projectPath, "Config.staging");
                var stagingManifest = System.IO.Path.Combine(projectPath, "manifest.staging.json");

                bool hasResidue = false;

                if (System.IO.Directory.Exists(stagingDir))
                {
                    try
                    {
                        System.IO.Directory.Delete(stagingDir, true);
                        hasResidue = true;
                        TM.App.Log("[PublishService] 检测并清理 Config.staging 残留（上次打包未完成）");
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[PublishService] 清理 Config.staging 残留失败: {ex.Message}");
                    }
                }

                if (System.IO.File.Exists(stagingManifest))
                {
                    try
                    {
                        System.IO.File.Delete(stagingManifest);
                        hasResidue = true;
                        TM.App.Log("[PublishService] 检测并清理 manifest.staging.json 残留");
                    }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[PublishService] 清理 manifest.staging.json 残留失败: {ex.Message}");
                    }
                }

                if (hasResidue)
                {
                    try
                    {
                        GlobalToast.Warning("打包残留已清理",
                            "检测到上次打包未完成（已自动清理 Config.staging 残留）。原数据未受影响，建议重新打包。", 6000);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[PublishService] staging 残留检测异常: {ex.Message}");
            }
        }

        private static Dictionary<string, HashSet<string>> PackageSubModuleAllowlist => PackagingAllowlist.SubModules;

        private List<PackageMapping> GetPackageMappings()
        {
            var mappings = new List<PackageMapping>();

            foreach (var pair in PackageSubModuleAllowlist)
            {
                var moduleType = pair.Key;
                var allowlist = pair.Value;
                var subModules = NavigationConfigParser.GetSubModules(moduleType);

                foreach (var (subModule, displayName) in subModules)
                {
                    if (!allowlist.Contains(displayName))
                        continue;

                    var modulePath = $"{moduleType}/{subModule}";
                    var status = _changeDetectionService.GetStatus(modulePath);
                    if (!status.IsEnabled)
                        continue;

                    var functions = NavigationConfigParser.GetFunctionsBySubModule(moduleType, subModule);
                    if (functions.Count == 0)
                    {
                        TM.App.Log($"[PublishService] 未找到功能: {moduleType}/{subModule}，跳过打包");
                        continue;
                    }
                    var functionNames = functions.Select(f => f.FunctionName).ToArray();

                    var targetFile = $"{subModule.ToLower()}.json";

                    mappings.Add(new PackageMapping(
                        moduleType,
                        subModule,
                        functionNames,
                        targetFile
                    ));
                }
            }

            return mappings;
        }

    }
}
