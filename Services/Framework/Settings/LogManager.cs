using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TM.Framework.SystemSettings.Logging.LogFormat;
using TM.Framework.SystemSettings.Logging.LogLevel;
using TM.Framework.SystemSettings.Logging.LogOutput;
using TM.Framework.SystemSettings.Logging.LogRotation;

namespace TM.Services.Framework.Settings
{
    public partial class LogManager
    {
        public static volatile bool IsInitializing;

        private readonly struct LogEntry
        {
            public LogEntry(LogLevelEnum level, string formatted)
            {
                Level = level;
                Formatted = formatted;
            }

            public LogLevelEnum Level { get; }
            public string Formatted { get; }
        }

        private static readonly object _debugLogLock = new();
        private static readonly System.Collections.Generic.HashSet<string> _debugLoggedKeys = new();

        [GeneratedRegex(@"\{timestamp(?::([^}]+))?\}", RegexOptions.IgnoreCase)]
        private static partial Regex TimestampRegex();
        [GeneratedRegex(@"\{level(?::([^}]+))?\}", RegexOptions.IgnoreCase)]
        private static partial Regex LevelRegex();
        [GeneratedRegex(@"\{message\}", RegexOptions.IgnoreCase)]
        private static partial Regex MessageRegex();
        [GeneratedRegex(@"\{caller\}", RegexOptions.IgnoreCase)]
        private static partial Regex CallerRegex();
        [GeneratedRegex(@"\{threadid\}", RegexOptions.IgnoreCase)]
        private static partial Regex ThreadIdRegex();
        [GeneratedRegex(@"\{processid\}", RegexOptions.IgnoreCase)]
        private static partial Regex ProcessIdRegex();
        [GeneratedRegex(@"\{exception\}", RegexOptions.IgnoreCase)]
        private static partial Regex ExceptionRegex();
        [GeneratedRegex(@"vol\d+_ch\d+")]
        private static partial Regex NormalizeVolChRegex();
        [GeneratedRegex(@"\b\d+(\.\d+)?\b")]
        private static partial Regex NormalizeNumberRegex();
        [GeneratedRegex(@"vol\d+")]
        private static partial Regex NormalizeVolRegex();
        [GeneratedRegex(@"不存在:\s*.+")]
        private static partial Regex NormalizeMissingPathRegex();
        [GeneratedRegex(@"(任务完成|执行任务|进度更新|已加载|已预热|已就绪|已恢复|已初始化)[:：]\s*.+")]
        private static partial Regex NormalizeSuffixRegex();
        [GeneratedRegex(@"\[预热\]\s*.+")]
        private static partial Regex NormalizeWarmupRegex();
        [GeneratedRegex(@"失败\s*[=\uff1d]?\s*0(?!\d)")]
        private static partial Regex FailureZeroRegex();
        [GeneratedRegex(@"成功\s+\d+[，,]\s*失败\s+\d+")]
        private static partial Regex SuccessFailureCountRegex();
        [GeneratedRegex(@"https?://[^\s,|]+")]
        private static partial Regex NormalizeUrlRegex();
        [GeneratedRegex(@"[A-Za-z]:[/\\][^\s,|""<>*?]+")]
        private static partial Regex NormalizePathRegex();

        private static readonly string[] _globalDeduplicatePatterns =
        {
            "业务导航双击激活",
        };

        private static readonly string[] _longWindowDeduplicatePatterns =
        {
            "业务导航双击激活",
            "已标记层级失效",
            "TreeAfterAction触发",
            "SelectedPath变化",
            "加载供应商",
            "加载分类",
            "同步providers.json",
            "从 ProviderModels 目录加载模型",
            "异步懒加载供应商",
            "Plugin 'Writer' 注册跳过",
            "反射解析完成",
            "开始生成章节:",
            "Trace| RequestStart:",
            "Trace| RequestComplete:",
        };

