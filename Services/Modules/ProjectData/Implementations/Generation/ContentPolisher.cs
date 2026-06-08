using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TM.Services.Framework.AI.Core;
using TM.Services.Framework.AI.Mlm;
using TM.Services.Framework.AI.SemanticGuard;
using TM.Services.Framework.AI.SemanticKernel;
using TM.Services.Framework.AI.WritingConfig;

namespace TM.Services.Modules.ProjectData.Implementations.Generation
{
    public class ContentPolisher
    {
        private const string StyleMimicryPrompt = @"<role>你是文学润色专家。核心任务：**严格按 <polish_rules> 中的技巧对中文小说正文进行风格润色**，让语言更自然流畅、更贴近人类写作习惯。</role>

<polish_rules priority=""primary"">

### 1. 增加冗余与解释性
将简洁的动词或动词短语替换为更长的、带有动作过程描述的短语。
-   ""管理"" → ""开展...的管理工作"" 或 ""进行管理""
-   ""交互"" → ""进行交互"" 或 ""开展交互""
-   ""配置"" → ""进行配置""
-   ""处理"" → ""去处理...工作""
-   ""恢复"" → ""进行恢复""
-   ""实现"" → ""得以实现"" 或 ""来实现""
-   ""分析"" → ""对…进行分析""
-   ""验证"" → ""开展相关的验证工作""

在句子中添加语法上允许但非必需的词语，使句子更饱满。
-   适当增加 ""了""、""的""、""地""、""所""、""会""、""可以""、""这个""、""方面""、""当中"" 等。
-   ""提供功能"" → ""有...功能"" 或 ""拥有...的功能""

### 4. 括号内容处理
对于原文中用于解释、举例或说明缩写的括号 `(...)` 或 `（...）`：
-   **优先整合:** 尝试将括号内的信息自然地融入句子，使用 ""也就是""、""即""、""比如""、""像"" 等引导词。
    -   示例：`ORM（对象关系映射）` → `对象关系映射即ORM` 或 `ORM也就是对象关系映射`
    -   示例：`功能（如ORM、Admin）` → `功能，比如ORM、Admin` 或 `功能，像ORM、Admin等`
-   **谨慎省略:** 如果整合后语句极其冗长或别扭，并且括号内容并非核心关键信息，可以考虑省略。

-   示例：`视图 (views.py) 中` → `视图文件views.py中`
-   示例：`权限类 (admin_panel.permissions)` → `权限类 admin_panel.permissions`

### 5. ""把""字句 + ""给""字结构高频化
凡有动作对象的句子，优先重写为 ""把…给…"" 结构。
-   ""他拉下电闸"" → ""他去把电闸给拉了下来""
-   ""锁上门"" → ""把门给锁上了""
-   ""掏出手机"" → ""把手机给掏了出来""
-   ""撕下一张报纸"" → ""把其中一张给撕了下来""
-   ""打开所有的灯"" → ""把所有的灯都给打开了""

### 6. 虚词、副词、语气词的自然堆叠
让句子从“精炼”变“啰嗦”，但节奏受控：
-   连接词：增加 ""就""、""都""、""倒""、""还""、""也""、""可""
-   时态词：句末补 ""了""，如 ""已经发黄"" → ""已经发黄了""
-   语气词：句末加 ""罢了""、""呢""、""嘛""，如 ""运气好"" → ""纯粹是运气好罢了""
-   指示词：增加 ""那个""、""那些""、""这个""，如 ""老妇人"" → ""那个老妇人""
-   补语词：""下来""、""起来""、""出来""、""过去""，如 ""亮了"" → ""亮了起来""
-   程度词：""有""、""有点""、""差不多""、""左右""，如 ""几秒"" → ""有几秒钟""

### 7. 重复强调的节奏控制
将单次描述改为重复强调，模拟反复斟酌感。**不可以滥用，在关键场景增加重量**：
-   选择最有情绪强度的词做重复，避免每段都用
-   示例：""他点点头""、""她笑了笑""、""沉默了沉默""
-   示例：""黑乎乎的"" → ""黑乎乎的一片""

### 8. 句子拆短 + 心理代偿短语
将 ""短促 + 信息密度高"" 重写为 ""稍长 + 信息密度低""，加入心理代偿性短语：
-   ""告诉自己那是老鼠"" → ""在心底里头跟自己说那准是老鼠""
-   ""他知道她在笑"" → ""可他心里头清楚得很，她一定在笑""
-   ""决定先去主卧"" → ""就决定先去二楼主卧那边把行李给整理一下""
-   ""他认为"" → ""他还觉得"" / ""他心里头不这么认为了""

### 9. 句式微调与自然化
-   **使用""把""字句:** 在合适的场景下，倾向于使用""把""字句。
    -   示例：""会将对象移动"" → ""会把这个对象移动""
-   **条件句式转换:** 将较书面的条件句式改为稍口语化的形式。
    -   示例：""若…，则…"" → ""要是...，那就..."" 或 ""如果...，就...""
-   **结构切换:** 进行名词化与动词化结构的相互转换。
    -   示例：""为了将…解耦"" → ""为了实现...的解耦""
-   **增加连接词:** 在句首或句中适时添加""那么""、""这样一来""、""同时""等词。
---
以上只是基本举例，如果文章中有和以上例子相似的，也要根据例子灵活修改

</polish_rules>

<strict_rules>
1. 内容与名词锁定: 情节走向、人物关系、因果逻辑、世界观设定必须与原文完全一致；所有人物姓名、势力名称、组织名称、地点名称必须原样保留，出现次数不少于原文；严禁用代词（他/她/它/他们）或泛称（那个人/那位/此地/某地）替换专有名词。
2. 人称保持: 保持原文的叙事人称不变（原文是第一人称就保持第一人称，第三人称同理）。
3. 字数控制: 修改后的总字数{WORD_COUNT_HINT}必须与原文保持一致，偏差不超过 ±5%。<polish_rules> 中的技巧优先用于「替换」原表达，而不是「叠加」在原文上。
4. 输出约束: 维持原文段落划分不变，不遗漏任何段落或情节；只输出修改后的完整正文，不附加任何解释、注释或标签。
</strict_rules>";

