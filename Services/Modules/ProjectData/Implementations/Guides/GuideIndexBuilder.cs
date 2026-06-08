using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class GuideIndexBuilder
    {
        private readonly Func<string, bool>? _isModuleEnabled;

        private readonly Dictionary<string, object> _loadCache = new(StringComparer.OrdinalIgnoreCase);

        private static readonly object _debugLogLock = new();
        private static readonly System.Collections.Generic.HashSet<string> _debugLoggedKeys = new();

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

            System.Diagnostics.Debug.WriteLine($"[GuideIndexBuilder] {key}: {ex.Message}");
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static void EnsureRequiredIds<T>(IEnumerable<T> items, Func<T, string> idSelector, string label, Func<T, string>? nameSelector = null)
        {
            var missing = items
                .Where(item => string.IsNullOrWhiteSpace(idSelector(item)))
                .Select(item => nameSelector?.Invoke(item) ?? "<unknown>")
                .Distinct()
                .ToList();

            if (missing.Count > 0)
            {
                throw new InvalidOperationException($"打包失败：{label} 缺失Id -> {string.Join("、", missing)}");
            }
        }

        private static void EnsureRequiredCategoryIds<T>(IEnumerable<T> items, Func<T, string> categorySelector, Func<T, string> categoryIdSelector, string label, Func<T, string>? nameSelector = null)
        {
            var missing = items
                .Where(item => !string.IsNullOrWhiteSpace(categorySelector(item)) && string.IsNullOrWhiteSpace(categoryIdSelector(item)))
                .Select(item => nameSelector?.Invoke(item) ?? categorySelector(item))
                .Distinct()
                .ToList();

            if (missing.Count > 0)
            {
                throw new InvalidOperationException($"打包失败：{label} 缺失CategoryId -> {string.Join("、", missing)}");
            }
        }

        public GuideIndexBuilder(Func<string, bool>? isModuleEnabled = null)
        {
            _isModuleEnabled = isModuleEnabled;
        }

    }
}
