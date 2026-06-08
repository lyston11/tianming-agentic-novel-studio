using System.Collections.Frozen;
using System.Collections.Generic;

namespace TM.Services.Modules.ProjectData.Implementations.Generation
{
    public static class HighRiskWords
    {
        public static readonly FrozenSet<string> Set = BuildSet();

        public static bool IsHighRisk(string? key)
        {
            return !string.IsNullOrEmpty(key) && Set.Contains(key);
        }

        private static FrozenSet<string> BuildSet()
        {
            var words = new HashSet<string>
            {
                "应用", "运行", "处理", "调用", "查询", "请求", "响应",
                "部署", "开发", "实现", "配置", "参数", "接口", "模块",
                "系统", "平台", "用户", "设备", "服务", "环境", "数据",
                "网络", "连接", "信号", "代码", "脚本", "程序",

                "关注", "思考", "表示", "表现", "显示", "出现", "发现",
                "形成", "构建", "创建", "维护", "支持", "保持",
                "导致", "造成", "推动", "促进", "影响",
                "面对", "应对", "对待", "处置",

                "试试", "看看", "想想", "听听", "走走",

                "应用程序", "运行环境", "处理流程", "数据结构", "用户体验",
                "试试看", "看一看", "等一等",

                "意识", "认识", "理解", "明白", "知道",
                "感觉", "觉得", "认为", "以为",
                "意味", "代表", "象征",
            };

            return words.ToFrozenSet();
        }
    }
}
