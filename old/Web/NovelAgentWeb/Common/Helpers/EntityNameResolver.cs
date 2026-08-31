using System.Collections.Generic;

namespace TM.Framework.Common.Helpers
{
    public static class EntityNameResolver
    {
        private static readonly object _lock = new();
        private static Dictionary<string, string>? _characterMap;
        private static Dictionary<string, string>? _locationMap;
        private static Dictionary<string, string>? _factionMap;
        private static Dictionary<string, string>? _plotRuleMap;
        private static Dictionary<string, string>? _foreshadowingMap;
        private static Dictionary<string, string>? _conflictMap;
        private static Dictionary<string, string>? _worldRuleMap;
        private static Dictionary<string, string>? _volumeDesignMap;
        private static Dictionary<string, string>? _chapterPlanMap;
        private static Dictionary<string, string>? _blueprintMap;
        private static Dictionary<string, string>? _outlineMap;
        private static Dictionary<string, string>? _itemMap;
        private static Dictionary<string, string>? _secretMap;
        private static Dictionary<string, string>? _pledgeMap;
        private static Dictionary<string, string>? _deadlineMap;

        public static string Resolve(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId))
                return entityId;

            EnsureLoaded();

            lock (_lock)
            {
                if (_foreshadowingMap?.TryGetValue(entityId, out var fName) == true && !string.IsNullOrEmpty(fName))
                    return fName;
                if (_conflictMap?.TryGetValue(entityId, out var cName) == true && !string.IsNullOrEmpty(cName))
                    return cName;
                if (_characterMap?.TryGetValue(entityId, out var charName) == true && !string.IsNullOrEmpty(charName))
                    return charName;
                if (_locationMap?.TryGetValue(entityId, out var locName) == true && !string.IsNullOrEmpty(locName))
                    return locName;
                if (_factionMap?.TryGetValue(entityId, out var facName) == true && !string.IsNullOrEmpty(facName))
                    return facName;
                if (_plotRuleMap?.TryGetValue(entityId, out var plotName) == true && !string.IsNullOrEmpty(plotName))
                    return plotName;
                if (_worldRuleMap?.TryGetValue(entityId, out var worldName) == true && !string.IsNullOrEmpty(worldName))
                    return worldName;
                if (_volumeDesignMap?.TryGetValue(entityId, out var volDesignName) == true && !string.IsNullOrEmpty(volDesignName))
                    return volDesignName;
                if (_chapterPlanMap?.TryGetValue(entityId, out var chapterName) == true && !string.IsNullOrEmpty(chapterName))
                    return chapterName;
                if (_blueprintMap?.TryGetValue(entityId, out var bpName) == true && !string.IsNullOrEmpty(bpName))
                    return bpName;
                if (_outlineMap?.TryGetValue(entityId, out var outlineName) == true && !string.IsNullOrEmpty(outlineName))
                    return outlineName;
                if (_itemMap?.TryGetValue(entityId, out var itemName) == true && !string.IsNullOrEmpty(itemName))
                    return itemName;
                if (_secretMap?.TryGetValue(entityId, out var secretName) == true && !string.IsNullOrEmpty(secretName))
                    return secretName;
                if (_pledgeMap?.TryGetValue(entityId, out var pledgeName) == true && !string.IsNullOrEmpty(pledgeName))
                    return pledgeName;
                if (_deadlineMap?.TryGetValue(entityId, out var deadlineName) == true && !string.IsNullOrEmpty(deadlineName))
                    return deadlineName;
            }

            return "未知实体";
        }

        public static string ResolveCharacter(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId)) return entityId;
            EnsureLoaded();
            lock (_lock)
            {
                if (_characterMap?.TryGetValue(entityId, out var name) == true && !string.IsNullOrEmpty(name))
                    return name;
            }
            return "未知角色";
        }

        public static string ResolveForeshadowing(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId)) return entityId;
            EnsureLoaded();
            lock (_lock)
            {
                if (_foreshadowingMap?.TryGetValue(entityId, out var name) == true && !string.IsNullOrEmpty(name))
                    return name;
            }
            return "未知伏笔";
        }

        public static string ResolveConflict(string entityId)
        {
            if (string.IsNullOrWhiteSpace(entityId)) return entityId;
            EnsureLoaded();
            lock (_lock)
            {
                if (_conflictMap?.TryGetValue(entityId, out var name) == true && !string.IsNullOrEmpty(name))
                    return name;
            }
            return "未知冲突";
        }

        public static void Invalidate()
        {
            lock (_lock)
            {
                _characterMap = null;
                _locationMap = null;
                _factionMap = null;
                _plotRuleMap = null;
                _foreshadowingMap = null;
                _conflictMap = null;
                _worldRuleMap = null;
                _volumeDesignMap = null;
                _chapterPlanMap = null;
                _blueprintMap = null;
                _outlineMap = null;
                _itemMap = null;
                _secretMap = null;
                _pledgeMap = null;
                _deadlineMap = null;
            }
        }

        public static void PreloadInBackground()
        {
            _ = System.Threading.Tasks.Task.Run(EnsureLoaded);
        }

        private static void EnsureLoaded()
        {
            if (_characterMap != null && _foreshadowingMap != null && _conflictMap != null)
            {
                return;
            }

            InitializeEmptyMaps();
        }

        private static void InitializeEmptyMaps()
        {
            lock (_lock)
            {
                if (_characterMap != null && _foreshadowingMap != null && _conflictMap != null)
                {
                    return;
                }

                _characterMap = new Dictionary<string, string>();
                _locationMap = new Dictionary<string, string>();
                _factionMap = new Dictionary<string, string>();
                _plotRuleMap = new Dictionary<string, string>();
                _foreshadowingMap = new Dictionary<string, string>();
                _conflictMap = new Dictionary<string, string>();
                _worldRuleMap = new Dictionary<string, string>();
                _volumeDesignMap = new Dictionary<string, string>();
                _chapterPlanMap = new Dictionary<string, string>();
                _blueprintMap = new Dictionary<string, string>();
                _outlineMap = new Dictionary<string, string>();
                _itemMap = new Dictionary<string, string>();
                _secretMap = new Dictionary<string, string>();
                _pledgeMap = new Dictionary<string, string>();
                _deadlineMap = new Dictionary<string, string>();
            }
        }
    }
}
