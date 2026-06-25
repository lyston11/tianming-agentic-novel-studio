using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Interfaces;
using TM.Services.Modules.ProjectData.Models.Context;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class RelationStrengthService
    {
        private readonly IRelationStrengthSourceService _source;
        private readonly SemaphoreSlim _indexLoadLock = new(1, 1);
        private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(30);
        private RelationStrengthIndex? _cachedIndex;
        private DateTime _cacheExpiry = DateTime.MinValue;
        private int _epoch;

        public RelationStrengthService(IRelationStrengthSourceService source)
        {
            _source = source;
        }

        public async Task<RelationStrength> GetStrengthAsync(string id1, string id2)
        {
            if (string.IsNullOrWhiteSpace(id1) || string.IsNullOrWhiteSpace(id2))
                return RelationStrength.Weak;

            var index = await GetOrBuildIndexAsync().ConfigureAwait(false);
            return index.GetStrength(id1, id2);
        }

        public void InvalidateCache()
        {
            Interlocked.Increment(ref _epoch);
            _cachedIndex = null;
            _cacheExpiry = DateTime.MinValue;
            _source.InvalidateCache();
            TM.App.Log("[RelationStrengthService] 缓存已清除");
        }

        public async Task<RelationStrengthIndex> BuildStrengthIndexAsync()
        {
            var index = new RelationStrengthIndex();
            var facts = await _source.LoadRelationStrengthFactsAsync().ConfigureAwait(false);
            foreach (var fact in facts)
            {
                if (string.IsNullOrWhiteSpace(fact.LeftId) ||
                    string.IsNullOrWhiteSpace(fact.RightId))
                {
                    continue;
                }

                index.UpgradeStrength(fact.LeftId, fact.RightId, fact.Strength);
            }

            TM.App.Log($"[RelationStrengthService] 索引构建完成，共{index.Pairs.Count}条关联");
            return index;
        }

        private async Task<RelationStrengthIndex> GetOrBuildIndexAsync()
        {
            if (_cachedIndex != null && _cacheExpiry > DateTime.UtcNow)
                return _cachedIndex;

            await _indexLoadLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_cachedIndex != null && _cacheExpiry > DateTime.UtcNow)
                    return _cachedIndex;

                var epoch = Volatile.Read(ref _epoch);
                var index = await BuildStrengthIndexAsync().ConfigureAwait(false);
                if (epoch == Volatile.Read(ref _epoch))
                {
                    _cachedIndex = index;
                    _cacheExpiry = DateTime.UtcNow.Add(_cacheDuration);
                }

                return index;
            }
            finally
            {
                _indexLoadLock.Release();
            }
        }
    }

    public class RelationStrengthIndex
    {
        [System.Text.Json.Serialization.JsonPropertyName("Pairs")] public Dictionary<string, RelationStrength> Pairs { get; set; } = new();

        public void AddPair(string id1, string id2, RelationStrength strength)
        {
            var key = GetKey(id1, id2);
            Pairs[key] = strength;
        }

        public void UpgradeStrength(string id1, string id2, RelationStrength newStrength)
        {
            var key = GetKey(id1, id2);
            if (!Pairs.TryGetValue(key, out var cur) || cur < newStrength)
                Pairs[key] = newStrength;
        }

        public void EnsureMinStrength(string id1, string id2, RelationStrength minStrength)
        {
            var key = GetKey(id1, id2);
            if (!Pairs.ContainsKey(key))
                Pairs[key] = minStrength;
        }

        public RelationStrength GetStrength(string id1, string id2)
        {
            var key = GetKey(id1, id2);
            return Pairs.GetValueOrDefault(key, RelationStrength.Weak);
        }

        private string GetKey(string id1, string id2)
        {
            return string.Compare(id1, id2, StringComparison.Ordinal) < 0 ? $"{id1}_{id2}" : $"{id2}_{id1}";
        }
    }
}