        private static readonly string[] _mediumWindowDeduplicatePatterns =
        {
            "保存用户配置:",
            "模块版本自增:",
            "MaxTokens=AUTO",
            "Adaptive max_tokens",
            "注册跳过",
            "provider ok",
            "HTTP超时策略",
            "Kernel 构建成功",
            "已注册",
            "未在模型库中找到",
            "流式生成完成",
            "构建 Kernel:",
            "协议推断:",
            "使用自定义模型:",
            "Mode=Business",
            "Mode=Edit",
            "Mode=Chat",
            "AUTO使用已探测",
            "AUTO=兜底输出上限",
            "AUTO吃满可用空间",
            "淘汰 ",
            "裁剪完成:",
            "降级尝试模型",
            "跳过流式（标记剩余",
            "GenerateWithChatHistory 调用开始",
            "模型库未收录",
            "使用业务提示词模板",
            "已注入Spec模板原文",
            "DeepSeek模型字数补偿",
            "GPT模型字数补偿",
            "Token预估 ",
            "SaveBatchEntitiesAsync: 成功保存",
            "ParseBatchJsonResult: 成功解析",
            "识别到CHANGES区域",
            "未配置润色API",
            "归一化补丁(",
            "归一化歧义(",
            "FactLedger 跳过的空子块",
            "GenerateChapterByNumber:",
            "GenerateChapter (可取消):",
            "WriterPlugin| gen:",
            "WriterPlugin| RRF ",
            "开始流式接收",
            "已加载统计数据:",
            "开始生成章节:",
            "第1次生成",
            "BuildContentContextAsync:",
            "事实快照抽取完成:",
            "势力注入:",
            "物品注入:",
            "注入 DeepSeek-V4+",
            "前置字数检测：",
            "字数不足 ",
            "字数超限 ",
            "思考=默认（不注入字段",
            "刷新章节列表状态:",
            "模块启用状态已更新:",
            "已清理业务会话（前缀=",
            "下游影响提示:",
            "实体引用",
            "直连返回 HTML",
            "代理也返回 HTML",
            "构建上游索引:",
            "层级 SmartParsing",
            "设计上下文已构建:",
            "注入 thinking.type=enabled",
            "更新会话模式:",
            "开始收集执行轨迹",
            "停止收集，共",
            "warn: design",
            "窗口设置已保存",
            "窗口设置已更新",
            "检测到文件变化，已发布刷新事件",
            "打开工作台窗口",
            "Save 完成 path=",
            "skip: within limits",
            "加载项目Spec成功",
            "字数控制已关闭，跳过字数补偿",
            "GET /api/config/builtin-configs",
            "分类 public(tm-public-",
            "写入 built_in_categories.json",
            "级联清理分类:",
            "内置分类仅删除直属数据",
            "内置分类直属数据已清除",
            "异步懒加载供应商",
            "已缓存模型输出上限",
            "获取依赖快照:",
            "异步添加数据",
            "文件不存在，返回空对象:",
            "HumanizeRules.ApplyDictionary",
            "润前正则",
            "润后正则",
            "ContentCallback| vol_ch 校验通过",
            "ContentCallback| S1: vol_ch",
            "ContentCallback| S2: vol_ch",
            "ContentCallback| vol_ch 更新角色状态",
            "ContentCallback| vol_ch 更新冲突进度",
            "ContentCallback| vol_ch 更新地点状态",
            "ContentCallback| vol_ch 更新时间推进",
            "ContentCallback| vol_ch 更新角色位置",
            "ContentCallback| vol_ch 更新物品流转",
            "ContentCallback| vol_ch 更新秘密知情",
            "ContentCallback| vol_ch 更新势力状态",
            "ContentCallback| vol_ch 更新承诺/契约",
            "ContentCallback| vol_ch 添加关键情节",
            "ContentCallback| vol_ch done",
            "ContentCallback| vol_ch ok",
            "ContentCallback| vol_ch 向量索引建设完成",
            "ContentCallback| 已更新章节摘要:",
            "ContentCallback| 漂移检测",
            "ContentCallback| 已自动补录角色",
            "CharacterState| 已更新",
            "CharacterState| 自动注册新角色:",
            "LocationState| 已更新",
            "LocationState| 自动创建地点条目:",
            "ConflictProgress| 已更新",
            "ConflictProgress| 自动注册新冲突:",
            "ItemState| 已更新",
            "ItemState| 自动创建物品条目:",
            "SecretReveal| 自动创建秘密条目:",
            "SecretReveal| 新增知情者:",
            "TimelineService| 已更新",
            "FactionState| 自动创建势力条目:",
            "FactionState| 已更新",
            "PlotPoints| 已添加情节索引:",
            "PledgeConstraint| 创建承诺/契约:",
            "PledgeConstraint| 执行动作:",
            "WriterPlugin| gen ok:",
            "WriterPlugin| 章节生成成功:",
            "AutoRewriteEngine| 开始润色（共",
            "AutoRewriteEngine| 润色完成并通过校验",
            "AutoRewriteEngine| 蓝图合规 warn:",
            "ContentPolisher| 本地正则润色完成",
            "Trace| RequestStart:",
            "Trace| RequestComplete:",
            "Lifecycle| runId=",
            "GG| [no-correlation]",
            "GG| quality-warn:",
            "ThinkingRouter| 注入 EnableThinkingFlag",
            "FactSnapshotExtractor| 注入近期活跃角色:",
            "FactSnapshotExtractor| 注入近期活跃地点:",
            "FactSnapshotExtractor| 近期活跃势力补入优先池:",
            "FactSnapshotExtractor| 补充注入活跃角色描述:",
            "GuideContextService| 注入首次描写:",
            "GuideContextService| 检测到",
            "LayeredPromptBuilder| 势力注入:",
            "Pipeline: 开始批量生成,",
            "Pipeline: 批量生成完成, 耗时=",
            "开始AI生成: 分类=",
            "第 1 批完成: 生成=",
            "批量AI生成完成: 成功=",
            "BootstrapManager| 开始执行启动任务",
            "OAuthService| 初始化完成",
            "ApiService| POST /api/auth/login",
            "LoginService| API登录成功:",
            "LoginWindow| 登录成功:",
            "AuthStartupService| 认证服务已初始化",
            "HolidayLibrary| 初始化完成",
            "TimeScheduleService| 初始化完成",
            "SystemThemeMonitor| 初始化完成",
            "SceneDetector| 初始化完成",
            "SystemFollowController| 初始化完成",
            "LocaleSettings| async loaded",
            "ChapterEmbedding| 索引不存在，空载:",
            "ChunkEmbedding| 索引不存在，空载:",
            "SessionManager| 初始化完成，会话目录:",
            "SessionManager| 加载消息:",
            "SKChatService| 初始化",
            "SKConversationViewModel| 初始化完成",
            "StatisticsService| 加载了",
            "GuideContextService| 缓存初始化完成",
            "DataIndexService| 索引初始化完成:",
            "QueryRoutingService| 索引初始化完成:",
            "DI| 所有模块服务初始化完成",
            "ATM| loaded",
            "ATM| saved",
            "SA| sync",
            "AppLockSettings| init",
            "SAI| init",
            "ContentViewModel| 开始清除打包",
            "ContentViewModel| 开始打包",
            "PublishService| 已生成 staging manifest",
            "PublishService| staging 已原子转正",
            "GlobalCleanupService| 全局清理完成",
            "BusinessCleanupService| 开始执行业务清理",
            "BusinessCleanupService| 已清空",
            "BusinessCleanupService| 业务清理完成，共清空",
            "OneClickGenerate| 刷新重置完成",
            "VolumeDesignService| 分类删除事件已触发:",
            "[Icon.Rocket] 管线启动:",
            "[Icon.Refresh] 开始 #",
            "[Icon.Refresh] #",
            "[Icon.CheckCircle] #",
            "[Icon.CheckCircle] 完成",
            "[Icon.Save] #",
            "[Icon.Clipboard] #",
            "[Icon.ChevronRight] #",
            "[Icon.Forbidden] #",
            "ChapterPreviewViewModel| 加载成功:",
            "MilestoneStore| 已更新第",
            "CurrentChapterTracker| 当前章节:",
            "UIStateCache| 左栏状态已缓存:",
            "UIStateCache| 右栏状态已缓存:",
            "ChangeDetectionService| 刷新所有模块变更状态",
            "ChangeDetectionService| 刷新完成，共",
            "ChangeDetectionService| manifest.json不存在",
            "ChangeDetectionService| 加载上次打包时间:",
            "EndpointTestService| 家族短路决策",
            "EndpointTestService| /models 已提示",
            "EndpointTestService| 全部参数探测被",
            "EndpointTestService| Phase B 节省:",
            "EndpointTestService| 探测结果已缓存",
            "EndpointTestService| 已清空全部探测缓存",
            "ChapterMarkdownEditor| 切换到标签:",
            "MemoryOptimization| 用户空闲",
            "MemoryOptimization| 内存使用",
            "SessionManager| 保存消息:",
            "SessionManager| 创建会话:",
            "SessionManager| 删除会话:",
            "SessionManager| 当前会话已删除",
            "SKConversationViewModel| 启动：已恢复会话",
            "SKConversationViewModel| 启动：章节上下文恢复为",
            "SKConversationViewModel| 加载",
            "SKConversationViewModel| 第1章唯一匹配",
            "SKConversationViewModel| 从自然语言解析到章节",
            "SKConversationViewModel| 消息完成:",
            "SKConversationViewModel| 计划模式 -",
            "SKConversationViewModel| 代理模式 -",
            "SKConversationViewModel| Agent 模式开始",
            "SKConversationViewModel| 已取消生成",
            "SKConversationViewModel| 收到",
            "SKConversationViewModel| 推理参数已同步回ModelService",
            "SKConversationViewModel| 快捷思考开关",
            "SKConversationViewModel| 快捷推理强度:",
            "SKConversationViewModel| 防御:",
            "SKConversationViewModel| 发送消息:",
            "AIService| 添加配置:",
            "AIService| 更新配置:",
            "AIService| 删除配置:",
            "AIService| AI核心服务已初始化",
            "ModelManagement| 端点已保存:",
            "ModelManagement| Models 端点成功:",
            "ModelManagement| 开始获取全部模型:",
            "ModelManagement| 获取模型成功",
            "ModelManagement| 已为模型创建AIService配置:",
            "ModelManagement| 已更新AIService配置:",
            "ModelManagement| MaxTokens 已调整:",
            "ModelManagement| 探测写入 MaxOutput 缓存:",
            "ModelManagement| 模型参数探测完成:",
            "ModelManagement| 孤儿配置已清理:",
            "ModelService| 系统内置分类不可修改:",
            "ModelService| 级联删除:",
            "ModelService| GetModelsForProvider",
            "ModelService| 批量添加完成:",
            "ModelService| 更新数据",
            "ModelService| 添加分类:",
            "ModelService| 更新分类:",
            "ModelService| 启动时加载供应商",
            "PlanModeMapper| 基于打包数据生成计划步骤",
            "PlanModeMapper| 基于打包数据跳过模型调用",
            "PlanPayloadPublisher| 发布",
            "PlanViewModel| 从 Events 提取",
            "TodoExecutionService| 启动",
            "TodoExecutionService| 已请求取消",
            "ChapterListPanel| 全部删除完成:",
            "ChapterListPanel| 选择章节:",
            "ChapterListPanel| 加载了",
            "DataTreeView| HandleInternalDeleteAll",
            "WriterPlugin| 生成已取消",
            "GG| start: vol_ch",
            "GG| ok: vol_ch",
            "AutoRewriteEngine| 字数检测通过:",
            "AutoRewriteEngine| 字数超限",
            "AutoRewriteEngine| 第1次生成成功",
            "AutoRewriteEngine| 第2次生成",
            "AutoRewriteEngine| 第3次生成",
            "AutoRewriteEngine| AI请求失败，内部重试",
            "BookAnalysisViewModel| 已加载",
            "BookAnalysisView| 爬虫服务已注入",
            "BookAnalysisView| WebView2 已冻结",
            "SubscriptionService| 订阅数据已加载",
            "FocusContextService| TrackingStatus实时构建完成",
            "FocusContextService| 设计上下文已构建:",
            "GenerationStats| 已加载统计数据:",
            "GenerationStats| 记录生成:",
            "ContextService| 构建CoreDesignContext",
            "ContextService| 剧情按卷过滤:",
            "ContextService| 实体注入（按卷）",
            "ContextService| OPT-017",
            "StatisticsService| 记录API调用:",
            "GuideContextService| 章节 vol_ch 缺少MD上下文",
            "GuideContextService| 已触发全局缓存失效事件",
            "GuideContextService| 缓存已清除",
            "GuideContextService| content_guide 聚合",
            "IndexService| 构建上游索引:",
            "IndexService| 层级 SmartParsing",
            "GlobalSummaryService| cache miss",
            "GlobalSummaryService| 实时计算完成",
            "GlobalSummaryService| 缓存已清除",
            "PromptService| 嵌入加载系统内置模板:",
            "PromptService| 合并系统内置模板:",
            "PromptService| 业务提示词分类已有",
            "GuideManager| 加载成功:",
            "GuideManager| 批量保存完成，共",
            "GuideManager| P1:",
            "GuideManager| P2:",
            "ChapterEmbedding| Save 完成",
            "ChapterEmbedding| Load 完成",
            "ChapterEmbedding| 缓存已失效",
            "ChunkEmbedding| Save 完成",
            "ChunkEmbedding| Load 完成",
            "ChunkEmbedding| 缓存已失效",
            "ChunkEmbedding| 按章节清理",
            "FirstChapterIndex| 捕获",
            "FirstChapterIndex| Save 完成",
            "FirstChapterIndex| Load 完成",
            "FirstChapterIndex| Rebuild",
            "FirstChapterIndex| 按章节失效",
            "FirstChapterIndex| 缓存已失效",
            "KeywordIndex| 已索引",
            "MilestoneStore| 已追加",
            "SummaryStore| 已移除摘要:",
            "SessionCache| 已标记层级失效:",
            "SessionCache| 缓存已清空",
            "PackageHistoryService| 已删除 manifest.json",
            "PackageHistoryService| 已清除当前打包文件",
            "PackageHistoryService| 已清除所有历史记录",
            "PackageHistoryService| 已清除已生成章节",
            "PackageHistoryService| 已清除当前章节",
            "PublishService| 开始打包所有模块",
            "PublishService| 已打包:",
            "PublishService| 统计完成:",
            "PublishService| 已保存指导文件:",
            "PublishService| 已创建备份:",
            "PublishService| 已刷新并预热缓存",
            "PublishService| 打包完成",
            "PublishService| 缓存已清除",
            "PublishService| 已生成 staging manifest",
            "GuideIndexBuilder| 大纲指导构建完成",
            "GuideIndexBuilder| 规划指导构建完成",
            "GuideIndexBuilder| 蓝图指导构建完成",
            "GuideIndexBuilder| 正文指导构建完成",
            "GuideIndexBuilder| 伏笔完成度追踪初始化完成",
            "DataIndexService| 索引初始化完成:",
            "DataIndexService| 项目切换/缓存失效",
            "DataIndexService| 索引已清除",
            "QueryRoutingService| 索引初始化完成:",
            "RelationStrengthService| 缓存已清除",
            "DataEditPlugin| 缓存刷新完成",
            "DataEditPlugin| RollbackChange 完成:",
            "DataEditPlugin| ExecuteChange 完成:",
            "DataEditPlugin| ConfirmChange 完成:",
            "DataEditPlugin| ReconcileAllData 完成",
            "PendingChangeStore| 创建预览",
            "AccessRightsService| 缓存已清除",
            "GoogleAuthService| 缓存已清除",
            "CharacterState| 已移除章节",
            "LocationState| 已移除章节",
            "PlotPoints| 已移除章节",
            "ConflictProgress| 已移除章节",
            "ItemState| 已移除章节",
            "SecretReveal| 已移除章节",
            "TimelineService| 已移除章节",
            "FactionState| 已移除章节",
            "PledgeConstraint| 已移除章节",
            "GeneratedContentService| 删除章节:",
            "ChapterFileWatcher| 文件变化:",
            "ChapterFileWatcher| 开始监听:",
            "WorkspaceLayout| 检测到文件变化",
            "ProtectionService| 启动校验已跳过",
            "ProtectionService| 运行时保护已禁用",
            "ProtectionService| 跳过初始化",
            "ProjectManager| 启动时同步当前项目:",
            "NavigationConfigParser| 已从硬编码加载",
            "ProviderLogoHelper| 嵌入加载 Logo 映射:",
            "EndpointTestService| 持久化探测缓存已加载:",
            "AccountBindingSettings| 数据已延迟加载:",
            "LoginHistorySettings| 数据已延迟加载:",
            "NotificationHistorySettings| 数据已延迟加载:",
            "LoginHistoryService| 登录记录已保存:",
            "TrayIcon| 托盘图标已创建",
            "TrayIcon| 托盘图标已初始化",
            "AppLock| 自动锁定定时器已启动",
            "AppLock| init ok",
            "MainWindow| 已从共享设置加载窗口状态:",
            "FontManager| UI字体已应用:",
            "FontManager| 编辑器字体已应用:",
            "WindowsNotification| Windows原生通知已禁用",
            "SystemIntegration| 设置已加载并应用",
            "UIResolution| 配置文件不存在，使用默认设置",
            "LocaleService| 文化区域已应用:",
            "Reconciler| 开始一致性对账",
            "Reconciler| GuideManager pending flush 已检查/恢复",
            "Reconciler| 对账完成:",
            "Reconciler| done:",
            "Reconciler| 首次描写冷启动补建",
            "对账| 已自动修复:",
            "Bootstrap| UIResolutionService 已激活",
            "Bootstrap| TimeScheduleService 已激活",
            "Bootstrap| SystemFollowController 已激活",
            "Bootstrap| LocaleService 已激活",
            "DI| 依赖注入容器已初始化",
            "BootstrapManager| 任务完成:",
            "BootstrapManager| 并行执行批次:",
            "BootstrapManager| 所有启动任务执行完成",
            "BootstrapManager| [",
            "SplashWindow| 启动进度窗口已初始化",
            "SplashWindow| 进度更新:",
            "UIPreWarm| 弹窗/控件/模板预热完成",
            "UIPreWarm| Pre-JIT 完成:",
            "UIPreWarm| 优先视图预创建完成",
            "UsageStatistics| 视图已加载",
            "UsageStatistics| 统计数据已刷新",
            "UnifiedValidationService| 初始化完成",
            "ValidationSummaryService| 加载数据:",
            "VersionTestingViewModel| 读取提示词缓存成功",
            "VersionTestingViewModel| 当前提示词缓存数量:",
            "VersionTestingViewModel| 获取分类数据，共",
            "VersionTestingViewModel| 构建树形数据完成",
            "VersionTesting| 提示词版本测试视图已加载",
            "VersionTestingService| 数据文件不存在，初始化空列表",
            "PromptManagement| 提示词管理视图已加载",
            "ModelManagement| 模型管理视图已加载",
            "TimeScheduleService| 初始化完成",
            "MemoryOptimization| 服务已启动，GC间隔:",
            "内存管理| 内存优化服务已启动",
            "字体| UI字体:",
            "代理| 代理配置已加载",
            "组件| 3栏布局",
            "命令行| 调试模式已启用",
            "生成参数| 生成参数已从本地存储加载",
            "主题| 当前主题:",
            "主窗口| 托盘图标服务已初始化",
            "ThemeManager| 主题切换成功:",
            "NetworkMonitor| 已启动系统网络监测",
            "NetworkMonitor| 已停止系统网络监测",
            "HolidayLibrary| 初始化完成",
            "OAuthService| 初始化完成",
            "IpLocationHelper| IP2Region 初始化成功",
            "ApiService| GET /api/config",
            "BuiltInConfigSyncService| 同步完成，共",
            "ChatModeSettings| 已加载探测缓存:",
            "ExecutionTraceCollector| 开始收集执行轨迹",
            "ExecutionTraceCollector| 停止收集，共",
            "PlanModeMapper| 内容纲要缓存预热完成",
            "ContentViewModel| 初始化正文模块",
            "ContentViewModel| 刷新所有模块状态",
            "ContentViewModel| 打包前自动执行全局清理",
            "GlobalCleanupService| 开始执行全局清理",
            "AIService| 已清理所有业务会话",
            "SKChatService| 已进入草稿会话状态",
            "SKChatService| FamilyContextWindow fallback:",
            "SKChatService| 取消当前请求",
            "SKChatService| 重建 ChatHistory",
            "SKChatService| 切换模式:",
            "MarkdownStreamViewer| 开始流式接收",
            "ApiKeyRotation| 临时禁用密钥",
            "EditorPanel| 初始化主页视图:",
            "EditorPanel| 刷新章节列表状态:",
            "ModelManagementViewModel| 业务导航双击激活",
            "ModelManagementViewModel| TreeAfterAction触发:",
            "ChapterViewModel| 业务导航双击激活",
            "DataTreeView| OnRootItemsRightButtonDown",
            "DataTreeView| 命中节点并进入激活态",
            "ChatModeSettings| 跳过 MaxOutput 低可信写入:",
            "ChatModeSettings| 并发探测 max_tokens:",
            "ChatModeSettings| override MaxTokens=",
            "ChatModeSettings| 已清除发现缓存:",
            "SKChatService| GenerateWithChatHistory max_tokens retry",
            "SKChatService| OpenAI 分支跳过后缀",
            "SKChatService| 已注册",
            "SKChatService| FamilyMaxOutput fallback:",
            "BookAnalysisViewModel| 开始执行AI智能生成功能",
            "BookAnalysisViewModel| 开始AI生成（配置化）",
            "BookAnalysisViewModel| 构建提示词完成，长度:",
            "BookAnalysisViewModel| AI生成完成",
            "BookAnalysisViewModel| 用户触发数据刷新",
            "BookAnalysisViewModel| dep recorded:",
            "NovelCrawlerService| 生成AI上下文摘录:",
            "NovelCrawlerService| 已保存爬取内容:",
            "NovelCrawlerService| 已保存结构蓝图:",
            "NovelCrawlerService| 已删除爬取内容目录:",
            "WebCrawlerService| 抓取成功:",
            "WebCrawlerService| 内容提取title",
            "WebCrawlerService| 章节样本[",
            "WebCrawlerService| 抓取完成:",
            "WebCrawlerService| 提取到",
            "WebCrawlerService| 开始抓取",
            "WebCrawlerService| 疑似目录页",
            "ModelManagement| 降级尝试模型",
            "ModelManagement| 候选URL:",
            "ModelManagement| 开始端点测试:",
            "ModelManagement| 端点签名变化已清空能力快照:",
            "ModelManagement| 端点配置变更，已清空验证状态并持久化:",
            "ModelManagement| 未在模型库中找到",
            "ModelManagement| 用户选择继续等待测试",
            "ModelManagement| 用户取消超时测试",
            "ModelManagement| Models 端点失败:",
            "ModelManagement| Models 端点404",
            "ModelManagement| 端点变更已同步到对话配置:",
            "ModelManagement| 家族兜底写入 MaxOutput 缓存:",
            "ModelManagement| /models 未返回 MaxTokens",
            "ModelManagement| 手动添加模型:",
            "ModelManagement| 手动模型已就绪:",
            "ModelManagement| API连接测试已取消",
            "EndpointTestService| ② thinking 探测协议",
            "EndpointTestService| 探测缓存命中",
            "EndpointTestService| 已清空探测缓存:",
            "ProtectionService| 启动校验开始",
            "ProtectionService| 启动校验通过",
            "ProtectionService| ext loaded",
            "ProtectionService| 运行时保护已启动",
            "ProtectionService| stopped",
            "ProxyService| 已应用应用内代理",
            "Reconciler| 关键词索引缺失",
            "Reconciler| 关键词对账",
            "BuiltInConfigSyncService| 写入 built_in_categories.json:",
            "CurrentChapterPersistence| 已恢复:",
            "ModelService| 内置分类仅删除直属数据",
            "ModelService| 内置分类直属数据已清除",
            "ModelService| 保存供应商",
            "WritingSettingsService| 保存完成",
            "OpenAIReasoningStrategy| 反射解析完成:",
            "SA| C2: response signature invalid",
            "SA| T8: serverTime signature invalid",
            "SAI| 心跳预警",
            "StoragePathHelper| 创建目录:",
            "Upsert分卷:",
            "ContextService| 设计文件:",
            "EditorPanel| 刷新章节列表状态:",
        };

