using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class GuideManager
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, object> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _dirtyKeys = new(StringComparer.OrdinalIgnoreCase);

        public Task<T> GetGuideAsync<T>(string fileName) where T : new()
        {
            lock (_lock)
            {
                if (!_cache.TryGetValue(fileName, out var cached))
                {
                    cached = new T();
                    _cache[fileName] = cached;
                }

                return Task.FromResult((T)cached);
            }
        }

        public void MarkDirty(string fileName)
        {
            lock (_lock)
            {
                _dirtyKeys.Add(fileName);
            }
        }

        public Task ClearDirtyMarksAsync()
        {
            lock (_lock)
            {
                _dirtyKeys.Clear();
            }

            return Task.CompletedTask;
        }

        public void ClearCache()
        {
            lock (_lock)
            {
                _cache.Clear();
                _dirtyKeys.Clear();
            }
        }

        public static string GetVolumeFileName(string baseFileName, int volumeNumber)
        {
            var ext = Path.GetExtension(baseFileName);
            var name = Path.GetFileNameWithoutExtension(baseFileName);
            return $"{name}_vol{volumeNumber}{ext}";
        }

        public List<int> GetExistingVolumeNumbers(string baseFileName)
        {
            lock (_lock)
            {
                var prefix = Path.GetFileNameWithoutExtension(baseFileName) + "_vol";
                return _cache.Keys
                    .Select(key => Path.GetFileNameWithoutExtension(key))
                    .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .Select(key => key[prefix.Length..])
                    .Select(suffix => int.TryParse(suffix, out var number) ? number : 0)
                    .Where(number => number > 0)
                    .Distinct()
                    .OrderBy(number => number)
                    .ToList();
            }
        }

        public void EvictCache<T>(string fileName, T newValue) where T : class
        {
            lock (_lock)
            {
                _cache[fileName] = newValue;
                _dirtyKeys.Remove(fileName);
            }
        }
    }
}