        public async Task<PolishResult> PolishAsync(string rawContent, int polishModel = 0, CancellationToken ct = default)
        {
            var result = new PolishResult { OriginalContent = rawContent };

            var polishModelName = polishModel == 1 ? "在线润色" : "本地正则";
            TM.Services.Framework.AI.SemanticKernel.GenerationProgressHub.ReportPhase(
                TM.Services.Framework.AI.SemanticKernel.ProgressPhase.Polishing,
                $"开始润色（模型：{polishModelName}）...");

            try
            {
                var separatorIndex = GenerationGate.FindChangesStartIndex(rawContent);
                string contentPart;
                string? changesPart;
                if (separatorIndex < 0)
                {
                    TM.App.Log("[ContentPolisher] 未找到CHANGES分隔符，将全文作为正文润色");
                    contentPart = rawContent.TrimEnd();
                    changesPart = null;
                }
                else
                {
                    contentPart = rawContent.Substring(0, separatorIndex).TrimEnd();
                    changesPart = rawContent.Substring(separatorIndex);
                }

                GenerationProgressHub.Report("正在执行润前正则降 AI...");
                var preLen = WordCountHelper.CountRaw(contentPart);
                var scorers = BuildPickerScorers();

                int chapterTimeoutMs = GetChapterTimeoutMs();
                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    timeoutCts.CancelAfter(chapterTimeoutMs);
                    try
                    {
                        contentPart = await HumanizeRules.ApplyPreLLMAsync(contentPart, scorers, timeoutCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
                    {
                        TM.App.Log($"[ContentPolisher] 三重引擎章节超时 {chapterTimeoutMs}ms，退化为纯 N-gram");
                        GenerationProgressHub.Report("三重引擎超时，已退化为纯正则降 AI");
                        contentPart = await HumanizeRules.ApplyPreLLMAsync(contentPart, scorers: null, ct).ConfigureAwait(false);
                    }
                }
                var afterPreLen = WordCountHelper.CountRaw(contentPart);
                TM.App.Log($"[ContentPolisher] 润前正则：{preLen} 字 → {afterPreLen} 字");

                var prePolished = changesPart != null
                    ? $"{contentPart}\n\n{changesPart}"
                    : contentPart;
                result.PrePolishedContent = prePolished;

                if (polishModel == 0)
                {
                    result.PolishedContent = prePolished;
                    result.Success = true;
                    result.ContentWithoutChanges = contentPart;

                    var localOrigEffective = WordCountHelper.CountRaw(result.OriginalContent);
                    TM.App.Log($"[ContentPolisher][{GenerationCorrelation.Current}] 本地正则润色完成（不调 LLM），原文{localOrigEffective}字 → 润后{afterPreLen}字");
                    GenerationProgressHub.Report($"本地正则润色完成（{preLen}→{afterPreLen}字）");
                    return result;
                }

                GenerationProgressHub.Report($"润前正则完成（{preLen}→{afterPreLen}字），正在调用AI润色...");

                var wordCountHint = afterPreLen > 0 ? $"（约 {afterPreLen} 字）" : string.Empty;
                var styledPrompt = StyleMimicryPrompt.Replace("{WORD_COUNT_HINT}", wordCountHint);
                var polishPrompt = $"{styledPrompt}\n\n<source_text>\n{contentPart}\n</source_text>";
                const int maxRetries = 2;
                var aiSvc = ServiceLocator.Get<AIService>();
                var writingCfg = ServiceLocator.Get<WritingSettingsService>();
                var polishConfigId = writingCfg.GetPolishConfigId();
                if (string.IsNullOrWhiteSpace(polishConfigId) && writingCfg.TryMarkPolishFallbackNotified())
                {
                    TM.App.Log("[ContentPolisher] 未配置润色API，兜底使用主对话API");
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                        GlobalToast.Info("润色API未配置", "当前未配置专用润色API，已自动使用主对话API兜底。可在“写作配置”中设置专用润色模型。"));
                }

                var aiResult = await aiSvc.GenerateWithConfigAsync(polishConfigId, polishPrompt, ct).ConfigureAwait(false);
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    if (aiResult.Success && !string.IsNullOrWhiteSpace(aiResult.Content))
                        break;
                    var delayMs = 1000 + new Random().Next(0, 2001);
                    TM.App.Log($"[ContentPolisher] 润色第{attempt + 1}次重试（等待{delayMs}ms）...");
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                    aiResult = await aiSvc.GenerateWithConfigAsync(polishConfigId, polishPrompt, ct).ConfigureAwait(false);
                }

                if (!aiResult.Success || string.IsNullOrWhiteSpace(aiResult.Content))
                {
                    result.Success = false;
                    result.ErrorMessage = $"润色失败: {aiResult.ErrorMessage ?? "AI未返回内容"}";
                    result.PolishedContent = rawContent;
                    TM.App.Log($"[ContentPolisher][{GenerationCorrelation.Current}] 润色失败（已重试{maxRetries}次），使用原文: {result.ErrorMessage}");
                    return result;
                }

                var rawLen = WordCountHelper.CountRaw(contentPart);
                var trimmedAiContent = aiResult.Content.Trim();
                var resultLen = WordCountHelper.CountRaw(trimmedAiContent);
                if (rawLen > 200 && resultLen < rawLen * 0.4)
                {
                    result.Success = false;
                    result.ErrorMessage = $"润色结果异常（原文{rawLen}字 → 结果{resultLen}字，疑似模型拒绝），使用原文";
                    result.PolishedContent = rawContent;
                    TM.App.Log($"[ContentPolisher][{GenerationCorrelation.Current}] {result.ErrorMessage}");
                    return result;
                }

                if (rawLen > 200 && resultLen > rawLen * 1.20)
                {
                    var pctOver = (resultLen - rawLen) * 100 / rawLen;
                    var retryHint = $"\n\n<retry_hint priority=\"strict\">⚠️ 上次输出 {resultLen} 字（原文 {rawLen} 字 +{pctOver}%），严重超出预算。请严格执行 <strict_rules> 第 3 条字数控制：输出必须严格控制在原文 {rawLen} 字 ±5% 以内。<polish_rules> 仅用于「替换」原表达，禁止「叠加」扩写。</retry_hint>";
                    var retryPrompt = polishPrompt + retryHint;
                    TM.App.Log($"[ContentPolisher][{GenerationCorrelation.Current}] 字数超 +{pctOver}%（{rawLen}→{resultLen}），自动重试 1 次以严格控制字数");
                    GenerationProgressHub.Report($"润色字数超 +{pctOver}%，正在重试以收敛...");
                    try
                    {
                        var retryResult = await aiSvc.GenerateWithConfigAsync(polishConfigId, retryPrompt, ct).ConfigureAwait(false);
                        if (retryResult.Success && !string.IsNullOrWhiteSpace(retryResult.Content))
                        {
                            var retryTrimmed = retryResult.Content.Trim();
                            var retryLen = WordCountHelper.CountRaw(retryTrimmed);
                            if (Math.Abs(retryLen - rawLen) < Math.Abs(resultLen - rawLen))
                            {
                                aiResult = retryResult;
                                trimmedAiContent = retryTrimmed;
                                resultLen = retryLen;
                                TM.App.Log($"[ContentPolisher][{GenerationCorrelation.Current}] 重试成功，字数收敛到 {retryLen} 字");
                            }
                            else
                            {
                                TM.App.Log($"[ContentPolisher][{GenerationCorrelation.Current}] 重试结果 {retryLen} 字未优于首次 {resultLen} 字，沿用首次结果");
                            }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        TM.App.Log($"[ContentPolisher][{GenerationCorrelation.Current}] 字数重试异常（沿用首次结果）: {ex.Message}");
                    }
                }

                var polishedContent = aiResult.Content.Trim();

                var cleanIdx = GenerationGate.FindSeparatorIndex(polishedContent).index;
                if (cleanIdx > 0)
                {
                    polishedContent = polishedContent.Substring(0, cleanIdx).TrimEnd();
                    TM.App.Log("[ContentPolisher] 清洗润色结果中残留的CHANGES段");
                }

                GenerationProgressHub.Report("正在执行润后正则兑底...");
                var postLen = WordCountHelper.CountRaw(polishedContent);
                polishedContent = await HumanizeRules.ApplyPostLLMAsync(polishedContent, ct).ConfigureAwait(false);
                var afterPostLen = WordCountHelper.CountRaw(polishedContent);
                TM.App.Log($"[ContentPolisher] 润后正则：{postLen} 字 → {afterPostLen} 字");
                GenerationProgressHub.Report($"润后正则完成（{postLen}→{afterPostLen}字）");

                result.PolishedContent = changesPart != null
                    ? $"{polishedContent}\n\n{changesPart}"
                    : polishedContent;
                result.Success = true;
                result.ContentWithoutChanges = polishedContent;

                TM.App.Log($"[ContentPolisher][{GenerationCorrelation.Current}] 润色成功，原文{rawLen}字 → 润色后{afterPostLen}字{(changesPart == null ? "（无CHANGES块）" : "")}");

                return result;
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "润色已取消";
                result.PolishedContent = rawContent;
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"润色异常: {ex.Message}";
                result.PolishedContent = rawContent;
                TM.App.Log($"[ContentPolisher][{GenerationCorrelation.Current}] 润色异常: {ex.Message}");
                return result;
            }
        }