        private static readonly string[] _summarySuppressedDeduplicatePatterns =
        {
            "业务导航双击激活",
            "已标记层级失效",
            "TreeAfterAction触发",
            "SelectedPath变化",
            "MaxTokens=AUTO",
            "Adaptive max_tokens",
            "注册跳过",
            "provider ok",
            "HTTP超时策略",
            "Kernel 构建成功",
            "已注册",
            "构建 Kernel:",
            "协议推断:",
            "Mode=Business",
            "Mode=Edit",
            "Mode=Chat",
            "AUTO使用已探测",
            "AUTO=兜底输出上限",
            "AUTO吃满可用空间",
            "淘汰 ",
            "使用自定义模型:",
            "裁剪完成:",
            "降级尝试模型",
            "跳过流式（标记剩余",
            "GenerateWithChatHistory 调用开始",
            "模型库未收录",
            "使用业务提示词模板",
            "已注入Spec模板原文",
            "DeepSeek模型字数补偿",
            "GPT模型字数补偿",
            "Token预估 ",
            "识别到CHANGES区域",
            "未配置润色API",
            "归一化补丁(",
            "归一化歧义(",
            "FactLedger 跳过的空子块",
            "GenerateChapterByNumber:",
            "GenerateChapter (可取消):",
            "WriterPlugin| gen:",
            "WriterPlugin| RRF ",
            "开始流式接收",
            "已加载统计数据:",
            "开始生成章节:",
            "第1次生成",
            "注入 DeepSeek-V4+",
            "BuildContentContextAsync:",
            "事实快照抽取完成:",
            "势力注入:",
            "物品注入:",
            "思考=默认（不注入字段",
            "刷新章节列表状态:",
            "模块启用状态已更新:",
            "已清理业务会话（前缀=",
            "下游影响提示:",
            "实体引用",
            "直连返回 HTML",
            "代理也返回 HTML",
            "构建上游索引:",
            "层级 SmartParsing",
            "设计上下文已构建:",
            "窗口设置已保存",
            "窗口设置已更新",
            "检测到文件变化，已发布刷新事件",
            "打开工作台窗口",
            "Save 完成 path=",
            "skip: within limits",
            "加载项目Spec成功",
            "字数控制已关闭，跳过字数补偿",
            "GET /api/config/builtin-configs",
            "分类 public(tm-public-",
            "写入 built_in_categories.json",
            "级联清理分类:",
            "内置分类仅删除直属数据",
            "内置分类直属数据已清除",
            "异步懒加载供应商",
            "已缓存模型输出上限",
            "获取依赖快照:",
            "异步添加数据",
            "文件不存在，返回空对象:",
            "HumanizeRules.ApplyDictionary",
            "润前正则",
            "润后正则",
            "ContentCallback| vol_ch 校验通过",
            "ContentCallback| S1: vol_ch",
            "ContentCallback| S2: vol_ch",
            "ContentCallback| vol_ch 更新角色状态",
            "ContentCallback| vol_ch 更新冲突进度",
            "ContentCallback| vol_ch 更新地点状态",
            "ContentCallback| vol_ch 更新时间推进",
            "ContentCallback| vol_ch 更新角色位置",
            "ContentCallback| vol_ch 更新物品流转",
            "ContentCallback| vol_ch 更新秘密知情",
            "ContentCallback| vol_ch 更新势力状态",
            "ContentCallback| vol_ch 更新承诺/契约",
            "ContentCallback| vol_ch 添加关键情节",
            "ContentCallback| vol_ch done",
            "ContentCallback| vol_ch ok",
            "ContentCallback| vol_ch 向量索引建设完成",
            "ContentCallback| 已更新章节摘要:",
            "ContentCallback| 漂移检测",
            "ContentCallback| 已自动补录角色",
            "CharacterState| 已更新",
            "CharacterState| 自动注册新角色:",
            "LocationState| 已更新",
            "LocationState| 自动创建地点条目:",
            "ConflictProgress| 已更新",
            "ConflictProgress| 自动注册新冲突:",
            "ItemState| 已更新",
            "ItemState| 自动创建物品条目:",
            "SecretReveal| 自动创建秘密条目:",
            "SecretReveal| 新增知情者:",
            "TimelineService| 已更新",
            "FactionState| 自动创建势力条目:",
            "FactionState| 已更新",
            "PlotPoints| 已添加情节索引:",
            "PledgeConstraint| 创建承诺/契约:",
            "PledgeConstraint| 执行动作:",
            "WriterPlugin| gen ok:",
            "WriterPlugin| 章节生成成功:",
            "AutoRewriteEngine| 开始润色（共",
            "AutoRewriteEngine| 润色完成并通过校验",
            "AutoRewriteEngine| 蓝图合规 warn:",
            "ContentPolisher| 本地正则润色完成",
            "Trace| RequestStart:",
            "Trace| RequestComplete:",
            "Lifecycle| runId=",
            "GG| [no-correlation]",
            "GG| quality-warn:",
            "ThinkingRouter| 注入 EnableThinkingFlag",
            "FactSnapshotExtractor| 注入近期活跃角色:",
            "FactSnapshotExtractor| 注入近期活跃地点:",
            "FactSnapshotExtractor| 近期活跃势力补入优先池:",
            "FactSnapshotExtractor| 补充注入活跃角色描述:",
            "GuideContextService| 注入首次描写:",
            "GuideContextService| 检测到",
            "LayeredPromptBuilder| 势力注入:",
            "Pipeline: 开始批量生成,",
            "Pipeline: 批量生成完成, 耗时=",
            "开始AI生成: 分类=",
            "第 1 批完成: 生成=",
            "批量AI生成完成: 成功=",
            "BootstrapManager| 开始执行启动任务",
            "OAuthService| 初始化完成",
            "ApiService| POST /api/auth/login",
            "LoginService| API登录成功:",
            "LoginWindow| 登录成功:",
            "AuthStartupService| 认证服务已初始化",
            "HolidayLibrary| 初始化完成",
            "TimeScheduleService| 初始化完成",
            "SystemThemeMonitor| 初始化完成",
            "SceneDetector| 初始化完成",
            "SystemFollowController| 初始化完成",
            "LocaleSettings| async loaded",
            "ChapterEmbedding| 索引不存在，空载:",
            "ChunkEmbedding| 索引不存在，空载:",
            "SessionManager| 初始化完成，会话目录:",
            "SessionManager| 加载消息:",
            "SKChatService| 初始化",
            "SKConversationViewModel| 初始化完成",
            "StatisticsService| 加载了",
            "GuideContextService| 缓存初始化完成",
            "DataIndexService| 索引初始化完成:",
            "QueryRoutingService| 索引初始化完成:",
            "DI| 所有模块服务初始化完成",
            "ATM| loaded",
            "ATM| saved",
            "SA| sync",
            "AppLockSettings| init",
            "SAI| init",
            "ContentViewModel| 开始清除打包",
            "ContentViewModel| 开始打包",
            "PublishService| 已生成 staging manifest",
            "PublishService| staging 已原子转正",
            "GlobalCleanupService| 全局清理完成",
            "BusinessCleanupService| 开始执行业务清理",
            "BusinessCleanupService| 已清空",
            "BusinessCleanupService| 业务清理完成，共清空",
            "OneClickGenerate| 刷新重置完成",
            "VolumeDesignService| 分类删除事件已触发:",
            "[Icon.Rocket] 管线启动:",
            "[Icon.Refresh] 开始 #",
            "[Icon.Refresh] #",
            "[Icon.CheckCircle] #",
            "[Icon.CheckCircle] 完成",
            "[Icon.Save] #",
            "[Icon.Clipboard] #",
            "[Icon.ChevronRight] #",
            "[Icon.Forbidden] #",
            "ChapterPreviewViewModel| 加载成功:",
            "MilestoneStore| 已更新第",
            "CurrentChapterTracker| 当前章节:",
            "UIStateCache| 左栏状态已缓存:",
            "UIStateCache| 右栏状态已缓存:",
            "ChangeDetectionService| 刷新所有模块变更状态",
            "ChangeDetectionService| 刷新完成，共",
            "ChangeDetectionService| manifest.json不存在",
            "ChangeDetectionService| 加载上次打包时间:",
            "EndpointTestService| 家族短路决策",
            "EndpointTestService| /models 已提示",
            "EndpointTestService| 全部参数探测被",
            "EndpointTestService| Phase B 节省:",
            "EndpointTestService| 探测结果已缓存",
            "EndpointTestService| 已清空全部探测缓存",
            "ChapterMarkdownEditor| 切换到标签:",
            "MemoryOptimization| 用户空闲",
            "MemoryOptimization| 内存使用",
            "SessionManager| 保存消息:",
            "SessionManager| 创建会话:",
            "SessionManager| 删除会话:",
            "SessionManager| 当前会话已删除",
            "SKConversationViewModel| 启动：已恢复会话",
            "SKConversationViewModel| 启动：章节上下文恢复为",
            "SKConversationViewModel| 加载",
            "SKConversationViewModel| 第1章唯一匹配",
            "SKConversationViewModel| 从自然语言解析到章节",
            "SKConversationViewModel| 消息完成:",
            "SKConversationViewModel| 计划模式 -",
            "SKConversationViewModel| 代理模式 -",
            "SKConversationViewModel| Agent 模式开始",
            "SKConversationViewModel| 已取消生成",
            "SKConversationViewModel| 收到",
            "SKConversationViewModel| 推理参数已同步回ModelService",
            "SKConversationViewModel| 快捷思考开关",
            "SKConversationViewModel| 快捷推理强度:",
            "SKConversationViewModel| 防御:",
            "SKConversationViewModel| 发送消息:",
            "AIService| 添加配置:",
            "AIService| 更新配置:",
            "AIService| 删除配置:",
            "AIService| AI核心服务已初始化",
            "ModelManagement| 端点已保存:",
            "ModelManagement| Models 端点成功:",
            "ModelManagement| 开始获取全部模型:",
            "ModelManagement| 获取模型成功",
            "ModelManagement| 已为模型创建AIService配置:",
            "ModelManagement| 已更新AIService配置:",
            "ModelManagement| MaxTokens 已调整:",
            "ModelManagement| 探测写入 MaxOutput 缓存:",
            "ModelManagement| 模型参数探测完成:",
            "ModelManagement| 孤儿配置已清理:",
            "ModelService| 系统内置分类不可修改:",
            "ModelService| 级联删除:",
            "ModelService| GetModelsForProvider",
            "ModelService| 批量添加完成:",
            "ModelService| 更新数据",
            "ModelService| 添加分类:",
            "ModelService| 更新分类:",
            "ModelService| 启动时加载供应商",
            "PlanModeMapper| 基于打包数据生成计划步骤",
            "PlanModeMapper| 基于打包数据跳过模型调用",
            "PlanPayloadPublisher| 发布",
            "PlanViewModel| 从 Events 提取",
            "TodoExecutionService| 启动",
            "TodoExecutionService| 已请求取消",
            "ChapterListPanel| 全部删除完成:",
            "ChapterListPanel| 选择章节:",
            "ChapterListPanel| 加载了",
            "DataTreeView| HandleInternalDeleteAll",
            "WriterPlugin| 生成已取消",
            "GG| start: vol_ch",
            "GG| ok: vol_ch",
            "AutoRewriteEngine| 字数检测通过:",
            "AutoRewriteEngine| 字数超限",
            "AutoRewriteEngine| 第1次生成成功",
            "AutoRewriteEngine| 第2次生成",
            "AutoRewriteEngine| 第3次生成",
            "AutoRewriteEngine| AI请求失败，内部重试",
            "BookAnalysisViewModel| 已加载",
            "BookAnalysisView| 爬虫服务已注入",
            "BookAnalysisView| WebView2 已冻结",
            "SubscriptionService| 订阅数据已加载",
            "FocusContextService| TrackingStatus实时构建完成",
            "FocusContextService| 设计上下文已构建:",
            "GenerationStats| 已加载统计数据:",
            "GenerationStats| 记录生成:",
            "ContextService| 构建CoreDesignContext",
            "ContextService| 剧情按卷过滤:",
            "ContextService| 实体注入（按卷）",
            "ContextService| OPT-017",
            "StatisticsService| 记录API调用:",
            "GuideContextService| 章节 vol_ch 缺少MD上下文",
            "GuideContextService| 已触发全局缓存失效事件",
            "GuideContextService| 缓存已清除",
            "GuideContextService| content_guide 聚合",
            "IndexService| 构建上游索引:",
            "IndexService| 层级 SmartParsing",
            "GlobalSummaryService| cache miss",
            "GlobalSummaryService| 实时计算完成",
            "GlobalSummaryService| 缓存已清除",
            "PromptService| 嵌入加载系统内置模板:",
            "PromptService| 合并系统内置模板:",
            "PromptService| 业务提示词分类已有",
            "GuideManager| 加载成功:",
            "GuideManager| 批量保存完成，共",
            "GuideManager| P1:",
            "GuideManager| P2:",
            "ChapterEmbedding| Save 完成",
            "ChapterEmbedding| Load 完成",
            "ChapterEmbedding| 缓存已失效",
            "ChunkEmbedding| Save 完成",
            "ChunkEmbedding| Load 完成",
            "ChunkEmbedding| 缓存已失效",
            "ChunkEmbedding| 按章节清理",
            "FirstChapterIndex| 捕获",
            "FirstChapterIndex| Save 完成",
            "FirstChapterIndex| Load 完成",
            "FirstChapterIndex| Rebuild",
            "FirstChapterIndex| 按章节失效",
            "FirstChapterIndex| 缓存已失效",
            "KeywordIndex| 已索引",
            "MilestoneStore| 已追加",
            "SummaryStore| 已移除摘要:",
            "SessionCache| 已标记层级失效:",
            "SessionCache| 缓存已清空",
            "PackageHistoryService| 已删除 manifest.json",
            "PackageHistoryService| 已清除当前打包文件",
            "PackageHistoryService| 已清除所有历史记录",
            "PackageHistoryService| 已清除已生成章节",
            "PackageHistoryService| 已清除当前章节",
            "PublishService| 开始打包所有模块",
            "PublishService| 已打包:",
            "PublishService| 统计完成:",
            "PublishService| 已保存指导文件:",
            "PublishService| 已创建备份:",
            "PublishService| 已刷新并预热缓存",
            "PublishService| 打包完成",
            "PublishService| 缓存已清除",
            "PublishService| 已生成 staging manifest",
            "GuideIndexBuilder| 大纲指导构建完成",
            "GuideIndexBuilder| 规划指导构建完成",
            "GuideIndexBuilder| 蓝图指导构建完成",
            "GuideIndexBuilder| 正文指导构建完成",
            "GuideIndexBuilder| 伏笔完成度追踪初始化完成",
            "DataIndexService| 索引初始化完成:",
            "DataIndexService| 项目切换/缓存失效",
            "DataIndexService| 索引已清除",
            "QueryRoutingService| 索引初始化完成:",
            "RelationStrengthService| 缓存已清除",
            "DataEditPlugin| 缓存刷新完成",
            "DataEditPlugin| RollbackChange 完成:",
            "DataEditPlugin| ExecuteChange 完成:",
            "DataEditPlugin| ConfirmChange 完成:",
            "DataEditPlugin| ReconcileAllData 完成",
            "PendingChangeStore| 创建预览",
            "AccessRightsService| 缓存已清除",
            "GoogleAuthService| 缓存已清除",
            "CharacterState| 已移除章节",
            "LocationState| 已移除章节",
            "PlotPoints| 已移除章节",
            "ConflictProgress| 已移除章节",
            "ItemState| 已移除章节",
            "SecretReveal| 已移除章节",
            "TimelineService| 已移除章节",
            "FactionState| 已移除章节",
            "PledgeConstraint| 已移除章节",
            "GeneratedContentService| 删除章节:",
            "ChapterFileWatcher| 文件变化:",
            "ChapterFileWatcher| 开始监听:",
            "WorkspaceLayout| 检测到文件变化",
            "ProtectionService| 启动校验已跳过",
            "ProtectionService| 运行时保护已禁用",
            "ProtectionService| 跳过初始化",
            "ProjectManager| 启动时同步当前项目:",
            "NavigationConfigParser| 已从硬编码加载",
            "ProviderLogoHelper| 嵌入加载 Logo 映射:",
            "EndpointTestService| 持久化探测缓存已加载:",
            "AccountBindingSettings| 数据已延迟加载:",
            "LoginHistorySettings| 数据已延迟加载:",
            "NotificationHistorySettings| 数据已延迟加载:",
            "LoginHistoryService| 登录记录已保存:",
            "TrayIcon| 托盘图标已创建",
            "TrayIcon| 托盘图标已初始化",
            "AppLock| 自动锁定定时器已启动",
            "AppLock| init ok",
            "MainWindow| 已从共享设置加载窗口状态:",
            "FontManager| UI字体已应用:",
            "FontManager| 编辑器字体已应用:",
            "WindowsNotification| Windows原生通知已禁用",
            "SystemIntegration| 设置已加载并应用",
            "UIResolution| 配置文件不存在，使用默认设置",
            "LocaleService| 文化区域已应用:",
            "Reconciler| 开始一致性对账",
            "Reconciler| GuideManager pending flush 已检查/恢复",
            "Reconciler| 对账完成:",
            "Reconciler| done:",
            "Reconciler| 首次描写冷启动补建",
            "对账| 已自动修复:",
            "Bootstrap| UIResolutionService 已激活",
            "Bootstrap| TimeScheduleService 已激活",
            "Bootstrap| SystemFollowController 已激活",
            "Bootstrap| LocaleService 已激活",
            "DI| 依赖注入容器已初始化",
            "BootstrapManager| 任务完成:",
            "BootstrapManager| 并行执行批次:",
            "BootstrapManager| 所有启动任务执行完成",
            "BootstrapManager| [",
            "SplashWindow| 启动进度窗口已初始化",
            "SplashWindow| 进度更新:",
            "UIPreWarm| 弹窗/控件/模板预热完成",
            "UIPreWarm| Pre-JIT 完成:",
            "UIPreWarm| 优先视图预创建完成",
            "UsageStatistics| 视图已加载",
            "UsageStatistics| 统计数据已刷新",
            "UnifiedValidationService| 初始化完成",
            "ValidationSummaryService| 加载数据:",
            "VersionTestingViewModel| 读取提示词缓存成功",
            "VersionTestingViewModel| 当前提示词缓存数量:",
            "VersionTestingViewModel| 获取分类数据，共",
            "VersionTestingViewModel| 构建树形数据完成",
            "VersionTesting| 提示词版本测试视图已加载",
            "VersionTestingService| 数据文件不存在，初始化空列表",
            "PromptManagement| 提示词管理视图已加载",
            "ModelManagement| 模型管理视图已加载",
            "TimeScheduleService| 初始化完成",
            "MemoryOptimization| 服务已启动，GC间隔:",
            "内存管理| 内存优化服务已启动",
            "字体| UI字体:",
            "代理| 代理配置已加载",
            "组件| 3栏布局",
            "命令行| 调试模式已启用",
            "生成参数| 生成参数已从本地存储加载",
            "主题| 当前主题:",
            "主窗口| 托盘图标服务已初始化",
            "ThemeManager| 主题切换成功:",
            "NetworkMonitor| 已启动系统网络监测",
            "NetworkMonitor| 已停止系统网络监测",
            "HolidayLibrary| 初始化完成",
            "OAuthService| 初始化完成",
            "IpLocationHelper| IP2Region 初始化成功",
            "ApiService| GET /api/config",
            "BuiltInConfigSyncService| 同步完成，共",
            "ChatModeSettings| 已加载探测缓存:",
            "ExecutionTraceCollector| 开始收集执行轨迹",
            "ExecutionTraceCollector| 停止收集，共",
            "PlanModeMapper| 内容纲要缓存预热完成",
            "ContentViewModel| 初始化正文模块",
            "ContentViewModel| 刷新所有模块状态",
            "ContentViewModel| 打包前自动执行全局清理",
            "GlobalCleanupService| 开始执行全局清理",
            "AIService| 已清理所有业务会话",
            "SKChatService| 已进入草稿会话状态",
            "SKChatService| FamilyContextWindow fallback:",
            "SKChatService| 取消当前请求",
            "SKChatService| 重建 ChatHistory",
            "SKChatService| 切换模式:",
            "MarkdownStreamViewer| 开始流式接收",
            "ApiKeyRotation| 临时禁用密钥",
            "EditorPanel| 初始化主页视图:",
            "EditorPanel| 刷新章节列表状态:",
            "ModelManagementViewModel| 业务导航双击激活",
            "ModelManagementViewModel| TreeAfterAction触发:",
            "ChapterViewModel| 业务导航双击激活",
            "DataTreeView| OnRootItemsRightButtonDown",
            "DataTreeView| 命中节点并进入激活态",
            "ChatModeSettings| 跳过 MaxOutput 低可信写入:",
            "ChatModeSettings| 并发探测 max_tokens:",
            "ChatModeSettings| override MaxTokens=",
            "ChatModeSettings| 已清除发现缓存:",
            "SKChatService| GenerateWithChatHistory max_tokens retry",
            "SKChatService| OpenAI 分支跳过后缀",
            "SKChatService| 已注册",
            "SKChatService| FamilyMaxOutput fallback:",
            "BookAnalysisViewModel| 开始执行AI智能生成功能",
            "BookAnalysisViewModel| 开始AI生成（配置化）",
            "BookAnalysisViewModel| 构建提示词完成，长度:",
            "BookAnalysisViewModel| AI生成完成",
            "BookAnalysisViewModel| 用户触发数据刷新",
            "BookAnalysisViewModel| dep recorded:",
            "NovelCrawlerService| 生成AI上下文摘录:",
            "NovelCrawlerService| 已保存爬取内容:",
            "NovelCrawlerService| 已保存结构蓝图:",
            "NovelCrawlerService| 已删除爬取内容目录:",
            "WebCrawlerService| 抓取成功:",
            "WebCrawlerService| 内容提取title",
            "WebCrawlerService| 章节样本[",
            "WebCrawlerService| 抓取完成:",
            "WebCrawlerService| 提取到",
            "WebCrawlerService| 开始抓取",
            "WebCrawlerService| 疑似目录页",
            "ModelManagement| 降级尝试模型",
            "ModelManagement| 候选URL:",
            "ModelManagement| 开始端点测试:",
            "ModelManagement| 端点签名变化已清空能力快照:",
            "ModelManagement| 端点配置变更，已清空验证状态并持久化:",
            "ModelManagement| 未在模型库中找到",
            "ModelManagement| 用户选择继续等待测试",
            "ModelManagement| 用户取消超时测试",
            "ModelManagement| Models 端点失败:",
            "ModelManagement| Models 端点404",
            "ModelManagement| 端点变更已同步到对话配置:",
            "ModelManagement| 家族兜底写入 MaxOutput 缓存:",
            "ModelManagement| /models 未返回 MaxTokens",
            "ModelManagement| 手动添加模型:",
            "ModelManagement| 手动模型已就绪:",
            "ModelManagement| API连接测试已取消",
            "EndpointTestService| ② thinking 探测协议",
            "EndpointTestService| 探测缓存命中",
            "EndpointTestService| 已清空探测缓存:",
            "ProtectionService| 启动校验开始",
            "ProtectionService| 启动校验通过",
            "ProtectionService| ext loaded",
            "ProtectionService| 运行时保护已启动",
            "ProtectionService| stopped",
            "ProxyService| 已应用应用内代理",
            "Reconciler| 关键词索引缺失",
            "Reconciler| 关键词对账",
            "BuiltInConfigSyncService| 写入 built_in_categories.json:",
            "CurrentChapterPersistence| 已恢复:",
            "ModelService| 内置分类仅删除直属数据",
            "ModelService| 内置分类直属数据已清除",
            "ModelService| 保存供应商",
            "WritingSettingsService| 保存完成",
            "OpenAIReasoningStrategy| 反射解析完成:",
            "SA| C2: response signature invalid",
            "SA| T8: serverTime signature invalid",
            "SAI| 心跳预警",
            "StoragePathHelper| 创建目录:",
            "Upsert分卷:",
            "ContextService| 设计文件:",
            "EditorPanel| 刷新章节列表状态:",
        };

