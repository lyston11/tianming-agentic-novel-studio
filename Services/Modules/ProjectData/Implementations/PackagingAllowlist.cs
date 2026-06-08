using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public static class PackagingAllowlist
    {
        public static readonly Dictionary<string, HashSet<string>> SubModules = new()
        {
            ["Design"] = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            {
                "全局设定",
                "设计元素"
            },
            ["Generate"] = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            {
                "全书设定",
                "创作元素"
            }
        };
    }
}
