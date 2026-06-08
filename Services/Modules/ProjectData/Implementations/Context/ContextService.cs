using System;
using System.Text.Json;
using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class ContextService : IContextService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static readonly System.Text.RegularExpressions.Regex VolNumKeyRegex = new(@"(?:第\s*)?(\d+)\s*卷", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex VolPrefixRegex = new(@"^vol(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex VolChIdRegex = new(@"vol(\d+)_ch(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex VolCnNumRegex = new(@"第([一二三四五六七八九十]+)卷", System.Text.RegularExpressions.RegexOptions.Compiled);
        private static readonly System.Text.RegularExpressions.Regex VolDigitExtractRegex = new(@"(?:第\s*|[Vv]ol[_\s]?)(\d+)\s*卷?", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        private const string PackagedCacheLayer = "PackagedContext";
        private const string FuncDataCacheLayer = "FuncData";

        private readonly SessionContextCache _sessionCache;

        public ContextService(SessionContextCache sessionCache)
        {
            _sessionCache = sessionCache;
            GuideContextService.CacheInvalidated += (_, _) => { _sessionCache.InvalidateLayer(PackagedCacheLayer); _sessionCache.InvalidateLayer(FuncDataCacheLayer); };
            ModuleDataNotifier.DataSaved += () => _sessionCache.InvalidateLayer(FuncDataCacheLayer);

            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) => { _sessionCache.InvalidateLayer(PackagedCacheLayer); _sessionCache.InvalidateLayer(FuncDataCacheLayer); };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContextService] 订阅项目切换事件失败: {ex.Message}");
            }
        }

    }
}