        private static readonly string[] _debugKeywordsIgnoreCase =
        {
            "清理完成", "目录清理完成", "清理成功",
            "开始加载视图", "view ok", "开始初始化", "初始化完成",
            "文件不存在，使用默认数据", "配置文件不存在，使用默认设置",
            "设置文件不存在，使用默认配置",
            "数据已加载", "数据已异步加载", "数据已保存", "数据已异步保存",
            "显示通知", "使用类型配置", "拦截通知", "进度更新",
            "任务完成", "流式发送", "并行执行批次", "模块启用状态已更新",
            "Upsert新建:", "Upsert更新:", "模块版本自增:",
            "MaxTokens=AUTO", "打开工作台窗口",
            "AUTO吃满可用空间:", "Adaptive max_tokens:",
            "AUTO=null（CW未知", "下游影响提示:",
            "刷新章节列表状态:", "自动绑定CategoryId:",
            "SelectedPath变化:", "TreeAfterAction触发:",
            "创建目录:", "打包文件不存在:",
            "ApplyFilter开始", "ApplyFilter完成",
            "筛选后得到", "筛选完成：", "刷新所有模块状态",
            "加载上次打包时间:", "同步providers.json",
            "从 ProviderModels 目录加载模型",
            "加载分类:", "加载供应商:", "懒加载供应商",
            "保存用户配置:",
            "窗口设置已保存", "窗口状态已保存", "窗口设置已加载", "窗口状态已加载",
            "开始视图预热", "视图预热完成",
            "业务导航双击激活", "已标记层级失效",
            "文件不存在，返回空对象", "文件不存在:",
            "激活配置:", "均衡器未启用", "取消当前请求",
            "构建 Kernel:", "Kernel 构建成功",
            "协议推断:", "provider ok", "HTTP超时策略:",
            "直连也返回 HTML:", "候选URL:", "/models 探测:",
            "已知模型族写入能力:", "验证名称推断",
            "HTTP探测覆盖名称推断:", "探测完成",
            "缓存已清除", "缓存已清空", "索引已清除", "索引已清空",
            "增益:", "物品注入:", "势力注入:",
            "事实快照抽取完成:", "BuildContentContextAsync:",
            "SystemPrompt长度:", "使用自定义模型:",
            "流式密钥轮换:", "保存完成 Chat=",
            "保存供应商", "同步CategoryId:",
            "资源已释放", "刷新完成，共",
            "dep snapshot:", "dep recorded:", "cache miss",
            "实时计算完成", "skip: within limits", "反射解析完成:", "内容提取title=",
        };

