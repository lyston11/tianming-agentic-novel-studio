using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace TM.Services.Framework.AI.SemanticKernel.Discovery
{
    public sealed class DiscoveryCache<T> where T : struct, IComparable<T>
    {
        private readonly ConcurrentDictionary<string, DiscoveryRecord<T>> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<DiscoverySource, TimeSpan> _ttlBySource = new()
        {
            [DiscoverySource.ErrorParsed] = TimeSpan.FromDays(180),
            [DiscoverySource.ProbedExact] = TimeSpan.FromDays(180),
            [DiscoverySource.ProbedBoundary] = TimeSpan.FromDays(30),
            [DiscoverySource.Declared] = TimeSpan.FromDays(30),
            [DiscoverySource.Family] = TimeSpan.FromDays(7),
            [DiscoverySource.Unknown] = TimeSpan.FromDays(1),
        };

        public event EventHandler<DiscoveryRecordChangedEventArgs<T>>? RecordChanged;

        public bool TryGet(string key, [NotNullWhen(true)] out DiscoveryRecord<T>? record)
        {
            record = null;
            if (string.IsNullOrEmpty(key)) return false;
            if (!_cache.TryGetValue(key, out var found) || found == null) return false;

            if (_ttlBySource.TryGetValue(found.Source, out var ttl)
                && DateTime.UtcNow - found.Timestamp > ttl)
            {
                _cache.TryRemove(key, out _);
                return false;
            }

            record = found;
            return true;
        }

        public bool Record(string key, T value, DiscoverySource source)
        {
            if (string.IsNullOrEmpty(key)) return false;

            var newRec = new DiscoveryRecord<T>(value, source, DateTime.UtcNow);
            var written = false;
            T? oldValue = null;
            DiscoverySource? oldSource = null;

            _cache.AddOrUpdate(key,
                _ =>
                {
                    written = true;
                    return newRec;
                },
                (_, existing) =>
                {
                    oldValue = existing.Value;
                    oldSource = existing.Source;

                    if (source > existing.Source)
                    {
                        written = true;
                        return newRec;
                    }
                    if (source == existing.Source && value.CompareTo(existing.Value) > 0)
                    {
                        written = true;
                        return newRec;
                    }
                    return existing;
                });

            if (written)
            {
                RecordChanged?.Invoke(this, new DiscoveryRecordChangedEventArgs<T>
                {
                    Key = key,
                    NewValue = value,
                    NewSource = source,
                    OldValue = oldValue,
                    OldSource = oldSource,
                });
            }
            return written;
        }

        public void ForceSet(string key, T value, DiscoverySource source)
        {
            if (string.IsNullOrEmpty(key)) return;
            _cache[key] = new DiscoveryRecord<T>(value, source, DateTime.UtcNow);
        }

        public bool TryRemove(string key)
            => !string.IsNullOrEmpty(key) && _cache.TryRemove(key, out _);

        public IReadOnlyDictionary<string, DiscoveryRecord<T>> Snapshot()
            => _cache.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        public void LoadSnapshot(IDictionary<string, DiscoveryRecord<T>> snapshot)
        {
            if (snapshot == null) return;
            _cache.Clear();
            foreach (var kv in snapshot)
            {
                if (string.IsNullOrEmpty(kv.Key) || kv.Value == null) continue;
                _cache[kv.Key] = kv.Value;
            }
        }

        public void Clear() => _cache.Clear();

        public int Count => _cache.Count;
    }

    public sealed class DiscoveryRecordChangedEventArgs<T> : EventArgs
        where T : struct, IComparable<T>
    {
        public string Key { get; init; } = string.Empty;
        public T NewValue { get; init; }
        public DiscoverySource NewSource { get; init; }
        public T? OldValue { get; init; }
        public DiscoverySource? OldSource { get; init; }
    }
}
