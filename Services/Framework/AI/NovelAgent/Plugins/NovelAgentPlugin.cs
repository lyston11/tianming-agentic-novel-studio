using System.ComponentModel;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Framework.AI.NovelAgent.Services;

namespace TM.Services.Framework.AI.NovelAgent.Plugins
{
    public sealed class NovelAgentPlugin
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private readonly NovelAgentOrchestrator _orchestrator;

        public NovelAgentPlugin(NovelAgentOrchestrator orchestrator)
        {
            _orchestrator = orchestrator;
        }

        [KernelFunction("GetStoryBible")]
        [Description("读取当前项目的 Story Bible、宏观创意候选、Canon Ledger 和最近 Agent Run。")]
        public async Task<string> GetStoryBibleAsync(CancellationToken ct)
        {
            var document = await _orchestrator.GetStoryBibleAsync(ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(document, JsonOptions);
        }

        [KernelFunction("ListAgentRuns")]
        [Description("列出最近的小说 Agent Run，用于恢复、取消或查看 Agent 历史任务。")]
        public async Task<string> ListAgentRunsAsync(
            CancellationToken ct,
            [Description("返回数量上限，默认20")] int take = 20)
        {
            var result = await _orchestrator.ListRunsAsync(take, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("ResumeAgentRun")]
        [Description("恢复一个未完成的小说 Agent Run，让它回到等待确认或规划状态。")]
        public async Task<string> ResumeAgentRunAsync(
            CancellationToken ct,
            [Description("Agent Run ID")] string runId)
        {
            var result = await _orchestrator.ResumeRunAsync(runId, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("ContinueAgentRun")]
        [Description("自动推进一个小说 Agent Run 中不需要人工确认的低/中风险步骤；遇到候选选择、正文生成、Canon 升级或改写覆盖时暂停等待用户确认。")]
        public async Task<string> ContinueAgentRunAsync(
            CancellationToken ct,
            [Description("Agent Run ID")] string runId,
            [Description("允许自动执行的最高风险等级：Low 或 Medium。默认 Medium，不会自动执行 High。")] string maxAutoRisk = "Medium")
        {
            var result = await _orchestrator.ContinueAgentRunAsync(runId, maxAutoRisk, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("CancelAgentRun")]
        [Description("取消一个未完成的小说 Agent Run，并将待执行步骤标记为跳过。")]
        public async Task<string> CancelAgentRunAsync(
            CancellationToken ct,
            [Description("Agent Run ID")] string runId,
            [Description("取消原因")] string reason = "")
        {
            var result = await _orchestrator.CancelRunAsync(runId, reason, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("PlanStoryFoundation")]
        [Description("为新书或重大重构生成故事创意宪法草案，包括题材风向、读者承诺、世界核心规则、主线冲突和禁止方向。")]
        public async Task<string> PlanStoryFoundationAsync(
            CancellationToken ct,
            [Description("用户的故事灵感或创作目标")] string userSeed,
            [Description("题材，例如玄幻、都市、悬疑、科幻")] string genre = "",
            [Description("子类型，例如爽文、烧脑、群像、克苏鲁修仙")] string subGenre = "",
            [Description("目标读者")] string targetReader = "",
            [Description("希望的风向，例如爽文、烧脑、深度、情绪、群像")] string desiredDirection = "")
        {
            var run = await _orchestrator.PlanStoryFoundationAsync(new StoryFoundationRequest
            {
                UserSeed = userSeed,
                Genre = genre,
                SubGenre = subGenre,
                TargetReader = targetReader,
                DesiredDirection = desiredDirection
            }, ct).ConfigureAwait(false);

            return JsonSerializer.Serialize(run, JsonOptions);
        }

        [KernelFunction("CommitStoryFoundation")]
        [Description("用户确认后，将指定 runId 的故事创意宪法提交为当前项目 Story Bible。已有 Story Bible 时默认不覆盖。")]
        public async Task<string> CommitStoryFoundationAsync(
            CancellationToken ct,
            [Description("PlanStoryFoundation 返回的 runId")] string runId,
            [Description("是否允许覆盖已有 Story Bible")] bool overwrite = false,
            [Description("用户是否已经明确确认本次高风险提交")] bool confirmed = false,
            [Description("用户选择的候选序号，1-based，优先于标题")] int selectedMacroCandidateIndex = 0,
            [Description("用户选择的候选稳定 ID，优先级最高")] string selectedMacroCandidateId = "",
            [Description("兼容旧调用的候选标题，仅用于显示或旧客户端")] string selectedMacroCandidateTitle = "")
        {
            var result = await _orchestrator.CommitStoryFoundationAsync(
                runId,
                overwrite,
                confirmed,
                selectedMacroCandidateTitle,
                selectedMacroCandidateId,
                selectedMacroCandidateIndex,
                ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("PlanVolumeArc")]
        [Description("为一卷生成卷级大框架草案，包括卷目标、阶段反转、高潮、伏笔投放回收、角色弧和世界观增量。")]
        public async Task<string> PlanVolumeArcAsync(
            CancellationToken ct,
            [Description("本卷创作目标或用户需求")] string userGoal,
            [Description("卷ID，例如 vol1")] string volumeId = "vol1",
            [Description("卷标题")] string volumeTitle = "",
            [Description("起始章节ID，例如 vol1_ch1")] string startChapterId = "",
            [Description("结束章节ID，例如 vol1_ch12")] string endChapterId = "",
            [Description("预计章节数")] int expectedChapterCount = 12)
        {
            var run = await _orchestrator.PlanVolumeArcAsync(new VolumeArcPlanningRequest
            {
                UserGoal = userGoal,
                VolumeId = volumeId,
                VolumeTitle = volumeTitle,
                StartChapterId = startChapterId,
                EndChapterId = endChapterId,
                ExpectedChapterCount = expectedChapterCount
            }, ct).ConfigureAwait(false);

            return JsonSerializer.Serialize(run, JsonOptions);
        }

        [KernelFunction("CommitVolumeArc")]
        [Description("用户确认后，将指定 runId 的卷级大框架提交到 Story Bible，作为后续章节规划的上层依据。")]
        public async Task<string> CommitVolumeArcAsync(
            CancellationToken ct,
            [Description("PlanVolumeArc 返回的 runId")] string runId,
            [Description("是否允许覆盖同一 volumeId 的已有卷规划")] bool overwrite = false,
            [Description("用户是否已经明确确认本次高风险提交")] bool confirmed = false)
        {
            var result = await _orchestrator.CommitVolumeArcAsync(runId, overwrite, confirmed, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("GetChapterStoryState")]
        [Description("读取指定章节的故事状态快照，包括章节目标、历史摘要、事实快照、活跃冲突、未回收伏笔、世界规则和已用桥段。")]
        public async Task<string> GetChapterStoryStateAsync(
            CancellationToken ct,
            [Description("章节ID，如 vol1_ch3")] string chapterId)
        {
            var snapshot = await _orchestrator.GetChapterStoryStateAsync(chapterId, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(snapshot, JsonOptions);
        }

        [KernelFunction("PlanChapterCreativeBrief")]
        [Description("为指定章节生成创意简报，先做剧情候选、反套路和代价设计，不直接写正文。")]
        public async Task<string> PlanChapterCreativeBriefAsync(
            CancellationToken ct,
            [Description("章节ID，如 vol1_ch3")] string chapterId,
            [Description("本章目标或用户需求")] string userGoal)
        {
            var run = await _orchestrator.PlanChapterAsync(new ChapterCreativeRequest
            {
                ChapterId = chapterId,
                UserGoal = userGoal
            }, ct).ConfigureAwait(false);

            return JsonSerializer.Serialize(run, JsonOptions);
        }

        [KernelFunction("SelectChapterCandidate")]
        [Description("用户确认章节候选后，将推荐候选、指定候选或多个混合候选写回章节创意简报。未确认时只返回确认请求。")]
        public async Task<string> SelectChapterCandidateAsync(
            CancellationToken ct,
            [Description("PlanChapterCreativeBrief 返回的 runId")] string runId,
            [Description("候选标题。多个标题可用逗号、分号或竖线分隔；留空则选择 Agent 推荐候选")] string candidateTitles = "",
            [Description("选择模式：Recommended、Single、Mixed 或自定义说明")] string selectionMode = "Recommended",
            [Description("用户选择或混合候选的理由")] string selectionRationale = "",
            [Description("用户是否已经明确确认本次候选选择")] bool confirmed = false)
        {
            var result = await _orchestrator.SelectChapterCandidateAsync(
                runId,
                candidateTitles,
                selectionMode,
                selectionRationale,
                confirmed,
                ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("ExecuteChapterFromBrief")]
        [Description("用户确认章节创意简报后，按指定 Agent Run 调用 Writer.GenerateChapter 生成并保存章节正文。未确认时只返回确认请求。")]
        public async Task<string> ExecuteChapterFromBriefAsync(
            CancellationToken ct,
            [Description("PlanChapterCreativeBrief 返回的 runId")] string runId,
            [Description("用户是否已经明确确认本章创意简报并允许落盘生成")] bool confirmed = false)
        {
            var result = await _orchestrator.ExecuteChapterFromBriefAsync(runId, confirmed, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("ReviewGeneratedChapter")]
        [Description("对指定 Agent Run 的已生成章节重跑生成后复盘，输出质量检查、设定风险、故事变量变化和下一章建议。")]
        public async Task<string> ReviewGeneratedChapterAsync(
            CancellationToken ct,
            [Description("Agent Run ID")] string runId)
        {
            var result = await _orchestrator.ReviewGeneratedChapterAsync(runId, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("RewriteChapterFromReview")]
        [Description("用户确认后，根据生成后复盘报告执行一次质量改写闭环：生成修复版、严格保存、重跑复盘。未确认时只返回确认请求。")]
        public async Task<string> RewriteChapterFromReviewAsync(
            CancellationToken ct,
            [Description("Agent Run ID")] string runId,
            [Description("用户是否已经明确确认允许覆盖保存当前章节")] bool confirmed = false)
        {
            var result = await _orchestrator.RewriteChapterFromReviewAsync(runId, confirmed, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("ImportProposedCanonFromReview")]
        [Description("从指定 Agent Run 的生成后复盘报告中导入 Proposed 设定，并进行基础 Canon 冲突检查。")]
        public async Task<string> ImportProposedCanonFromReviewAsync(
            CancellationToken ct,
            [Description("Agent Run ID")] string runId)
        {
            var result = await _orchestrator.ImportProposedCanonFromReviewAsync(runId, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("PromoteProposedCanonFromRun")]
        [Description("用户确认后，将指定 Agent Run 相关的 Proposed 设定升级为 Canon。未确认时只返回确认请求。")]
        public async Task<string> PromoteProposedCanonFromRunAsync(
            CancellationToken ct,
            [Description("Agent Run ID")] string runId,
            [Description("可选：要升级的 Canon Ledger 条目 ID，多个用逗号/分号/竖线分隔；留空则升级该 Run 相关 Proposed")] string entryIds = "",
            [Description("用户是否已经明确确认本次 Canon 升级")] bool confirmed = false)
        {
            var result = await _orchestrator.PromoteProposedCanonFromRunAsync(
                runId,
                entryIds,
                confirmed,
                ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("RejectProposedCanonFromRun")]
        [Description("用户确认后，将指定 Agent Run 相关的 Proposed 设定标记为 Rejected，避免后续章节把它作为正文依据。未确认时只返回确认请求。")]
        public async Task<string> RejectProposedCanonFromRunAsync(
            CancellationToken ct,
            [Description("Agent Run ID")] string runId,
            [Description("可选：要拒绝的 Canon Ledger 条目 ID，多个用逗号/分号/竖线分隔；留空则拒绝该 Run 相关 Proposed")] string entryIds = "",
            [Description("拒绝原因，会写入 conflictCheck 作为审计记录")] string reason = "",
            [Description("用户是否已经明确确认本次 Proposed 拒绝")] bool confirmed = false)
        {
            var result = await _orchestrator.RejectProposedCanonFromRunAsync(
                runId,
                entryIds,
                reason,
                confirmed,
                ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("ImportForeshadowFromReview")]
        [Description("从指定 Agent Run 的生成后复盘报告中导入伏笔投放、强化或候选回收动作，写入 Story Bible 伏笔账本。")]
        public async Task<string> ImportForeshadowFromReviewAsync(
            CancellationToken ct,
            [Description("Agent Run ID")] string runId)
        {
            var result = await _orchestrator.ImportForeshadowFromReviewAsync(runId, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("ConfirmForeshadowStatusFromRun")]
        [Description("用户确认后，将指定 Agent Run 中高风险伏笔回收、废弃或冲突状态写入伏笔账本。未确认时只返回确认请求。")]
        public async Task<string> ConfirmForeshadowStatusFromRunAsync(
            CancellationToken ct,
            [Description("Agent Run ID")] string runId,
            [Description("可选：要确认的伏笔账本条目 ID，多个用逗号/分号/竖线分隔；留空则确认该 Run 相关高风险伏笔变化")] string entryIds = "",
            [Description("用户是否已经明确确认本次伏笔状态变化")] bool confirmed = false)
        {
            var result = await _orchestrator.ConfirmForeshadowStatusFromRunAsync(
                runId,
                entryIds,
                confirmed,
                ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("ImportCharacterStateFromReview")]
        [Description("从指定 Agent Run 的生成后复盘报告中导入角色目标、秘密、关系、能力代价和心理变化，写入 Story Bible 角色状态账本。")]
        public async Task<string> ImportCharacterStateFromReviewAsync(
            CancellationToken ct,
            [Description("Agent Run ID")] string runId)
        {
            var result = await _orchestrator.ImportCharacterStateFromReviewAsync(runId, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("ConfirmCharacterStateFromRun")]
        [Description("用户确认后，将指定 Agent Run 中高风险角色死亡、身份改写、秘密揭露、关系反转或能力规则变化写入角色状态账本。未确认时只返回确认请求。")]
        public async Task<string> ConfirmCharacterStateFromRunAsync(
            CancellationToken ct,
            [Description("Agent Run ID")] string runId,
            [Description("可选：要确认的角色账本条目 ID，多个用逗号/分号/竖线分隔；留空则确认该 Run 相关高风险角色变化")] string entryIds = "",
            [Description("用户是否已经明确确认本次角色状态变化")] bool confirmed = false)
        {
            var result = await _orchestrator.ConfirmCharacterStateFromRunAsync(
                runId,
                entryIds,
                confirmed,
                ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("RetrieveCreativeKnowledge")]
        [Description("检索小说创意知识库，返回类型原则、套路风险、反套路策略和项目记忆。")]
        public async Task<string> RetrieveCreativeKnowledgeAsync(
            CancellationToken ct,
            [Description("检索问题，例如 玄幻爽文第三章如何避免重复打脸")] string query)
        {
            var result = await _orchestrator.RetrieveCreativeKnowledgeAsync(query, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("AddCreativeKnowledgeEntry")]
        [Description("向小说创意知识库追加一条知识，可用于补充类型知识、套路风险、反套路策略、主题深度、情绪线或关系变化策略。")]
        public async Task<string> AddCreativeKnowledgeEntryAsync(
            CancellationToken ct,
            [Description("分类：GenrePrinciple, TropePattern, AntiTropeStrategy, ProjectUsedPattern, ReaderPromise, ThemeDepth, EmotionArc, RelationshipDynamic")] string category,
            [Description("标题")] string title,
            [Description("内容")] string content,
            [Description("题材，如 玄幻、都市、悬疑，可留空")] string genre = "",
            [Description("子类型，如 爽文、烧脑、群像，可留空")] string subGenre = "",
            [Description("标签，多个用逗号/分号/竖线分隔")] string tags = "",
            [Description("权重 1-10，越高越优先命中")] int weight = 5)
        {
            var entry = new CreativeKnowledgeEntry
            {
                Category = ParseEnum(category, CreativeKnowledgeCategory.GenrePrinciple),
                Genre = genre,
                SubGenre = subGenre,
                Title = title,
                Content = content,
                Tags = SplitList(tags),
                Weight = Math.Clamp(weight, 1, 10),
                Source = "User"
            };

            var result = await _orchestrator.AddCreativeKnowledgeEntryAsync(entry, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("RecordUsedPlotPattern")]
        [Description("记录项目已用桥段模式，后续章节规划会将其作为重复风险和反套路依据。")]
        public async Task<string> RecordUsedPlotPatternAsync(
            CancellationToken ct,
            [Description("章节ID，如 vol1_ch3")] string chapterId,
            [Description("已用桥段模式，例如 嘲讽-证明-全场震惊")] string pattern,
            [Description("补充说明，例如 本章由宗门大比触发")] string note = "")
        {
            var result = await _orchestrator.RecordUsedPlotPatternAsync(chapterId, pattern, note, ct)
                .ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("AddCanonLedgerEntry")]
        [Description("追加 Story Bible 设定账本条目。写作中新增世界观应先以 Proposed 写入，确认后再升级为 Canon。")]
        public async Task<string> AddCanonLedgerEntryAsync(
            CancellationToken ct,
            [Description("条目标题")] string title,
            [Description("条目内容")] string content,
            [Description("类型：WorldRule, CharacterRule, FactionRule, LocationRule, PlotRule, Foreshadowing, Theme, Constraint")] string type = "WorldRule",
            [Description("状态：Draft, Proposed, Canon, Deprecated, Conflict, Rejected")] string status = "Proposed",
            [Description("为什么需要这个设定")] string rationale = "",
            [Description("影响范围，如 全书、第一卷、vol1_ch3")] string impactScope = "",
            [Description("冲突检查说明")] string conflictCheck = "",
            [Description("来源章节ID")] string sourceChapterId = "",
            [Description("来源 Agent Run ID")] string sourceRunId = "",
            [Description("用户是否已经明确确认本次高风险设定写入")] bool confirmed = false)
        {
            var entry = new CanonLedgerEntry
            {
                Type = ParseEnum(type, CanonLedgerEntryType.WorldRule),
                Status = ParseEnum(status, CanonLedgerEntryStatus.Proposed),
                Title = title,
                Content = content,
                Rationale = rationale,
                ImpactScope = impactScope,
                ConflictCheck = conflictCheck,
                SourceChapterId = sourceChapterId,
                SourceRunId = sourceRunId
            };

            var result = await _orchestrator.AddLedgerEntryAsync(entry, confirmed, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("UpdateCanonLedgerEntryStatus")]
        [Description("更新 Story Bible 设定账本条目状态，用于将 Proposed 设定确认进入 Canon，或标记为 Conflict/Deprecated。")]
        public async Task<string> UpdateCanonLedgerEntryStatusAsync(
            CancellationToken ct,
            [Description("设定账本条目 ID")] string entryId,
            [Description("新状态：Draft, Proposed, Canon, Deprecated, Conflict, Rejected")] string status,
            [Description("冲突检查或确认说明")] string conflictCheck = "",
            [Description("用户是否已经明确确认本次高风险状态更新")] bool confirmed = false)
        {
            var result = await _orchestrator.UpdateLedgerEntryStatusAsync(
                entryId,
                ParseEnum(status, CanonLedgerEntryStatus.Proposed),
                conflictCheck,
                confirmed,
                ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("AddForeshadowLedgerEntry")]
        [Description("追加 Story Bible 伏笔账本条目。新伏笔可先以 Proposed 或 Planned 写入，正式回收/废弃需要用户确认。")]
        public async Task<string> AddForeshadowLedgerEntryAsync(
            CancellationToken ct,
            [Description("伏笔名称")] string name,
            [Description("投放方式或线索内容")] string setup,
            [Description("计划回收方式")] string payoff = "",
            [Description("类型：Plot, WorldRule, CharacterSecret, Relationship, Object, Threat, Theme, Other")] string type = "Plot",
            [Description("状态：Draft, Proposed, Planned, Setup, Reinforced, Due, PaidOff, Abandoned, Conflict")] string status = "Proposed",
            [Description("所属卷ID，如 vol1")] string sourceVolumeId = "",
            [Description("计划投放章节ID")] string plannedSetupChapterId = "",
            [Description("计划回收章节ID")] string plannedPayoffChapterId = "",
            [Description("重要度 1-10")] int importance = 5,
            [Description("来源章节ID")] string sourceChapterId = "",
            [Description("来源 Agent Run ID")] string sourceRunId = "",
            [Description("用户是否已经明确确认本次高风险伏笔写入")] bool confirmed = false)
        {
            var entry = new ForeshadowLedgerEntry
            {
                Name = name,
                Setup = setup,
                Payoff = payoff,
                Type = ParseEnum(type, ForeshadowLedgerEntryType.Plot),
                Status = ParseEnum(status, ForeshadowLedgerStatus.Proposed),
                SourceVolumeId = sourceVolumeId,
                PlannedSetupChapterId = plannedSetupChapterId,
                PlannedPayoffChapterId = plannedPayoffChapterId,
                Importance = Math.Clamp(importance, 1, 10),
                SourceChapterId = sourceChapterId,
                SourceRunId = sourceRunId
            };

            var result = await _orchestrator.AddForeshadowEntryAsync(entry, confirmed, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("UpdateForeshadowLedgerStatus")]
        [Description("更新 Story Bible 伏笔账本条目状态，用于标记投放、强化、临近回收、正式回收、废弃或冲突。")]
        public async Task<string> UpdateForeshadowLedgerStatusAsync(
            CancellationToken ct,
            [Description("伏笔账本条目 ID")] string entryId,
            [Description("新状态：Draft, Proposed, Planned, Setup, Reinforced, Due, PaidOff, Abandoned, Conflict")] string status,
            [Description("发生该状态变化的章节ID")] string chapterId = "",
            [Description("状态变化说明或证据")] string note = "",
            [Description("用户是否已经明确确认本次高风险状态更新")] bool confirmed = false)
        {
            var result = await _orchestrator.UpdateForeshadowEntryStatusAsync(
                entryId,
                ParseEnum(status, ForeshadowLedgerStatus.Proposed),
                chapterId,
                note,
                confirmed,
                ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("AddCharacterLedgerEntry")]
        [Description("追加 Story Bible 角色状态账本条目。用于维护角色目标、秘密、关系、能力代价、心理变化和信念变化；高风险状态需要用户确认。")]
        public async Task<string> AddCharacterLedgerEntryAsync(
            CancellationToken ct,
            [Description("角色名")] string characterName,
            [Description("条目摘要")] string summary,
            [Description("类型：Goal, Secret, Relationship, AbilityCost, Psychology, Belief, Identity, Role, Death, Other")] string type = "Goal",
            [Description("状态：Draft, Proposed, Active, GoalUpdated, SecretSeeded, SecretRevealed, RelationshipChanged, RelationshipReversed, AbilityChanged, AbilityRuleChanged, PsychologicalShifted, BeliefShifted, IdentityRewritten, LeftStage, Dead, Conflict, Rejected")] string status = "Proposed",
            [Description("角色定位，如 主角、盟友、反派、导师")] string role = "",
            [Description("当前目标")] string currentGoal = "",
            [Description("当前意图或本章选择")] string currentIntent = "",
            [Description("下一步压力")] string nextPressure = "",
            [Description("秘密内容，可留空")] string secret = "",
            [Description("关系对象，可留空")] string targetCharacter = "",
            [Description("关系状态：Unknown, Ally, Rival, Enemy, Mentor, Family, Lover, Betrayed, Broken, Ambiguous")] string relationshipStatus = "Unknown",
            [Description("能力或资源状态，可留空")] string ability = "",
            [Description("能力代价、债务或限制，可留空")] string abilityCost = "",
            [Description("心理情绪，可留空")] string emotion = "",
            [Description("心理压力 1-10")] int stressLevel = 5,
            [Description("信念变化，可留空")] string beliefShift = "",
            [Description("身份状态，可留空")] string identityState = "",
            [Description("重要度 1-10")] int importance = 5,
            [Description("来源章节ID")] string sourceChapterId = "",
            [Description("来源 Agent Run ID")] string sourceRunId = "",
            [Description("用户是否已经明确确认本次高风险角色状态写入")] bool confirmed = false)
        {
            var entry = new CharacterLedgerEntry
            {
                CharacterName = characterName,
                Role = role,
                Type = ParseEnum(type, CharacterLedgerEntryType.Goal),
                Status = ParseEnum(status, CharacterLedgerStatus.Proposed),
                Summary = summary,
                CurrentGoal = currentGoal,
                CurrentIntent = currentIntent,
                NextPressure = nextPressure,
                Secret = new CharacterSecretState
                {
                    Content = secret,
                    Status = string.IsNullOrWhiteSpace(secret) ? CharacterSecretStatus.None : CharacterSecretStatus.Seeded
                },
                Relationship = new CharacterRelationshipState
                {
                    TargetCharacter = targetCharacter,
                    Status = ParseEnum(relationshipStatus, CharacterRelationshipStatus.Unknown)
                },
                AbilityCost = new CharacterAbilityCostState
                {
                    Ability = ability,
                    Cost = abilityCost
                },
                Psychology = new CharacterPsychologyState
                {
                    Emotion = emotion,
                    StressLevel = Math.Clamp(stressLevel, 1, 10)
                },
                BeliefShift = beliefShift,
                IdentityState = identityState,
                Importance = Math.Clamp(importance, 1, 10),
                SourceChapterId = sourceChapterId,
                SourceRunId = sourceRunId
            };

            var result = await _orchestrator.AddCharacterEntryAsync(entry, confirmed, ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [KernelFunction("UpdateCharacterLedgerStatus")]
        [Description("更新 Story Bible 角色状态账本条目状态，用于标记目标变化、秘密揭露、关系变化、能力变化、心理变化、身份改写、退场或死亡。")]
        public async Task<string> UpdateCharacterLedgerStatusAsync(
            CancellationToken ct,
            [Description("角色状态账本条目 ID")] string entryId,
            [Description("新状态：Draft, Proposed, Active, GoalUpdated, SecretSeeded, SecretRevealed, RelationshipChanged, RelationshipReversed, AbilityChanged, AbilityRuleChanged, PsychologicalShifted, BeliefShifted, IdentityRewritten, LeftStage, Dead, Conflict, Rejected")] string status,
            [Description("发生该状态变化的章节ID")] string chapterId = "",
            [Description("状态变化说明或证据")] string note = "",
            [Description("用户是否已经明确确认本次高风险状态更新")] bool confirmed = false)
        {
            var result = await _orchestrator.UpdateCharacterEntryStatusAsync(
                entryId,
                ParseEnum(status, CharacterLedgerStatus.Proposed),
                chapterId,
                note,
                confirmed,
                ct).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, JsonOptions);
        }

        private static TEnum ParseEnum<TEnum>(string value, TEnum fallback)
            where TEnum : struct
        {
            return Enum.TryParse<TEnum>(value?.Trim(), ignoreCase: true, out var parsed)
                ? parsed
                : fallback;
        }

        private static System.Collections.Generic.List<string> SplitList(string values)
        {
            if (string.IsNullOrWhiteSpace(values))
                return new System.Collections.Generic.List<string>();

            return new System.Collections.Generic.List<string>(
                values.Split(new[] { ',', '，', ';', '；', '|', '\n', '\r' },
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase));
        }
    }
}