        private static readonly string[] _debugKeywordsOrdinal =
        {
            "淘汰 ", "同前缀旧 bundle",
            "跳过流式（标记剩余",
            "Mode=Business,", "Mode=Edit,", "Mode=Chat,", "Mode=Channel,",
            "记录API调用:", "错误响应解析失败:",
            "hb err:", "hb fail",
            "视图已加载", "视图已初始化", "界面已初始化",
            "统计数据已刷新", "读取提示词缓存成功", "当前提示词缓存数量:",
            "获取分类数据，共", "构建树形数据完成", "初始化正文模块",
            "识别到CHANGES区域，格式:", "刷新所有模块变更状态",
            "GenerateWithConfig: 使用配置", "content_guide 聚合",
            "检测到文件变化，已发布刷新事件",
            "使用业务提示词模板:", "已注入Spec模板原文:", "加载项目Spec成功",
            "批量保存完成，共", "SaveBatchEntitiesAsync: 成功保存",
            "ParseBatchJsonResult: 成功解析", "名称映射加载完成:",
            "FamilyContextWindow fallback:", "硬截断完成:", "业务会话触发压缩:",
            "发送前预检：", "生成结束清理:", "批完成: 生成=", "模型库未收录",
            "获取依赖快照:", "已清理业务会话（前缀=",
            "批量AI生成完成:", "槽位缩减：已完成", "继续补齐剩余",
            "Pipeline: 开始批量生成,", "开始AI生成: 分类=",
            "蓝图已全部完成", "已全部完成，跳过生成",
            "窗口设置已原子更新", "当前系统音量",
            "选择输出设备:", "选择输入设备:",
            "音频设备管理器初始化成功", "系统音量控制器初始化成功",
            "加载自定义音效:", "切换方案:", "加载音效库，共",
            "加载系统字体:", "OpenType检测",
            "个连字", "个OpenType特性", "已加载 C# 代码示例",
            "开始刷新监控数据", "延迟完成，开始数据采集", "数据加载任务已完成",
            "磁盘使用率采集完成", "网络流量采集完成",
            "内存动态信息采集完成", "CPU动态信息采集完成",
            "传感器信息采集完成", "监控数据刷新完成",
            "Pre-JIT 完成:", "ViewModel初始化",
            "开始异步加载数据",
            "加载配置选项:", "弹窗/控件/模板预热完成",
            "保存设置成功", "刷新统计信息:",
            "检测到显示器刷新率:", "组合效果已更新:", "系统信息已刷新:",
            "已刷新显示器信息", "已刷新变化历史",
            "从Settings加载", "转换完成，_allRecords=",
            "使用默认配置", "配置文件不存在，使用默认配置",
            "收藏数据文件不存在", "数据文件不存在，初始化空列表",
            "加载Logo映射配置:", "已从硬编码加载",
            "manifest.json不存在", "加载免打扰设置",
            "初始化主题系统", "主题切换成功:", "当前主题: 未知主题",
            "所有登录前服务已就绪", "显示登录窗口",
            "找到项目根目录:", "第三方登录图标已加载", "登录窗口已初始化",
            "初始化服务器授权", "服务器授权已启动",
            "后台服务已启动", "显示启动进度窗口", "启动进度窗口已初始化",
            "开始执行启动任务，共", "依赖注入容器已初始化",
            "启动时同步当前项目:", "AI核心服务已初始化",
            "加载系统内置模板:", "合并系统内置模板:",
            "业务提示词分类已有", "已种子业务提示词默认模板:",
            "添加数据", "生成参数已从本地存储加载",
            "Windows原生通知已禁用", "设置已加载并应用",
            "UIResolutionService 已激活", "UI字体已应用:", "编辑器字体已应用:",
            "TimeScheduleService 已激活", "SystemFollowController 已激活",
            "文化区域已应用:", "LocaleService 已激活",
            "开始一致性对账", "GuideManager pending flush 已检查/恢复",
            "对账完成: 所有数据一致，无需修复",
            "会话索引已加载:", "章节索引已加载:",
            "右栏状态已缓存:", "当前章节已恢复",
            "SK对话服务已初始化", "登录记录已保存:",
            "AI模型库索引已预热", "对话实体索引已就绪",
            "对话上下文服务已就绪",
            "UI状态预热完成", "自动锁定定时器已启动",
            "服务已启动，GC间隔:", "内存优化服务已启动，已注册缓存清理回调",
            "所有启动任务执行完成", "后台预热完成",
            "程序启动完成", "调试模式已启用",
            "代理配置已加载", "IP2Region 初始化成功",
            "开始端点测试: Endpoint=", "Models 端点404，端点可能不暴露/models",
            "左栏状态已缓存:", "] 执行任务:", "UI字体: ",
            "WARNING: LoadData() called on UI thread",
            "缓存未命中，fire-and-forget",
            "Mode=Channel, MaxTokens=", "Mode=Chat, MaxTokens=",
            "FactLedger 跳过的空子块:",
            "fa err:",
            "裁剪完成:",
            "构建CoreDesignContext",
            "剧情按卷过滤:", "实体注入（按卷）",
            "已触发全局缓存失效事件", "已重置实体索引",
            "写入 built_in_categories.json:", "同步完成，共",
            "恢复会话模式:", "异步加载用户配置:", "已加载探测缓存:",
            "优先视图预创建完成",
            "所有任务完成，显示主窗口", "代理返回 HTML，尝试直连",
            "启动校验已跳过", "运行时保护已禁用",
            "GenerateWithChatHistory 调用开始:", "OneShot 生成开始",
            "OnRootItemsRightButtonDown",
            "检测到开发级问题，短路处理:", "开发级问题响应完成:",
            "AUTO使用已探测", "已缓存模型输出上限:",
            "检测到项目变化，已异步重载版本注册表",
            "构建上游索引:", "TrackingStatus实时构建完成",
            "设计上下文已构建:", "层级 SmartParsing 加载了",
            "实体引用", "主动补全CategoryId:",
            "加载了 0 个分类, 0 个章节", "初始化主页视图:",
            "重建 ChatHistory，消息数:",
            "开始监听: ", "3栏布局初始化",
            "托盘图标已创建", "托盘图标已初始化", "托盘图标服务已初始化",
            "已从共享设置加载窗口状态:",
            "已启动系统网络监测", "AI功能授权缓存已就绪",
            "数据已延迟加载:", "加载数据: 0 条",
            "配置加载成功", "加载配置成功",
            "切换到个人模式", "头像目录:",
            "用户资料加载成功", "已从服务器同步资料到本地",
            "订阅信息已从服务器同步:", "锁定状态已从服务器同步",
            "已刷新服务: AIService",
            "窗口设置已更新",
            "已保存窗口状态到共享设置:", "主窗口已关闭，引用已清空",
            "已停止系统网络监测", "自动锁定定时器已停止",
            "保存当前章节状态", "保存未写入的数据", "已保存所有未写入的数据",
            "开始清理托盘图标", "托盘图标已彻底移除",
            "托盘服务已释放", "托盘图标服务已停止",
            "初始化警告:", "已应用应用内代理",
            "Plugin 'Writer' 注册跳过",
            "WebView2 已冻结", "WebView2 已恢复", "WebView2 初始化成功",
            "导航到:", "章节样本[",
            "正文提取成功", "抓取成功:",
            "已保存记住的账号:", "已刷新服务:",
            "流式生成完成:", "Pipeline: 批量生成完成",
            "保存项目Spec成功", "Spec 已同步为题材:",
            "已恢复管线状态:", "Pipeline: 第",
            "从大纲 VolumeDivision 解析成功:", "大纲分配:",
            "批量配置: 总卷数=", "异步更新数据",
            "切换备用通道", "切换模式:", "切换对话模式:",
            "OneShot 生成完成", "未配置润色API，兜底使用主对话API",
            "使用激活配置", "gen ok:",
            "开始生成章节:", "第1次生成", "生成成功",
            "Token预估", "无需降级",
            "已加载统计数据:", "记录生成:",
            "润色成功，原文", "润色后字数超限", "字数检测通过:",
            "GPT模型字数补偿：", "字数控制已关闭", "字数控制=全局免检",
            "start: vol", "ok: vol", "校验通过",
            "S1: vol", "S2: vol",
            "已添加情节索引:", "更新物品流转:", "更新秘密知情:",
            "自动创建物品条目:", "创建承诺/契约:",
            "更新时间推进", "添加关键情节:",
            "创建会话:", "删除会话:", "更新会话模式:",
            "当前会话已删除", "执行结束，Mode=",
            "开始清除打包", "已删除 manifest.json",
            "已清除当前打包文件", "已清除所有历史记录",
            "已清除已生成章节", "已清除当前章节",
            "开始打包", "打包前自动执行全局清理",
            "开始执行全局清理", "已清理所有业务会话",
            "已进入草稿会话状态", "开始打包所有模块",
            "已创建备份:", "已打包:",
            "统计完成:", "已更新manifest",
            "指导构建完成", "已保存指导文件:",
            "指导文件生成完成", "L-004: 数据检查通过",
            "已刷新并预热缓存", "打包完成",
            "缺少MD上下文，使用备用模式",
            "内存使用", "内存优化完成:",
            "初始化完成", "刷新所有模块状态", "刷新所有模块变更状态",
            "TreeAfterAction触发:", "模块启用状态已更新:",
            "并行执行批次:", "创建目录:",
            "文件不存在，返回空对象:", "自动绑定CategoryId:",
            "配置弹窗完成:", "模型库未收录",
            "已标记层级失效:", "Upsert新建:", "指导文件不存在:",
            "已清除当前打包文件", "已清除所有历史记录",
            "已清除已生成章节", "已清除当前章节", "缓存已失效",
            "开始执行AI智能生成功能", "进入分类AI生成模式",
            "归一化补丁(", "归一化歧义(",
            "直连返回 HTML，尝试代理", "代理也返回 HTML:", "候选URL:",
            "BugTrace",
            "文件变化: Created -", "文件变化: Deleted -", "文件变化: Changed -",
            "已移除章节 ", "已移除摘要:",
            "按章节清理 ", "按章节失效 ",
            "GuideManager| 加载成功:",
            "启动时加载供应商", "已加载探测缓存:",
            "内容纲要缓存预热完成",
            "MicroEmbedding| 模型加载完成",
            "FirstChapterIndex| 捕获 ", "FirstChapterIndex| Rebuild ",
            "FirstChapterIndex| Save 完成", "FirstChapterIndex| Load 完成",
            "ChapterEmbedding| Save 完成", "ChapterEmbedding| Load 完成",
            "ChunkEmbedding| Save 完成", "ChunkEmbedding| Load 完成",
            "KeywordIndex| 已索引 ",
            "GuideManager| P1:", "GuideManager| P2:",
            "ChapterListPanel| 加载了 ",
            "AppLock| init ok",
            "MilestoneStore| 已追加",
            "获取依赖快照:",
            "异步添加数据",
            "HumanizeRules.ApplyDictionary",
            "润前正则",
            "润后正则",
        };

