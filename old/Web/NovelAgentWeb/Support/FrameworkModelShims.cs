using System.Security.Cryptography;
using System.Text;

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
            if (value.Length >= 12 && char.IsUpper(value[0])) return true;
            return false;
        }

        public static string New(string prefix) => NormalizePrefix(prefix) + GenerateRandomId();

        public static Guid NewGuid() => Guid.NewGuid();

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

namespace TM.Modules.Generate.Elements.VolumeDesign.Services
{
    public sealed class VolumeDesignService
    {
        private readonly List<TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign.VolumeDesignData> _items = new();

        public Task InitializeAsync() => Task.CompletedTask;

        public IEnumerable<TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign.VolumeDesignData> GetAllVolumeDesigns() =>
            _items;

        public Task AddVolumeDesignAsync(TM.Services.Modules.ProjectData.Models.Generate.VolumeDesign.VolumeDesignData item)
        {
            if (item == null)
                return Task.CompletedTask;

            _items.RemoveAll(existing => string.Equals(existing.Id, item.Id, StringComparison.OrdinalIgnoreCase));
            _items.Add(item);
            return Task.CompletedTask;
        }

        public void ClearAllVolumeDesigns() => _items.Clear();
    }
}