        private static PickerScorers? BuildPickerScorers()
        {
            if (!ServiceLocator.IsInitialized) return null;

            try
            {
                var writingCfg = ServiceLocator.TryGet<WritingSettingsService>();
                var settings = writingCfg?.Settings;

                if (settings != null && !settings.HumanizePickerEnabled)
                {
                    return null;
                }

                var mlm = ServiceLocator.TryGet<IMicroMlmService>();
                var guard = ServiceLocator.TryGet<ISemanticGuard>();

                if (mlm == null && guard == null) return null;

                return new PickerScorers
                {
                    Mlm = mlm,
                    Guard = guard,
                    Options = new PickerOptions
                    {
                        GuardCosineThreshold = settings?.HumanizeGuardCosineThreshold ?? 0.85f,
                        GuardWindowChars = settings?.HumanizeGuardWindowChars ?? 50,
                    },
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[ContentPolisher] BuildPickerScorers 失败（PickBestAsync 退化为纯 N-gram）: {ex.Message}");
                return null;
            }
        }

        private static int GetChapterTimeoutMs()
        {
            try
            {
                if (!ServiceLocator.IsInitialized) return 15000;
                var settings = ServiceLocator.TryGet<WritingSettingsService>()?.Settings;
                return settings?.HumanizePickerChapterTimeoutMs ?? 15000;
            }
            catch
            {
                return 15000;
            }
        }

    }

    public class PolishResult
    {
        public bool Success { get; set; }

        public string? ErrorMessage { get; set; }

        public string OriginalContent { get; set; } = string.Empty;

        public string PolishedContent { get; set; } = string.Empty;

        public string? ContentWithoutChanges { get; set; }

        public string? PrePolishedContent { get; set; }
    }
}