        private static bool ContainsAnyKeyword(string message, string[] keywords, StringComparison comparison)
        {
            for (int i = 0; i < keywords.Length; i++)
            {
                if (message.Contains(keywords[i], comparison))
                    return true;
            }
            return false;
        }

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

            Debug.WriteLine($"[LogManager] {key}: {ex.Message}");
        }

        private readonly string _logLevelSettingsPath;
        private readonly string _logFormatSettingsPath;
        private readonly string _logOutputSettingsPath;
        private readonly string _logRotationSettingsPath;

        private readonly Channel<LogEntry> _highPriorityChannel = Channel.CreateUnbounded<LogEntry>(new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
        private readonly Channel<LogEntry> _lowPriorityChannel = Channel.CreateBounded<LogEntry>(
            new BoundedChannelOptions(4096)
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.DropWrite
            });

        private long _droppedLowPriority;
        private DateTime _lastDropReport = DateTime.MinValue;
        private readonly CancellationTokenSource _writerCts = new();
        private readonly Task _writerTask;

        private LogLevelSettings _levelSettings = new();
        private LogFormatSettings _formatSettings = new();
        private volatile HashSet<string> _activeFormatTokens = new() { "timestamp", "level", "message", "caller", "threadid", "processid", "exception" };
        private LogOutputSettings _outputSettings = new();
        private LogRotationSettings _rotationSettings = new();
        private string? _resolvedLogDir;
        private readonly HashSet<string> _clearedFilesThisSession = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _deduplicateLock = new();
        private readonly Dictionary<string, (int Count, DateTime FirstSeen, LogLevelEnum Level)> _recentLogs = new();
        private int _deduplicateCallCount;
        private const int DeduplicateWindowMs = 30000;
        private const int MediumDeduplicateWindowMs = 300000;
        private const int LongDeduplicateWindowMs = 600000;
        private const int DeduplicateKeyMaxLength = 120;

