using System;
using System.Collections.Generic;
using System.Linq;
using TM.Framework.Common.Constants;
using TM.Services.Modules.ProjectData.Models.Navigation;

namespace TM.Services.Modules.ProjectData.Helpers
{
    public static class NavigationConfigParser
    {
        private static List<ModuleInfo>? _cachedModules;

        private static readonly string[] BusinessModuleTypes = { "Design", "Generate", "Validate" };

        public static List<ModuleInfo> GetAllModules()
        {
            EnsureCacheValid();
            return _cachedModules ?? new List<ModuleInfo>();
        }

        public static List<ModuleInfo> GetModulesByType(string moduleType)
        {
            return GetAllModules()
                .Where(m => m.ModuleType.Equals(moduleType, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public static List<ModuleInfo> GetFunctionsBySubModule(string moduleType, string subModule)
        {
            return GetAllModules()
                .Where(m => m.ModuleType.Equals(moduleType, StringComparison.OrdinalIgnoreCase)
                         && m.SubModule.Equals(subModule, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public static string GetDisplayName(string functionName)
        {
            var module = GetAllModules()
                .FirstOrDefault(m => m.FunctionName.Equals(functionName, StringComparison.OrdinalIgnoreCase));
            return module?.DisplayName ?? functionName;
        }

        public static string GetStoragePath(string functionName)
        {
            var module = GetAllModules()
                .FirstOrDefault(m => m.FunctionName.Equals(functionName, StringComparison.OrdinalIgnoreCase));
            return module?.StoragePath ?? string.Empty;
        }

        public static string GetSubModuleDisplayName(string subModule)
        {
            var module = GetAllModules()
                .FirstOrDefault(m => m.SubModule.Equals(subModule, StringComparison.OrdinalIgnoreCase));
            return module?.SubModuleDisplayName ?? subModule;
        }

        public static List<(string SubModule, string DisplayName)> GetSubModules(string moduleType)
        {
            return GetModulesByType(moduleType)
                .GroupBy(m => m.SubModule)
                .Select(g => (g.Key, g.First().SubModuleDisplayName))
                .ToList();
        }

        public static void ClearCache()
        {
            _cachedModules = null;
            TM.App.Log("[NavigationConfigParser] 缓存已清除");
        }

        private static void EnsureCacheValid()
        {
            if (_cachedModules != null)
            {
                return;
            }

            _cachedModules = LoadAllModulesFromDefinitions();
            TM.App.Log($"[NavigationConfigParser] 已从硬编码加载 {_cachedModules.Count} 个模块配置");
        }

        private static List<ModuleInfo> LoadAllModulesFromDefinitions()
        {
            var modules = new List<ModuleInfo>();

            foreach (var moduleType in BusinessModuleTypes)
            {
                var moduleNav = NavigationDefinitions.GetModuleByName(moduleType);
                if (moduleNav == null) continue;

                foreach (var subModule in moduleNav.SubModules)
                {
                    var firstFunction = subModule.Functions.FirstOrDefault();
                    if (firstFunction == null) continue;

                    var subModuleName = ExtractSubModuleFromViewPath(firstFunction.ViewPath);

                    foreach (var function in subModule.Functions)
                    {
                        var functionName = ExtractFunctionFromViewPath(function.ViewPath);
                        var storagePath = BuildStoragePath(function.ViewPath);

                        modules.Add(new ModuleInfo
                        {
                            ModuleType = moduleType,
                            SubModule = subModuleName,
                            SubModuleDisplayName = subModule.Name,
                            FunctionName = functionName,
                            DisplayName = function.Name,
                            Icon = function.Icon,
                            StoragePath = storagePath,
                            ViewPath = function.ViewPath
                        });
                    }
                }
            }

            return modules;
        }

        private static string ExtractSubModuleFromViewPath(string viewPath)
        {
            var parts = viewPath.Split('/');
            return parts.Length >= 4 ? parts[3] : string.Empty;
        }

        private static string ExtractFunctionFromViewPath(string viewPath)
        {
            var parts = viewPath.Split('/');
            return parts.Length >= 5 ? parts[4] : string.Empty;
        }

        private static string BuildStoragePath(string viewPath)
        {
            var parts = viewPath.Split('/');
            if (parts.Length >= 5)
            {
                return $"{parts[1]}/{parts[2]}/{parts[3]}/{parts[4]}";
            }
            return string.Empty;
        }
    }
}
