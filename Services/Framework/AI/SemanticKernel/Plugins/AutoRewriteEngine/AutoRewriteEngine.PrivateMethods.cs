using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TM.Framework.UI.Workspace.Services.Spec;
using TM.Services.Modules.ProjectData.Implementations.Generation;
using TM.Services.Modules.ProjectData.Models.Guides;
using TM.Services.Modules.ProjectData.Models.TaskContexts;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Framework.AI.SemanticKernel.Plugins
{
    public partial class AutoRewriteEngine
    {
        private static readonly System.Text.RegularExpressions.Regex FeedbackNameRegex = new(@"（反馈了名称'[^']+'）", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly Dictionary<string, string> EnglishChangeFieldToChinese = new(StringComparer.Ordinal)
        {
            { "CharacterStateChanges", "角色状态变化" },
            { "ConflictProgress", "冲突进度" },
            { "ForeshadowingActions", "伏笔动作" },
            { "NewPlotPoints", "新增情节" },
            { "LocationStateChanges", "地点状态变化" },
            { "FactionStateChanges", "势力状态变化" },
            { "TimeProgression", "时间推进" },
            { "CharacterMovements", "角色移动" },
            { "ItemTransfers", "物品流转" },
            { "SecretRevealChanges", "秘密揭示" },
            { "PledgeConstraintChanges", "承诺约束变化" },
            { "DeadlineConstraintChanges", "倒计时约束变化" },
        };

        internal static string HumanizeFailureForUser(string failureMsg)
        {
            if (string.IsNullOrEmpty(failureMsg)) return failureMsg;
            var s = failureMsg;
            foreach (var (en, cn) in EnglishChangeFieldToChinese)
                s = s.Replace($"CHANGES.{en}", $"「{cn}」", StringComparison.Ordinal);
            return s;
        }

        #region 私有方法

        private ContentTaskContext DegradeContext(
            ContentTaskContext original,
            FactSnapshot factSnapshot,
            CreativeSpec? spec,
            string systemPrompt,
            int safeInputLimit,
            int initialTotalTokens)
        {
            var ctx = CloneTaskContext(original);
            var systemTokens = TokenEstimator.CountTokens(systemPrompt);

            var estimatedTotal = initialTotalTokens;

            bool VerifyFits()
            {
                var prompt = BuildPromptWithFailures(ctx, factSnapshot, spec);
                return systemTokens + TokenEstimator.CountTokens(prompt) <= safeInputLimit;
            }

            {
                if (ctx.Blueprints?.Count > 0 && ctx.Scenes?.Count > 0)
                    ctx.Scenes = new();
                if (ctx.MdPreviousChapterSummaries?.Count > 0 && ctx.PreviousChapterSummaries?.Count > 0)
                    ctx.PreviousChapterSummaries = new();
            }

            if (ctx.LongDistanceRecallFragments.Count > 0)
            {
                var removedTokens = EstimateFragmentTokens(ctx.LongDistanceRecallFragments) + 30;
                ctx.LongDistanceRecallFragments = new();
                estimatedTotal -= removedTokens;
                TM.App.Log($"[AutoRewriteEngine] 降级 Tier1: 清除 LongDistanceRecallFragments (~-{removedTokens} tokens, est={estimatedTotal})");
                if (estimatedTotal <= safeInputLimit && VerifyFits()) return ctx;
            }

            if (ctx.FirstDescriptionSnippets?.Count > 0)
            {
                var removedTokens = EstimateFirstDescSnippetsTokens(ctx.FirstDescriptionSnippets) + 30;
                ctx.FirstDescriptionSnippets = new();
                estimatedTotal -= removedTokens;
                TM.App.Log($"[AutoRewriteEngine] 降级 Tier1.5: 清除 FirstDescriptionSnippets (~-{removedTokens} tokens, est={estimatedTotal})");
                if (estimatedTotal <= safeInputLimit && VerifyFits()) return ctx;
            }

            if (ctx.HistoricalMilestones.Count > 3)
            {
                var removed = ctx.HistoricalMilestones.Take(ctx.HistoricalMilestones.Count - 3);
                var removedTokens = EstimateMilestoneTokens(removed);
                ctx.HistoricalMilestones = ctx.HistoricalMilestones.TakeLast(3).ToList();
                estimatedTotal -= removedTokens;
                TM.App.Log($"[AutoRewriteEngine] 降级 Tier2: HistoricalMilestones → 3卷 (~-{removedTokens} tokens, est={estimatedTotal})");
                if (estimatedTotal <= safeInputLimit && VerifyFits()) return ctx;
            }

            if (ctx.PreviousVolumeArchives.Count > 2)
            {
                var removed = ctx.PreviousVolumeArchives.Take(ctx.PreviousVolumeArchives.Count - 2);
                var removedTokens = EstimateArchiveTokens(removed);
                ctx.PreviousVolumeArchives = ctx.PreviousVolumeArchives.TakeLast(2).ToList();
                estimatedTotal -= removedTokens;
                TM.App.Log($"[AutoRewriteEngine] 降级 Tier3: PreviousVolumeArchives → 2卷 (~-{removedTokens} tokens, est={estimatedTotal})");
                if (estimatedTotal <= safeInputLimit && VerifyFits()) return ctx;
            }

            if (ctx.PreviousChapterSummaries?.Count > 10)
            {
                var removed = ctx.PreviousChapterSummaries.Take(ctx.PreviousChapterSummaries.Count - 10);
                var removedTokens = EstimateSummaryTokens(removed);
                ctx.PreviousChapterSummaries = ctx.PreviousChapterSummaries.TakeLast(10).ToList();
                estimatedTotal -= removedTokens;
                TM.App.Log($"[AutoRewriteEngine] 降级 Tier4: PreviousChapterSummaries → 10章 (~-{removedTokens} tokens, est={estimatedTotal})");
                if (estimatedTotal <= safeInputLimit && VerifyFits()) return ctx;
            }
            if (ctx.MdPreviousChapterSummaries?.Count > 10)
            {
                var removed = ctx.MdPreviousChapterSummaries.Take(ctx.MdPreviousChapterSummaries.Count - 10);
                var removedTokens = EstimateSummaryTokens(removed);
                ctx.MdPreviousChapterSummaries = ctx.MdPreviousChapterSummaries.TakeLast(10).ToList();
                estimatedTotal -= removedTokens;
                TM.App.Log($"[AutoRewriteEngine] 降级 Tier4: MdPreviousChapterSummaries → 10章 (~-{removedTokens} tokens, est={estimatedTotal})");
                if (estimatedTotal <= safeInputLimit && VerifyFits()) return ctx;
            }

            if (ctx.PreviousChapterSummaries?.Count > 5)
            {
                var removed = ctx.PreviousChapterSummaries.Take(ctx.PreviousChapterSummaries.Count - 5);
                var removedTokens = EstimateSummaryTokens(removed);
                ctx.PreviousChapterSummaries = ctx.PreviousChapterSummaries.TakeLast(5).ToList();
                estimatedTotal -= removedTokens;
                TM.App.Log($"[AutoRewriteEngine] 降级 Tier4.5: PreviousChapterSummaries → 5章 (~-{removedTokens} tokens, est={estimatedTotal})");
                if (estimatedTotal <= safeInputLimit && VerifyFits()) return ctx;
            }
            if (ctx.MdPreviousChapterSummaries?.Count > 5)
            {
                var removed = ctx.MdPreviousChapterSummaries.Take(ctx.MdPreviousChapterSummaries.Count - 5);
                var removedTokens = EstimateSummaryTokens(removed);
                ctx.MdPreviousChapterSummaries = ctx.MdPreviousChapterSummaries.TakeLast(5).ToList();
                estimatedTotal -= removedTokens;
                TM.App.Log($"[AutoRewriteEngine] 降级 Tier4.5: MdPreviousChapterSummaries → 5章 (~-{removedTokens} tokens, est={estimatedTotal})");
                if (estimatedTotal <= safeInputLimit && VerifyFits()) return ctx;
            }

            ctx.HistoricalMilestones = new();
            ctx.PreviousVolumeArchives = new();
            ctx.PreviousChapterSummaries = (ctx.PreviousChapterSummaries ?? new()).TakeLast(5).ToList();
            ctx.MdPreviousChapterSummaries = (ctx.MdPreviousChapterSummaries ?? new()).TakeLast(5).ToList();
            ctx.FirstDescriptionSnippets = new();
            if (ctx.CompressedKeyEvents.Count == 0 && original.CompressedKeyEvents.Count > 0)
                ctx.CompressedKeyEvents = original.CompressedKeyEvents;
            TM.App.Log($"[AutoRewriteEngine] 降级 Tier5: 清除所有长距离层，摘要保留5章，关键事件={ctx.CompressedKeyEvents.Count}条");
            if (VerifyFits()) return ctx;

            {
                int bChars = ctx.Characters.Count, bExp = ctx.ExpandedCharacters.Count;
                int bFac = ctx.Factions.Count, bLoc = ctx.Locations.Count;
                int bWR = ctx.WorldRules.Count, bPR = ctx.PlotRules.Count;

                if (ctx.Characters.Count > 15)
                    ctx.Characters = ctx.Characters.Take(15).ToList();
                ctx.ExpandedCharacters = new();
                if (ctx.Factions.Count > 10)
                    ctx.Factions = ctx.Factions.Take(10).ToList();
                if (ctx.Locations.Count > 15)
                    ctx.Locations = ctx.Locations.Take(15).ToList();
                if (ctx.WorldRules.Count > 3)
                    ctx.WorldRules = ctx.WorldRules.Take(3).ToList();
                if (ctx.PlotRules.Count > 10)
                    ctx.PlotRules = ctx.PlotRules.Take(10).ToList();
                ctx.Templates = new();

                TM.App.Log($"[AutoRewriteEngine] 降级 Tier6: 任务层裁剪 (角色{bChars}→{ctx.Characters.Count}, 扩展角色{bExp}→0, " +
                    $"势力{bFac}→{ctx.Factions.Count}, 地点{bLoc}→{ctx.Locations.Count}, " +
                    $"世界观{bWR}→{ctx.WorldRules.Count}, 剧情{bPR}→{ctx.PlotRules.Count})");
            }

            return ctx;
        }

        private static string BuildTokenDiagnostics(ContentTaskContext ctx, FactSnapshot? snapshot, int systemTokens)
        {
            var sb = new StringBuilder();
            sb.Append($"SystemPrompt={systemTokens}");
            sb.Append($", Characters={ctx.Characters.Count}");
            sb.Append($", ExpandedChars={ctx.ExpandedCharacters.Count}");
            sb.Append($", Factions={ctx.Factions.Count}");
            sb.Append($", Locations={ctx.Locations.Count}");
            sb.Append($", WorldRules={ctx.WorldRules.Count}");
            sb.Append($", PlotRules={ctx.PlotRules.Count}");
            sb.Append($", Blueprints={ctx.Blueprints.Count}");
            sb.Append($", Summaries={ctx.PreviousChapterSummaries.Count}+{ctx.MdPreviousChapterSummaries.Count}");
            sb.Append($", Milestones={ctx.HistoricalMilestones.Count}");
            sb.Append($", Archives={ctx.PreviousVolumeArchives.Count}");
            sb.Append($", VectorFragments={ctx.LongDistanceRecallFragments.Count}");
            if (snapshot != null)
            {
                sb.Append($", Snap(Char={snapshot.CharacterStates?.Count ?? 0}");
                sb.Append($",Conflict={snapshot.ConflictProgress?.Count ?? 0}");
                sb.Append($",Foreshadow={snapshot.ForeshadowingStatus?.Count ?? 0}");
                sb.Append($",Loc={snapshot.LocationStates?.Count ?? 0}");
                sb.Append($",Faction={snapshot.FactionStates?.Count ?? 0}");
                sb.Append($",Item={snapshot.ItemStates?.Count ?? 0}");
                sb.Append($",Secret={snapshot.SecretStates?.Count ?? 0}");
                sb.Append($",Pledge={snapshot.PledgeStates?.Count ?? 0}");
                sb.Append($",Deadline={snapshot.DeadlineStates?.Count ?? 0})");
            }
            return sb.ToString();
        }

        private static string BuildSectionTokenBreakdown(string prompt, int maxSections = 12)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return string.Empty;
            var entries = new List<(string Name, int Tokens, int Chars)>();
            const string marker = "<section name=\"";
            const string endMarker = "</section>";
            var idx = 0;
            while (true)
            {
                var start = prompt.IndexOf(marker, idx, StringComparison.Ordinal);
                if (start < 0) break;
                var nameStart = start + marker.Length;
                var nameEnd = prompt.IndexOf('"', nameStart);
                if (nameEnd < 0) break;
                var name = prompt.Substring(nameStart, nameEnd - nameStart);

                var end = prompt.IndexOf(endMarker, nameEnd, StringComparison.Ordinal);
                if (end < 0) break;
                end += endMarker.Length;

                var block = prompt.Substring(start, end - start);
                entries.Add((name, TokenEstimator.CountTokens(block), block.Length));
                idx = end;
            }

            if (entries.Count == 0) return string.Empty;
            var total = TokenEstimator.CountTokens(prompt);
            var top = entries.OrderByDescending(e => e.Tokens).Take(maxSections).ToList();
            return $"PromptTokens={total}; TopSections=" + string.Join(", ", top.Select(e => $"{e.Name}:{e.Tokens}"));
        }

        private static int EstimateFragmentTokens(List<LongDistanceRecallFragment> fragments)
        {
            if (fragments.Count == 0) return 0;
            var sb = new StringBuilder();
            foreach (var f in fragments)
            {
                sb.Append(f.ChapterId);
                sb.AppendLine(f.Content);
            }
            return TokenEstimator.CountTokens(sb.ToString());
        }

        private static int EstimateFirstDescSnippetsTokens(List<FirstDescriptionSnippet> snippets)
        {
            if (snippets == null || snippets.Count == 0) return 0;
            var sb = new StringBuilder();
            foreach (var s in snippets)
            {
                sb.Append(s.EntityName).Append(s.ChapterId);
                sb.AppendLine(s.Content);
            }
            return TokenEstimator.CountTokens(sb.ToString());
        }

        private static int EstimateMilestoneTokens(IEnumerable<VolumeMilestoneEntry> milestones)
        {
            var sb = new StringBuilder();
            foreach (var m in milestones)
                sb.AppendLine(m.Milestone);
            return TokenEstimator.CountTokens(sb.ToString());
        }

        private static int EstimateSummaryTokens(IEnumerable<ChapterSummaryEntry> summaries)
        {
            var sb = new StringBuilder();
            foreach (var s in summaries)
                sb.AppendLine(s.Summary);
            return TokenEstimator.CountTokens(sb.ToString());
        }

        private static int EstimateArchiveTokens(IEnumerable<VolumeFactArchive> archives)
        {
            var sb = new StringBuilder();
            foreach (var a in archives)
            {
                if (a.CharacterStates != null)
                    foreach (var cs in a.CharacterStates)
                        sb.Append(cs.Name).Append(cs.Stage).Append(cs.Abilities).Append(cs.Relationships);
                if (a.ConflictProgress != null)
                    foreach (var cf in a.ConflictProgress)
                        sb.Append(cf.Name).Append(cf.Status);
                if (a.Timeline != null)
                    foreach (var t in a.Timeline)
                        sb.Append(t.TimePeriod).Append(t.ElapsedTime).Append(t.KeyTimeEvent);
                if (a.CharacterLocations != null)
                    foreach (var loc in a.CharacterLocations)
                        sb.Append(loc.CharacterName).Append(loc.CurrentLocation);
                if (a.FactionStates != null)
                    foreach (var fac in a.FactionStates)
                        sb.Append(fac.Name).Append(fac.Status);
                if (a.LocationStates != null)
                    foreach (var ls in a.LocationStates)
                        sb.Append(ls.Name).Append(ls.Status);
                if (a.ItemStates != null)
                    foreach (var item in a.ItemStates)
                        sb.Append(item.Name).Append(item.CurrentHolder).Append(item.Status);
                if (a.ForeshadowingStatus != null)
                    foreach (var fs in a.ForeshadowingStatus)
                        sb.Append(fs.Name);
                if (a.SecretStates != null)
                    foreach (var sec in a.SecretStates)
                        sb.Append(sec.Name).Append(sec.Status);
                if (a.PledgeStates != null)
                    foreach (var p in a.PledgeStates)
                        sb.Append(p.Name).Append(p.Type).Append(p.Status).Append(p.Condition);
                if (a.DeadlineStates != null)
                    foreach (var d in a.DeadlineStates)
                        sb.Append(d.Name).Append(d.Type).Append(d.Deadline);
            }
            return (int)(TokenEstimator.CountTokens(sb.ToString()) * 1.3);
        }

        private static ContentTaskContext CloneTaskContext(ContentTaskContext src)
        {
            return new ContentTaskContext
            {
                ContextMode = src.ContextMode,
                ChapterId = src.ChapterId,
                Title = src.Title,
                Summary = src.Summary,
                Characters = new(src.Characters),
                Locations = new(src.Locations),
                PlotRules = new(src.PlotRules),
                Factions = new(src.Factions),
                WorldRules = new(src.WorldRules),
                Templates = new(src.Templates),
                VolumeOutline = src.VolumeOutline,
                ChapterPlan = src.ChapterPlan,
                Blueprints = new(src.Blueprints),
                VolumeDesign = src.VolumeDesign,
                PreviousChapterSummary = src.PreviousChapterSummary,
                Rhythm = src.Rhythm,
                Scenes = new(src.Scenes),
                PreviousChapterSummaries = new(src.PreviousChapterSummaries),
                MdPreviousChapterSummaries = new(src.MdPreviousChapterSummaries),
                PreviousChapterTail = src.PreviousChapterTail,
                PreviousChapterId = src.PreviousChapterId,
                ExpandedCharacters = new(src.ExpandedCharacters),
                IsKeySceneExpanded = src.IsKeySceneExpanded,
                FactSnapshot = src.FactSnapshot,
                ContextIds = src.ContextIds,
                HistoricalMilestones = new(src.HistoricalMilestones),
                LongDistanceRecallFragments = new(src.LongDistanceRecallFragments),
                PreviousVolumeArchives = new(src.PreviousVolumeArchives),
                StateDivergenceWarnings = new(src.StateDivergenceWarnings),
                CompressedKeyEvents = new(src.CompressedKeyEvents),
                FirstDescriptionSnippets = new(src.FirstDescriptionSnippets),
                RepairHints = src.RepairHints,
            };
        }

        private static ConsistencyIssueRegistry.BaselineScope ComputeChangesOnlyScope(GateResult gateResult)
        {
            var scope = ConsistencyIssueRegistry.BaselineScope.None;
            foreach (var f in gateResult.Failures)
                foreach (var i in f.ConsistencyIssues)
                    scope |= GetRegisteredDescriptor(i.IssueType).Scope;
            return scope;
        }

        private static bool IsChangesOnlyFailure(GateResult gateResult)
        {
            if (gateResult.Success || gateResult.Failures.Count == 0) return false;
            return gateResult.Failures.All(f =>
                f.Type == FailureType.Protocol ||
                (f.Type == FailureType.Consistency && f.ConsistencyIssues.All(i =>
                    GetRegisteredDescriptor(i.IssueType).IsChangesOnly)));
        }

        private static ConsistencyIssueRegistry.Descriptor GetRegisteredDescriptor(string issueType)
        {
            if (ConsistencyIssueRegistry.All.TryGetValue(issueType, out var desc))
                return desc;
            throw new InvalidOperationException($"[ConsistencyIssueRegistry] 未知 IssueType = {issueType}，请在 ConsistencyIssueRegistry.All 中补充注册");
        }

        private static void HandlePolishFailure(int polishControl, PolishResult polishResult, ref string finalContent, string failureReason, string correlationId)
        {
            switch (polishControl)
            {
                case 2:
                    throw new PolishFatalException($"润色失败（{failureReason}），按润色控制[终止落盘]终止本章");

                case 1:
                    if (!string.IsNullOrWhiteSpace(polishResult.PrePolishedContent))
                    {
                        finalContent = polishResult.PrePolishedContent!;
                        TM.App.Log($"[AutoRewriteEngine][{correlationId}] 正则降级：使用本地正则模式产出（{failureReason}）");
                        GenerationProgressHub.Report("⚠ 已切换到正则降级（本地正则结果）");
                    }
                    else
                    {
                        TM.App.Log($"[AutoRewriteEngine][{correlationId}] 正则降级未生效（PrePolishedContent 为空），保留原文：{failureReason}");
                        GenerationProgressHub.Report("⚠ 正则降级未生效，保留原文");
                    }
                    break;

                case 0:
                default:
                    TM.App.Log($"[AutoRewriteEngine][{correlationId}] 使用原文：{failureReason}");
                    GenerationProgressHub.Report("⚠ 使用原文（润色失败）");
                    break;
            }
        }

        private string BuildRewriteFeedback(List<string> failures, FactSnapshot? factSnapshot = null, bool changesOnly = false, bool wordCountOnly = false, bool wordCountOverLimit = false, ConsistencyIssueRegistry.BaselineScope changesOnlyScope = ConsistencyIssueRegistry.BaselineScope.None, ContextIdCollection? contextIds = null, bool contentRepetitionOnly = false)
        {
            var sb = new StringBuilder();
            var rewriteReason = contentRepetitionOnly ? "content_repetition" : wordCountOnly ? (wordCountOverLimit ? "word_count_over_limit" : "word_count_insufficient") : "validation_failure";
            sb.AppendLine($"<rewrite_feedback reason=\"{rewriteReason}\">");
            var isPreDetectMissingChanges = changesOnly && failures.Any(f => !string.IsNullOrWhiteSpace(f)
                    && (f.Contains("输出缺少CHANGES", StringComparison.Ordinal)
                        || f.Contains("未包含 ---CHANGES---", StringComparison.Ordinal)
                        || f.Contains(ChapterChanges.ChangesXmlOpen, StringComparison.Ordinal)
                        || f.Contains("变更摘要标签", StringComparison.Ordinal)));
            if (changesOnly)
            {
                if (isPreDetectMissingChanges)
                {
                    sb.AppendLine($"你上次生成的输出缺少 {ChapterChanges.ChangesXmlOpen}...{ChapterChanges.ChangesXmlClose} 变更摘要标签（可能是输出被截断或模型漏写）。");
                    sb.AppendLine($"请尽量保留上次正文不改；若正文尾部确实被截断，可先补齐缺失的尾部正文，然后在末尾用成对的 {ChapterChanges.ChangesXmlOpen} 与 {ChapterChanges.ChangesXmlClose} 标签包裹完整 JSON 变更摘要。");
                }
                else
                {
                    sb.AppendLine("你上次生成的**正文已通过校验**，无需修改正文内容。");
                    sb.AppendLine($"请原样保留上次的完整正文，仅在末尾用成对的 {ChapterChanges.ChangesXmlOpen} 与 {ChapterChanges.ChangesXmlClose} 标签重新输出正确的 JSON 变更摘要。");
                }
                sb.AppendLine("以下是 CHANGES 中的具体问题：");
            }
            else if (wordCountOnly)
            {
                sb.AppendLine("你上次生成的内容**已通过一致性校验**（人物/地点/物品/情节均正确）。");
                if (wordCountOverLimit)
                    sb.AppendLine("**字数超限**，请在保持原有情节走向、人物行为和CHANGES记录完全不变的前提下，删减正文至目标字数：");
                else
                    sb.AppendLine("**字数不达标**，请在保持原有情节走向、人物行为和CHANGES记录完全不变的前提下，扩充正文至目标字数：");
            }
            else if (contentRepetitionOnly)
            {
                sb.AppendLine("你上次生成的内容**已通过一致性校验**，但**正文存在重复内容**。");
                sb.AppendLine("请保留CHANGES记录不变，重新生成正文，确保：");
                sb.AppendLine("- 每个段落必须推进新内容，禁止在不同段落重复表达同一信息或场景");
                sb.AppendLine("- 从上一章结尾状态直接向前推进，不得复述上一章尾部已发生的动作、对话或结论");
                sb.AppendLine("- 每个场景必须产生至少一项实质推进（行动结果/信息揭示/关系变化/冲突升级）");
                sb.AppendLine("以下是检测到的具体重复问题：");
            }
            else
            {
                sb.AppendLine("你上次生成的内容未通过校验，请根据以下问题重新生成完整章节：");
            }
            sb.AppendLine();

            var reasonsToAppend = failures.Take(MaxFailureReasonsPerRewrite).ToList();
            for (int i = 0; i < reasonsToAppend.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {reasonsToAppend[i]}");
            }

            if (changesOnly && factSnapshot != null)
            {
                if ((changesOnlyScope & ConsistencyIssueRegistry.BaselineScope.Location) != 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("<hard_baseline mandatory=\"true\" scope=\"changes_consistency\">");
                    sb.AppendLine("以下为账本基线（必须严格对齐）：");
                    sb.AppendLine("1) 若输出角色移动：角色ID可写名称或ShortId（系统自动解析）；出发地点可留空由系统从账本补值；若填写则必须等于该角色账本当前位置（同章多次移动时 FromLocation=上一次ToLocation）；");
                    sb.AppendLine("2) 若输出物品流转：物品ID/原持有者/新持有者可写名称或ShortId（系统自动解析）；原持有者可留空由系统从账本补值；同章多次转手时起点必须与上次终点一致.\n");

                    if (factSnapshot.CharacterLocations != null && factSnapshot.CharacterLocations.Count > 0)
                    {
                        sb.AppendLine("【角色当前位置】格式：角色名（角色ShortId）: 地点名（地点ShortId）");
                        var locDescMapCo = factSnapshot.LocationDescriptions ?? new System.Collections.Generic.Dictionary<string, LocationCoreDescription>();
                        var locIdToNameCo = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var ls in factSnapshot.LocationStates ?? new System.Collections.Generic.List<LocationStateSnapshot>())
                            if (!string.IsNullOrWhiteSpace(ls.Id) && !string.IsNullOrWhiteSpace(ls.Name)) locIdToNameCo[ls.Id] = ls.Name;
                        foreach (var (lid, ld) in locDescMapCo)
                            if (!string.IsNullOrWhiteSpace(lid) && !string.IsNullOrWhiteSpace(ld.Name)) locIdToNameCo[lid] = ld.Name;
                        foreach (var loc in factSnapshot.CharacterLocations)
                        {
                            var charName = string.IsNullOrWhiteSpace(loc.CharacterName) ? loc.CharacterId : loc.CharacterName;
                            if (string.IsNullOrWhiteSpace(charName) || string.IsNullOrWhiteSpace(loc.CurrentLocation)) continue;
                            string locDisplay;
                            if (TM.Framework.Common.Helpers.Id.ShortIdGenerator.IsLikelyId(loc.CurrentLocation)
                                && locIdToNameCo.TryGetValue(loc.CurrentLocation, out var lNameCo))
                                locDisplay = $"{lNameCo}\uff08{loc.CurrentLocation}\uff09";
                            else
                                locDisplay = loc.CurrentLocation;
                            sb.AppendLine($"- {charName}\uff08{loc.CharacterId}\uff09: {locDisplay}");
                        }
                        sb.AppendLine();
                    }

                    if (factSnapshot.ItemStates != null && factSnapshot.ItemStates.Count > 0)
                    {
                        sb.AppendLine("【物品持有者】格式：物品名（物品ShortId）: 持有者名（角色ShortId）");
                        var charDescMapCo = factSnapshot.CharacterDescriptions ?? new System.Collections.Generic.Dictionary<string, CharacterCoreDescription>();
                        var charIdToNameCo = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var cs in factSnapshot.CharacterStates ?? new System.Collections.Generic.List<CharacterStateSnapshot>())
                            if (!string.IsNullOrWhiteSpace(cs.Id) && !string.IsNullOrWhiteSpace(cs.Name)) charIdToNameCo[cs.Id] = cs.Name;
                        foreach (var (cid, cdesc) in charDescMapCo)
                            if (!string.IsNullOrWhiteSpace(cid) && !string.IsNullOrWhiteSpace(cdesc.Name)) charIdToNameCo[cid] = cdesc.Name;
                        foreach (var item in factSnapshot.ItemStates)
                        {
                            if (string.IsNullOrWhiteSpace(item.Name)) continue;
                            string holderDisplay;
                            if (string.IsNullOrWhiteSpace(item.CurrentHolder))
                                holderDisplay = "无人持有";
                            else if (TM.Framework.Common.Helpers.Id.ShortIdGenerator.IsLikelyId(item.CurrentHolder)
                                     && charIdToNameCo.TryGetValue(item.CurrentHolder, out var holderNameCo))
                                holderDisplay = $"{holderNameCo}\uff08{item.CurrentHolder}\uff09";
                            else
                                holderDisplay = item.CurrentHolder;
                            var idPart = string.IsNullOrWhiteSpace(item.Id) ? string.Empty : $"\uff08{item.Id}\uff09";
                            sb.AppendLine($"- {item.Name}{idPart}: {holderDisplay}");
                        }
                        sb.AppendLine();
                    }

                    sb.AppendLine("</hard_baseline>");
                }

                if ((changesOnlyScope & ConsistencyIssueRegistry.BaselineScope.Foreshadow) != 0 && factSnapshot.ForeshadowingStatus?.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("⚠ **伏笔当前状态（重写时必须严格遵守，不可违反）**：");
                    foreach (var fs in factSnapshot.ForeshadowingStatus)
                    {
                        var st = fs.IsResolved ? "已揭示（禁止再埋设/揭示）"
                               : fs.IsSetup ? "已埋设未揭示（可揭示，禁止再埋设）"
                                              : "未埋设（可埋设，禁止揭示）";
                        sb.AppendLine($"  - {fs.Name}（{fs.Id}）：{st}");
                    }
                }

                if ((changesOnlyScope & ConsistencyIssueRegistry.BaselineScope.Semantic) != 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("<hard_baseline mandatory=\"true\" scope=\"semantic_consistency\">");
                    sb.AppendLine("- **等级只升不降**：若确实发生等级回退，KeyEvent 中必须明确写出降级原因（失去/废除/代价等），否则请删除 NewLevel 或保持原等级。");
                    sb.AppendLine("- **关系申报不矛盾**：同一章内同一对角色，不能同时声明为盟友和仇敌。请统一关系结论，删除矛盾的一方。");
                    sb.AppendLine("- **单章信任值变化不超过 ±30**：请将 TrustDelta 绝对值限制在 30 以内；极端关系变化可分多章渐进。");
                    sb.AppendLine("- **能力失去必须说明原因**：LostAbilities 非空时，KeyEvent 中必须写明失去原因（封印/废除/代价等），否则请清空 LostAbilities。");
                    sb.AppendLine("</hard_baseline>");
                }

                var hasOmissions = failures.Any(f => !string.IsNullOrWhiteSpace(f)
                    && f.Contains("[漏报]", StringComparison.Ordinal));
                if (hasOmissions)
                {
                    sb.AppendLine();
                    sb.AppendLine("<hard_baseline mandatory=\"true\" scope=\"omitted_declaration\">");
                    sb.AppendLine("⚠ **本次失败核心问题：正文中出现的实体未在 CHANGES 中申报**");
                    sb.AppendLine("请保留正文不变，根据上方失败列表每项指示的维度与字段，在 CHANGES 对应数组中补全条目：");
                    sb.AppendLine("- 角色出现 → CharacterStateChanges 补条（CharacterId + KeyEvent；若无状态变化，仅填这两个字段）");
                    sb.AppendLine("- 势力出现 → FactionStateChanges 补条（FactionId + Event；NewStatus 可留空）");
                    sb.AppendLine("- 地点出现 → LocationStateChanges 补条（LocationId + LocationName + Event）");
                    sb.AppendLine("- 冲突推进 → ConflictProgress 补条（ConflictId + NewStatus 不可回退 + Event）");
                    sb.AppendLine("- 物品流转 → ItemTransfers 补条（ItemId + FromHolder + ToHolder + Event；FromHolder 必须等于账本当前持有者）");
                    sb.AppendLine("- 伏笔流转 → ForeshadowingActions 补条（ForeshadowId + Action=\"setup\" 或 \"payoff\"，根据正文上下文判断方向）");
                    sb.AppendLine("- 秘密揭示 → SecretRevealChanges 补条（SecretId + NewKnowerIds 列出新知情者）");
                    sb.AppendLine("- 承诺/誓言 → PledgeConstraintChanges 补条（PledgeId + Action=\"create/update/fulfill/break\"）");
                    sb.AppendLine("- 倒计时 → DeadlineConstraintChanges 补条（DeadlineId + Action=\"create/trigger/expire/cancel\"）");
                    sb.AppendLine("⚠ 若实体仅作背景一笔带过、无任何动作或状态变化，可做最小化申报（只填 Id 和 KeyEvent/Event 字段说明\"出现\"）。");
                    sb.AppendLine("</hard_baseline>");
                }

                if (factSnapshot != null)
                {
                    var templateJson = LayeredPromptBuilder.BuildPrefilledChangesJson(factSnapshot, contextIds);
                    if (!string.IsNullOrWhiteSpace(templateJson))
                    {
                        sb.AppendLine();
                        sb.AppendLine("<changes_template mandatory=\"true\" priority=\"critical\">");
                        if (isPreDetectMissingChanges)
                            sb.AppendLine($"请基于以下预填模板输出 CHANGES(填值+删除无变化条目，{ChapterChanges.TopLevelFieldCount}个顶级字段不可省略)：");
                        else
                            sb.AppendLine($"请参考以下正确结构修正你的 CHANGES 输出({ChapterChanges.TopLevelFieldCount}个顶级字段不可省略，ID 以模板或事实账本为准)：");
                        sb.AppendLine("⚠ 除新增物品/秘密/承诺/倒计时(Action=\"create\"且提供Name)外，模板中已有的所有 ...Id 字段值禁止改动、禁止自造，必须原样保留或从事实账本选取。");
                        sb.AppendLine($"⚠ 必须用成对的 {ChapterChanges.ChangesXmlOpen} 与 {ChapterChanges.ChangesXmlClose} 标签包裹模板输出(仅半角字符、不得省略)。");
                        sb.AppendLine(ChapterChanges.ChangesXmlOpen);
                        sb.AppendLine(templateJson);
                        sb.AppendLine(ChapterChanges.ChangesXmlClose);
                        sb.AppendLine("</changes_template>");
                    }
                }

                sb.AppendLine("</rewrite_feedback>");
                return sb.ToString();
            }

            List<string> cachedCharHints = null!, cachedConflHints = null!, cachedFsHints = null!,
                          cachedLocHints = null!, cachedFacHints = null!, cachedItemHints = null!;

            if (factSnapshot != null)
            {
                if (failures.Any(f => f.Contains("出发地点", StringComparison.OrdinalIgnoreCase)
                                      || f.Contains("原持有者", StringComparison.OrdinalIgnoreCase)
                                      || f.Contains("路径不连续", StringComparison.OrdinalIgnoreCase)))
                {
                    sb.AppendLine();
                    sb.AppendLine("<hard_baseline mandatory=\"true\" scope=\"changes_consistency\">");
                    sb.AppendLine("以下为账本基线（必须严格对齐）：");
                    sb.AppendLine("1) 若输出角色移动：角色ID可写名称或ShortId（系统自动解析）；出发地点可留空由系统从账本补值；若填写则必须等于该角色账本当前位置（同章多次移动时 FromLocation=上一次ToLocation）；");
                    sb.AppendLine("2) 若输出物品流转：物品ID/原持有者/新持有者可写名称或ShortId（系统自动解析）；原持有者可留空由系统从账本补值；同章多次转手时起点必须与上次终点一致。\n");

                    if (factSnapshot.CharacterLocations != null && factSnapshot.CharacterLocations.Count > 0)
                    {
                        sb.AppendLine("【角色当前位置】格式：角色名（角色ShortId）: 地点名（地点ShortId）");
                        var locDescMap2 = factSnapshot.LocationDescriptions ?? new System.Collections.Generic.Dictionary<string, LocationCoreDescription>();
                        var locIdToName2 = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var ls in factSnapshot.LocationStates ?? new System.Collections.Generic.List<LocationStateSnapshot>())
                            if (!string.IsNullOrWhiteSpace(ls.Id) && !string.IsNullOrWhiteSpace(ls.Name)) locIdToName2[ls.Id] = ls.Name;
                        foreach (var (lid2, ld2) in locDescMap2)
                            if (!string.IsNullOrWhiteSpace(lid2) && !string.IsNullOrWhiteSpace(ld2.Name)) locIdToName2[lid2] = ld2.Name;
                        foreach (var loc in factSnapshot.CharacterLocations)
                        {
                            var charName = string.IsNullOrWhiteSpace(loc.CharacterName) ? loc.CharacterId : loc.CharacterName;
                            if (string.IsNullOrWhiteSpace(charName) || string.IsNullOrWhiteSpace(loc.CurrentLocation)) continue;
                            string locDisplay;
                            if (TM.Framework.Common.Helpers.Id.ShortIdGenerator.IsLikelyId(loc.CurrentLocation)
                                && locIdToName2.TryGetValue(loc.CurrentLocation, out var lName2))
                                locDisplay = $"{lName2}\uff08{loc.CurrentLocation}\uff09";
                            else
                                locDisplay = loc.CurrentLocation;
                            sb.AppendLine($"- {charName}\uff08{loc.CharacterId}\uff09: {locDisplay}");
                        }
                        sb.AppendLine();
                    }

                    if (factSnapshot.ItemStates != null && factSnapshot.ItemStates.Count > 0)
                    {
                        sb.AppendLine("【物品持有者】格式：物品名（物品ShortId）: 持有者名（角色ShortId）");
                        var charDescMap3 = factSnapshot.CharacterDescriptions ?? new System.Collections.Generic.Dictionary<string, CharacterCoreDescription>();
                        var charIdToName3 = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var cs in factSnapshot.CharacterStates ?? new System.Collections.Generic.List<CharacterStateSnapshot>())
                            if (!string.IsNullOrWhiteSpace(cs.Id) && !string.IsNullOrWhiteSpace(cs.Name)) charIdToName3[cs.Id] = cs.Name;
                        foreach (var (cid3, cdesc3) in charDescMap3)
                            if (!string.IsNullOrWhiteSpace(cid3) && !string.IsNullOrWhiteSpace(cdesc3.Name)) charIdToName3[cid3] = cdesc3.Name;
                        foreach (var item in factSnapshot.ItemStates)
                        {
                            if (string.IsNullOrWhiteSpace(item.Name)) continue;
                            string holderDisplay;
                            if (string.IsNullOrWhiteSpace(item.CurrentHolder))
                                holderDisplay = "无人持有";
                            else if (TM.Framework.Common.Helpers.Id.ShortIdGenerator.IsLikelyId(item.CurrentHolder)
                                     && charIdToName3.TryGetValue(item.CurrentHolder, out var holderName3))
                                holderDisplay = $"{holderName3}\uff08{item.CurrentHolder}\uff09";
                            else
                                holderDisplay = item.CurrentHolder;
                            var idPart = string.IsNullOrWhiteSpace(item.Id) ? string.Empty : $"\uff08{item.Id}\uff09";
                            sb.AppendLine($"- {item.Name}{idPart}: {holderDisplay}");
                        }
                        sb.AppendLine();
                    }

                    sb.AppendLine("</hard_baseline>");
                }

                cachedCharHints = GetValidEntityHints(factSnapshot.CharacterStates?.Select(s => ((string?)s.Name, (string?)s.Id)).ToList());
                cachedConflHints = GetValidEntityHints(factSnapshot.ConflictProgress?.Select(c => ((string?)c.Name, (string?)c.Id)).ToList());
                cachedFsHints = GetValidEntityHints(factSnapshot.ForeshadowingStatus?.Select(f => ((string?)f.Name, (string?)f.Id)).ToList());
                cachedLocHints = GetValidEntityHints(factSnapshot.LocationStates?.Select(l => ((string?)l.Name, (string?)l.Id)).ToList());
                cachedFacHints = GetValidEntityHints(factSnapshot.FactionStates?.Select(f => ((string?)f.Name, (string?)f.Id)).ToList());
                cachedItemHints = GetValidEntityHints(factSnapshot.ItemStates?.Select(i => ((string?)i.Name, (string?)i.Id)).ToList());

                AppendValidEntityHint(sb, failures, "角色ID", cachedCharHints,
                    "角色状态变更的角色ID", "角色移动的角色ID", "不在本章涉及角色列表");
                AppendValidEntityHint(sb, failures, "关系对象角色ID", cachedCharHints,
                    "关系对象角色ID");
                AppendValidEntityHint(sb, failures, "冲突ID", cachedConflHints,
                    "冲突进度的冲突ID", "冲突状态");
                AppendValidEntityHint(sb, failures, "伏笔ID", cachedFsHints,
                    "伏笔动作的伏笔ID", "伏笔", "埋设", "揭示");
                bool hasForeshadowErr = failures.Any(f => f.Contains("伏笔", StringComparison.OrdinalIgnoreCase)
                                                       || f.Contains("埋设", StringComparison.OrdinalIgnoreCase));
                if (hasForeshadowErr && factSnapshot.ForeshadowingStatus?.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("⚠ **伏笔当前状态（重写时必须严格遵守，不可违反）**：");
                    foreach (var fs in factSnapshot.ForeshadowingStatus)
                    {
                        var st = fs.IsResolved ? "已揭示（禁止再埋设/揭示）"
                               : fs.IsSetup ? "已埋设未揭示（可揭示，禁止再埋设）"
                                              : "未埋设（可埋设，禁止揭示）";
                        sb.AppendLine($"  - {fs.Name}（{fs.Id}）：{st}");
                    }
                }
                bool hasLevelErr = failures.Any(f => f.Contains("等级", StringComparison.OrdinalIgnoreCase) && f.Contains("回退", StringComparison.OrdinalIgnoreCase));
                bool hasRelContra = failures.Any(f => f.Contains("关系申报矛盾", StringComparison.OrdinalIgnoreCase));
                bool hasTrustErr = failures.Any(f => f.Contains("信任值变化", StringComparison.OrdinalIgnoreCase));
                bool hasAbilityErr = failures.Any(f => f.Contains("失去能力", StringComparison.OrdinalIgnoreCase) && f.Contains("KeyEvent", StringComparison.OrdinalIgnoreCase));

                if (hasLevelErr || hasRelContra || hasTrustErr || hasAbilityErr)
                {
                    sb.AppendLine();
                    sb.AppendLine("<hard_baseline mandatory=\"true\" scope=\"semantic_consistency\">");
                    if (hasLevelErr)
                        sb.AppendLine("- **等级只升不降**：若确实发生等级回退，KeyEvent 中必须明确写出降级原因（失去/废除/代价等），否则请删除 NewLevel 或保持原等级。");
                    if (hasRelContra)
                        sb.AppendLine("- **关系申报不矛盾**：同一章内同一对角色，不能同时声明为盟友和仇敌。请统一关系结论，删除矛盾的一方。");
                    if (hasTrustErr)
                        sb.AppendLine("- **单章信任值变化不超过 ±30**：请将 TrustDelta 绝对值限制在 30 以内；极端关系变化可分多章渐进。");
                    if (hasAbilityErr)
                        sb.AppendLine("- **能力失去必须说明原因**：LostAbilities 非空时，KeyEvent 中必须写明失去原因（封印/废除/代价等），否则请清空 LostAbilities。");
                    sb.AppendLine("</hard_baseline>");
                }

                AppendValidEntityHint(sb, failures, "地点ID", cachedLocHints,
                    "地点状态变化的地点ID", "出发地点ID", "到达地点ID", "终点位置");
                AppendValidEntityHint(sb, failures, "势力ID", cachedFacHints,
                    "势力状态变化的势力ID", "势力");
                AppendValidEntityHint(sb, failures, "涉及角色ID列表", cachedCharHints,
                    "涉及角色ID列表");
                AppendValidEntityHint(sb, failures, "物品ID", cachedItemHints,
                    "物品流转的物品ID", "物品流转");
                AppendValidEntityHint(sb, failures, "物品持有者角色ID", cachedCharHints,
                    "原持有者ID", "新持有者ID", "持有者");

                bool charLedgerEmpty = factSnapshot.CharacterStates?.All(s => string.IsNullOrWhiteSpace(s.Id)) != false;
                bool conflictLedgerEmpty = factSnapshot.ConflictProgress?.All(c => string.IsNullOrWhiteSpace(c.Id)) != false;
                bool fsLedgerEmpty = factSnapshot.ForeshadowingStatus?.All(f => string.IsNullOrWhiteSpace(f.Id)) != false;
                bool locLedgerEmpty = factSnapshot.LocationStates?.All(l => string.IsNullOrWhiteSpace(l.Id)) != false;
                bool facLedgerEmpty = factSnapshot.FactionStates?.All(f => string.IsNullOrWhiteSpace(f.Id)) != false;
                bool itemLedgerEmpty = factSnapshot.ItemStates?.All(i => string.IsNullOrWhiteSpace(i.Id)) != false;
                bool secretLedgerEmpty = factSnapshot.SecretStates?.All(s => string.IsNullOrWhiteSpace(s.Id)) != false;
                bool pledgeLedgerEmpty = factSnapshot.PledgeStates?.All(p => string.IsNullOrWhiteSpace(p.Id)) != false;
                bool deadlineLedgerEmpty = factSnapshot.DeadlineStates?.All(d => string.IsNullOrWhiteSpace(d.Id)) != false;

                bool needCharEmpty = charLedgerEmpty && failures.Any(f => f.Contains("角色ID", StringComparison.OrdinalIgnoreCase)
                                                                             || f.Contains("涉及角色ID列表", StringComparison.OrdinalIgnoreCase)
                                                                             || f.Contains("角色移动", StringComparison.OrdinalIgnoreCase)
                                                                             || f.Contains("原持有者ID", StringComparison.OrdinalIgnoreCase)
                                                                             || f.Contains("新持有者ID", StringComparison.OrdinalIgnoreCase));
                bool needConflictEmpty = conflictLedgerEmpty && failures.Any(f => f.Contains("冲突ID", StringComparison.OrdinalIgnoreCase));
                bool needFsEmpty = fsLedgerEmpty && failures.Any(f => f.Contains("伏笔ID", StringComparison.OrdinalIgnoreCase));
                bool needLocEmpty = locLedgerEmpty && failures.Any(f => f.Contains("地点ID", StringComparison.OrdinalIgnoreCase)
                                                                             || f.Contains("出发地点ID", StringComparison.OrdinalIgnoreCase)
                                                                             || f.Contains("到达地点ID", StringComparison.OrdinalIgnoreCase));
                bool needFacEmpty = facLedgerEmpty && failures.Any(f => f.Contains("势力ID", StringComparison.OrdinalIgnoreCase));
                bool needItemEmpty = itemLedgerEmpty && failures.Any(f => f.Contains("物品ID", StringComparison.OrdinalIgnoreCase));
                bool needSecretEmpty = secretLedgerEmpty && failures.Any(f => f.Contains("秘密ID", StringComparison.OrdinalIgnoreCase));
                bool needPledgeEmpty = pledgeLedgerEmpty && failures.Any(f => f.Contains("承诺ID", StringComparison.OrdinalIgnoreCase)
                                                                             || f.Contains("PledgeId", StringComparison.OrdinalIgnoreCase));
                bool needDeadlineEmpty = deadlineLedgerEmpty && failures.Any(f => f.Contains("倒计时ID", StringComparison.OrdinalIgnoreCase)
                                                                             || f.Contains("DeadlineId", StringComparison.OrdinalIgnoreCase));

                if (needCharEmpty || needConflictEmpty || needFsEmpty || needLocEmpty || needFacEmpty || needItemEmpty || needSecretEmpty || needPledgeEmpty || needDeadlineEmpty)
                {
                    sb.AppendLine();
                    sb.AppendLine("<hard_baseline mandatory=\"true\" scope=\"empty_ledger_shortid\">");
                    sb.AppendLine("⚠ 以下实体类型在账本中无已追踪记录（无可用ShortId），对应CHANGES字段通常必须输出空数组；禁止自造/猜测ShortId。除物品、秘密、承诺、倒计时（需Action=\"create\"+Name）外，其它实体类型在账本为空时不得凭空创建：");
                    if (needCharEmpty)
                        sb.AppendLine("  角色账本为空 → 角色状态变化、角色移动、关系变化、涉及角色列表均必须留空；物品流转中的原持有者/新持有者也必须留空字符串");
                    if (needConflictEmpty)
                        sb.AppendLine("  冲突账本为空 → 冲突进度必须留空数组");
                    if (needFsEmpty)
                        sb.AppendLine("  伏笔账本为空 → 伏笔动作必须留空数组");
                    if (needLocEmpty)
                        sb.AppendLine("  地点账本为空 → 地点状态变化必须留空数组");
                    if (needFacEmpty)
                        sb.AppendLine("  势力账本为空 → 势力状态变化必须留空数组");
                    if (needItemEmpty)
                        sb.AppendLine("  物品账本为空 → 若本章确实引入新物品，请在 ItemTransfers 中提供 ItemName（或在 ItemId 填名称），系统会自动生成物品ShortId并建立追踪；若本章不涉及物品流转则留空数组。禁止自造/猜测ShortId");
                    if (needSecretEmpty)
                        sb.AppendLine("  秘密账本为空 → 若本章确实引入新秘密，请在 SecretRevealChanges 中提供 SecretName（或在 SecretId 填名称），系统会自动生成秘密ShortId并建立追踪；若本章不涉及秘密揭示则留空数组。禁止自造/猜测ShortId");
                    if (needPledgeEmpty)
                        sb.AppendLine("  承诺账本为空 → 若本章确实引入新承诺/契约，请在 PledgeConstraintChanges 中设置 Action=\"create\" 并提供 PledgeName，系统会自动生成ShortId并建立追踪；若本章不涉及承诺则留空数组。禁止自造/猜测ShortId");
                    if (needDeadlineEmpty)
                        sb.AppendLine("  倒计时账本为空 → 若本章确实引入新倒计时/时限，请在 DeadlineConstraintChanges 中设置 Action=\"create\" 并提供 DeadlineName，系统会自动生成ShortId并建立追踪；若本章不涉及倒计时则留空数组。禁止自造/猜测ShortId");
                    sb.AppendLine("</hard_baseline>");
                }

                var missingChars = failures
                    .Where(f => f.Contains("指定角色未在正文出现:") || f.Contains("剧情关键角色未在正文出现:"))
                    .Select(f => { var i = f.IndexOf(':'); return i >= 0 ? f.Substring(i + 1).Trim() : f; })
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();
                var missingFactions = failures
                    .Where(f => f.Contains("指定势力未在正文出现:"))
                    .Select(f => { var i = f.IndexOf(':'); return i >= 0 ? f.Substring(i + 1).Trim() : f; })
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();
                var missingLocs = failures
                    .Where(f => f.Contains("指定地点未在正文出现:"))
                    .Select(f => { var i = f.IndexOf(':'); return i >= 0 ? f.Substring(i + 1).Trim() : f; })
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();
                var missingBpEntities = failures
                    .Where(f => f.Contains("蓝图要求") || f.Contains("自然融入"))
                    .SelectMany(f =>
                    {
                        var s = f.IndexOf('【'); var e = f.IndexOf('】');
                        if (s < 0 || e <= s) return Enumerable.Empty<string>();
                        return f.Substring(s + 1, e - s - 1)
                                .Split('、', StringSplitOptions.RemoveEmptyEntries)
                                .Select(n => n.Trim()).Where(n => n.Length >= 2);
                    })
                    .Where(n => !missingChars.Contains(n, StringComparer.OrdinalIgnoreCase)
                             && !missingFactions.Contains(n, StringComparer.OrdinalIgnoreCase)
                             && !missingLocs.Contains(n, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (missingChars.Count > 0 || missingFactions.Count > 0 || missingLocs.Count > 0 || missingBpEntities.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("<hard_baseline mandatory=\"true\" scope=\"design_element_presence\">");
                    sb.AppendLine("以下实体是本章任务的必要组成部分，重写时**必须**在正文中安排有实质戏份（对话/动作/事件均可）：");
                    if (missingChars.Count > 0)
                        sb.AppendLine($"- 必须出场的角色：【{string.Join("、", missingChars)}】");
                    if (missingFactions.Count > 0)
                        sb.AppendLine($"- 必须提及的势力：【{string.Join("、", missingFactions)}】");
                    if (missingLocs.Count > 0)
                        sb.AppendLine($"- 必须出现的地点：【{string.Join("、", missingLocs)}】");
                    if (missingBpEntities.Count > 0)
                        sb.AppendLine($"- 必须出现的实体：【{string.Join("、", missingBpEntities)}】");
                    sb.AppendLine("请在重写时围绕本章主线情节，自然地安排上述实体出场，不可省略或跳过。");
                    sb.AppendLine("</hard_baseline>");
                }
            }

            bool hasBlueprintFailure = failures.Any(f => f.Contains("蓝图要求") || f.Contains("自然融入"));
            if (hasBlueprintFailure && factSnapshot != null)
            {
                var charHints = cachedCharHints;
                var conflHints = cachedConflHints;
                var fsHints = cachedFsHints;
                var locHints = cachedLocHints;
                var facHints = cachedFacHints;
                if (charHints.Count > 0 || conflHints.Count > 0 || fsHints.Count > 0 || locHints.Count > 0 || facHints.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("<shortid_reference mandatory=\"true\" reason=\"blueprint_rewrite\">");
                    sb.AppendLine("重写时 CHANGES 中所有 Id 字段**优先**使用以下括号内 ShortId；若不确定可填写名称，系统将自动解析；禁止自造/猜测不存在的标识符：");
                    if (charHints.Count > 0) sb.AppendLine($"角色: {string.Join("、", charHints)}");
                    if (conflHints.Count > 0) sb.AppendLine($"冲突: {string.Join("、", conflHints)}");
                    if (fsHints.Count > 0) sb.AppendLine($"伏笔: {string.Join("、", fsHints)}");
                    if (locHints.Count > 0) sb.AppendLine($"地点: {string.Join("、", locHints)}");
                    if (facHints.Count > 0) sb.AppendLine($"势力: {string.Join("、", facHints)}");
                    sb.AppendLine("</shortid_reference>");
                }
            }

            if (factSnapshot != null)
            {
                LayeredPromptBuilder.AppendChangesIdQuickRef(sb, factSnapshot);
            }

            sb.AppendLine();
            sb.AppendLine(LayeredPromptBuilder.GetRewriteOutputContractReminder());
            sb.AppendLine("</rewrite_feedback>");

            return sb.ToString();
        }

        #endregion
    }
}