        private volatile bool _useFastFormat = true;
        private volatile int _prevTotalMinutes = -1;
        private volatile int _prevSecond = -1;
        private long _prevLogTimeTicks;
        private static readonly char[] _levelChars = { 'T', 'D', 'I', 'W', 'E', 'F' };

        public LogManager()
        {
            IsInitializing = true;

            _logLevelSettingsPath = StoragePathHelper.GetFilePath(
                "Framework",
                "SystemSettings/Logging/LogLevel",
                "settings.json"
            );
            _logFormatSettingsPath = StoragePathHelper.GetFilePath(
                "Framework",
                "SystemSettings/Logging/LogFormat",
                "settings.json"
            );
            _logOutputSettingsPath = StoragePathHelper.GetFilePath(
                "Framework",
                "SystemSettings/Logging/LogOutput",
                "settings.json"
            );
            _logRotationSettingsPath = StoragePathHelper.GetFilePath(
                "Framework",
                "SystemSettings/Logging/LogRotation",
                "settings.json"
            );

            IsInitializing = false;

            _writerTask = Task.Run(async () =>
            {
                try { await ReloadAsync().ConfigureAwait(false); CompressOldLogFiles(); CleanupOldLogFiles(); }
                catch (Exception ex) { Debug.WriteLine($"[LogManager] 配置加载失败，使用默认配置: {ex.Message}"); }

                await BackgroundWriterLoopAsync().ConfigureAwait(false);
            });
        }

        public void Flush()
        {
            try
            {
                _highPriorityChannel.Writer.TryComplete();
                _lowPriorityChannel.Writer.TryComplete();
                _writerTask.Wait(TimeSpan.FromSeconds(2));
            }
            catch { }
        }

        public async System.Threading.Tasks.Task ReloadAsync()
        {
            _levelSettings = await LoadJsonOrDefaultAsync(_logLevelSettingsPath, new LogLevelSettings()).ConfigureAwait(false);
            _formatSettings = await LoadJsonOrDefaultAsync(_logFormatSettingsPath, new LogFormatSettings()).ConfigureAwait(false);
            _outputSettings = await LoadJsonOrDefaultAsync(_logOutputSettingsPath, new LogOutputSettings()).ConfigureAwait(false);
            _rotationSettings = await LoadJsonOrDefaultAsync(_logRotationSettingsPath, new LogRotationSettings()).ConfigureAwait(false);
            _resolvedLogDir = null;
            _activeFormatTokens = DetectTokensInTemplate(_formatSettings.FormatTemplate);
            _useFastFormat = IsDefaultTemplate(_formatSettings.FormatTemplate);
        }

        private static bool IsDefaultTemplate(string? template)
        {
            if (string.IsNullOrWhiteSpace(template)) return true;
            return template == "[{timestamp}] [{level}] [{caller}] {message}"
                || template == "[{timestamp}] [{level}] {message}";
        }

        private static HashSet<string> DetectTokensInTemplate(string? template)
        {
            if (string.IsNullOrWhiteSpace(template))
                template = "[{timestamp}] [{level}] [{caller}] {message}";
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (template.Contains("timestamp", StringComparison.OrdinalIgnoreCase)) tokens.Add("timestamp");
            if (template.Contains("level", StringComparison.OrdinalIgnoreCase)) tokens.Add("level");
            if (template.Contains("message", StringComparison.OrdinalIgnoreCase)) tokens.Add("message");
            if (template.Contains("caller", StringComparison.OrdinalIgnoreCase)) tokens.Add("caller");
            if (template.Contains("threadid", StringComparison.OrdinalIgnoreCase)) tokens.Add("threadid");
            if (template.Contains("processid", StringComparison.OrdinalIgnoreCase)) tokens.Add("processid");
            if (template.Contains("exception", StringComparison.OrdinalIgnoreCase)) tokens.Add("exception");
            return tokens;
        }

        public void Log(string message)
        {
            try
            {
                var (module, parsedMessage) = TryParseModule(message);

                LogLevelEnum level;
                if (_levelSettings.MinimumLevel >= LogLevelEnum.Warning
                    && !parsedMessage.Contains("Fatal", StringComparison.OrdinalIgnoreCase)
                    && !parsedMessage.Contains("致命", StringComparison.Ordinal)
                    && !parsedMessage.Contains("Error", StringComparison.OrdinalIgnoreCase)
                    && !parsedMessage.Contains("错误", StringComparison.Ordinal)
                    && !parsedMessage.Contains("失败", StringComparison.Ordinal)
                    && !parsedMessage.Contains("异常", StringComparison.Ordinal)
                    && !parsedMessage.Contains("Warn", StringComparison.OrdinalIgnoreCase)
                    && !parsedMessage.Contains("警告", StringComparison.Ordinal))
                {
                    level = LogLevelEnum.Debug;
                }
                else
                {
                    level = GuessLevel(parsedMessage);
                }

                if (level == LogLevelEnum.Info && string.IsNullOrWhiteSpace(module))
                {
                    level = LogLevelEnum.Debug;
                }

                if (!ShouldWrite(level, module))
                {
                    return;
                }
                if (level < LogLevelEnum.Warning && TryDeduplicate(message, parsedMessage, level))
                {
                    return;
                }

                var formatted = Format(level, module, parsedMessage, null);
                if (TM.App.IsDebugMode)
                {
                    Debug.WriteLine(formatted);
                }
                if (TM.App.IsDebugMode && level >= LogLevelEnum.Error)
                {
                    Console.WriteLine(formatted);
                }
                WriteToFile(level, formatted);
            }
            catch (Exception ex)
            {
                DebugLogOnce("WriteLog", ex);
            }
        }

        private static string NormalizeToPatternKey(string message)
        {
            var s = message.Length <= DeduplicateKeyMaxLength ? message : message.Substring(0, DeduplicateKeyMaxLength);
            s = NormalizeUrlRegex().Replace(s, "_URL_");
            s = NormalizePathRegex().Replace(s, "_PATH_");
            s = NormalizeVolChRegex().Replace(s, "vol_ch");
            s = NormalizeNumberRegex().Replace(s, "#");
            s = NormalizeVolRegex().Replace(s, "vol");
            s = NormalizeMissingPathRegex().Replace(s, "不存在: _PATH_");
            s = NormalizeSuffixRegex().Replace(s, "$1: _");
            s = NormalizeWarmupRegex().Replace(s, "[预热] _WARM_");
            return s;
        }

        private static string BuildDeduplicateKey(string message, string parsedMessage)
        {
            var keySource = ContainsAnyKeyword(parsedMessage, _globalDeduplicatePatterns, StringComparison.OrdinalIgnoreCase)
                ? parsedMessage
                : message;
            return NormalizeToPatternKey(keySource);
        }

        private static int GetDeduplicateWindowMs(string key)
        {
            if (ContainsAnyKeyword(key, _longWindowDeduplicatePatterns, StringComparison.OrdinalIgnoreCase))
                return LongDeduplicateWindowMs;
            if (ContainsAnyKeyword(key, _mediumWindowDeduplicatePatterns, StringComparison.OrdinalIgnoreCase))
                return MediumDeduplicateWindowMs;
            return DeduplicateWindowMs;
        }

        private static bool ShouldEmitDeduplicateSummary(string key)
        {
            return !ContainsAnyKeyword(key, _summarySuppressedDeduplicatePatterns, StringComparison.OrdinalIgnoreCase);
        }

        private bool TryDeduplicate(string message, string parsedMessage, LogLevelEnum level)
        {
            var key = BuildDeduplicateKey(message, parsedMessage);
            var windowMs = GetDeduplicateWindowMs(key);

            var now = DateTime.UtcNow;

            List<(string Key, int Count, LogLevelEnum Level)>? expiredSnapshot = null;
            (int Count, LogLevelEnum Level)? singleExpired = null;
            string? singleExpiredKey = null;
            bool isDuplicate;

            lock (_deduplicateLock)
            {
                if (++_deduplicateCallCount % 32 == 0)
                {
                    List<string>? expiredKeys = null;
                    foreach (var kv in _recentLogs)
                    {
                        if ((now - kv.Value.FirstSeen).TotalMilliseconds > GetDeduplicateWindowMs(kv.Key))
                        {
                            expiredKeys ??= new List<string>();
                            expiredKeys.Add(kv.Key);
                        }
                    }
                    if (expiredKeys != null)
                    {
                        expiredSnapshot = new List<(string, int, LogLevelEnum)>(expiredKeys.Count);
                        foreach (var ek in expiredKeys)
                        {
                            var entry = _recentLogs[ek];
                            _recentLogs.Remove(ek);
                            if (entry.Count > 1)
                                expiredSnapshot.Add((ek, entry.Count, entry.Level));
                        }
                    }
                }

                if (_recentLogs.TryGetValue(key, out var existing))
                {
                    if ((now - existing.FirstSeen).TotalMilliseconds <= windowMs)
                    {
                        _recentLogs[key] = (existing.Count + 1, existing.FirstSeen, existing.Level);
                        isDuplicate = true;
                        goto ExitLock;
                    }
                    _recentLogs.Remove(key);
                    if (existing.Count > 1)
                    {
                        singleExpiredKey = key;
                        singleExpired = (existing.Count, existing.Level);
                    }
                }
                if (_recentLogs.Count >= 256)
                {
                    _recentLogs.Clear();
                }

                _recentLogs[key] = (1, now, level);
                isDuplicate = false;

            ExitLock:;
            }

            if (expiredSnapshot != null && expiredSnapshot.Count > 0)
            {
                EmitExpiredBatchSummary(expiredSnapshot);
            }
            if (singleExpired != null && ShouldEmitDeduplicateSummary(singleExpiredKey!))
            {
                var count = singleExpired.Value.Count;
                if (count >= 3)
                {
                    var briefKey = BriefKey(singleExpiredKey!);
                    var summary = Format(singleExpired.Value.Level, null, $"（合并 {count} 条重复）{briefKey}", null);
                    WriteToFile(singleExpired.Value.Level, summary);
                }
            }

            return isDuplicate;
        }

        private void EmitExpiredBatchSummary(List<(string Key, int Count, LogLevelEnum Level)> expired)
        {
            var buckets = new Dictionary<LogLevelEnum, List<(string Key, int Count)>>();
            foreach (var (ek, count, lvl) in expired)
            {
                if (!ShouldEmitDeduplicateSummary(ek)) continue;
                if (!buckets.TryGetValue(lvl, out var list))
                {
                    list = new List<(string, int)>();
                    buckets[lvl] = list;
                }
                list.Add((ek, count));
            }

            foreach (var (lvl, list) in buckets)
            {
                var main = new List<(string Key, int Count)>();
                int tailItemCount = 0;
                int tailTotalRepeats = 0;
                foreach (var item in list)
                {
                    if (item.Count >= 3)
                        main.Add(item);
                    else
                    {
                        tailItemCount++;
                        tailTotalRepeats += item.Count;
                    }
                }

                if (main.Count == 0) continue;

                main.Sort((a, b) => b.Count.CompareTo(a.Count));
                const int maxMain = 6;
                if (main.Count > maxMain)
                {
                    for (int i = maxMain; i < main.Count; i++)
                    {
                        tailItemCount++;
                        tailTotalRepeats += main[i].Count;
                    }
                    main.RemoveRange(maxMain, main.Count - maxMain);
                }

                var sb = new StringBuilder(256);
                sb.Append("（合并 ").Append(main.Count + tailItemCount).Append(" 类）");
                for (int i = 0; i < main.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(BriefKey(main[i].Key)).Append('×').Append(main[i].Count);
                }
                if (tailItemCount > 0)
                {
                    if (main.Count > 0) sb.Append(", ");
                    sb.Append("另 ").Append(tailItemCount).Append(" 类共 ").Append(tailTotalRepeats).Append(" 条低频重复");
                }

                var summary = Format(lvl, null, sb.ToString(), null);
                WriteToFile(lvl, summary);
            }
        }

        private static string BriefKey(string key)
        {
            const int maxLen = 50;
            return key.Length > maxLen ? key.Substring(0, maxLen) + "…" : key;
        }

    }
}


