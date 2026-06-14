using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Services.Framework.AI.NovelAgent.Services
{
    public interface IStoryBibleDocumentStore
    {
        Task<StoryBibleDocument?> LoadAsync(CancellationToken ct = default);

        Task SaveAsync(StoryBibleDocument document, CancellationToken ct = default);
    }

    public sealed class StoryBibleService
    {
        private const string StorageSubPath = "Framework/AI/NovelAgent";
        private const string StoryBibleFileName = "story_bible.json";
        private const int MaxAgentRuns = 100;
        private const int MaxRevisions = 200;

        private readonly SemaphoreSlim _ioLock = new(1, 1);
        private readonly IStoryBibleDocumentStore? _documentStore;
        private readonly string _storageIdentity;
        private StoryBibleDocument? _cache;

        public StoryBibleService(
            IStoryBibleDocumentStore? documentStore = null,
            string storageIdentity = "sqlite-redis://story-bible")
        {
            _documentStore = documentStore;
            _storageIdentity = string.IsNullOrWhiteSpace(storageIdentity)
                ? "sqlite-redis://story-bible"
                : storageIdentity;

            try
            {
                StoragePathHelper.CurrentProjectChanged += (_, _) =>
                {
                    _cache = null;
                };
            }
            catch (Exception ex)
            {
                TM.App.Log($"[StoryBibleService] 订阅项目切换事件失败: {ex.Message}");
            }
        }

        public string GetStoragePath()
        {
            return _storageIdentity;
        }

        public async Task<StoryBibleDocument> LoadAsync(CancellationToken ct = default)
        {
            if (_cache != null) return Clone(_cache);

            await _ioLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_cache != null) return Clone(_cache);

                _cache = _documentStore == null
                    ? new StoryBibleDocument()
                    : await _documentStore.LoadAsync(ct).ConfigureAwait(false) ?? new StoryBibleDocument();
                Normalize(_cache);
                return Clone(_cache);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[StoryBibleService] 加载 Story Bible 失败: {ex.Message}");
                _cache = new StoryBibleDocument();
                return Clone(_cache);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task SaveRunAsync(NovelAgentRun run, CancellationToken ct = default)
        {
            if (run == null) return;

            await _ioLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var document = await LoadWithoutLockAsync(ct).ConfigureAwait(false);
                document.AgentRuns.RemoveAll(r => string.Equals(r.RunId, run.RunId, StringComparison.OrdinalIgnoreCase));
                document.AgentRuns.Insert(0, run);
                if (document.AgentRuns.Count > MaxAgentRuns)
                    document.AgentRuns.RemoveRange(MaxAgentRuns, document.AgentRuns.Count - MaxAgentRuns);
                Touch(document);
                await SaveWithoutLockAsync(document, ct).ConfigureAwait(false);
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<NovelAgentRunOperationResult> ListRunsAsync(
            int take = 20,
            CancellationToken ct = default)
        {
            var document = await LoadAsync(ct).ConfigureAwait(false);
            var runs = document.AgentRuns
                .OrderByDescending(r => r.UpdatedAt)
                .Take(Math.Max(1, take))
                .ToList();

            return new NovelAgentRunOperationResult
            {
                Success = true,
                Message = $"已返回最近 {runs.Count} 个 Agent Run。",
                Runs = runs
            };
        }

        public async Task<NovelAgentRunOperationResult> ResumeRunAsync(
            string runId,
            CancellationToken ct = default)
        {
            await _ioLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var document = await LoadWithoutLockAsync(ct).ConfigureAwait(false);
                var run = FindRun(document, runId);
                if (run == null)
                {
                    return new NovelAgentRunOperationResult
                    {
                        Success = false,
                        Message = "未找到指定 Agent Run。"
                    };
                }

                if (run.Status is NovelAgentRunStatus.Completed or NovelAgentRunStatus.Cancelled)
                {
                    return new NovelAgentRunOperationResult
                    {
                        Success = true,
                        Message = $"Agent Run 已是 {run.Status} 状态，无需恢复执行。",
                        Run = Clone(run)
                    };
                }

                run.Status = run.Steps.Any(s => s.Status == NovelAgentStepStatus.WaitingUser)
                    ? NovelAgentRunStatus.AwaitingConfirmation
                    : NovelAgentRunStatus.Planning;
                run.Notes.Add("Agent Run 已恢复，等待继续执行或用户确认。");
                Touch(run);
                Touch(document);
                AddRevision(document, "ResumeAgentRun", $"恢复 Agent Run：{run.Intent} / {run.RunId}", run.RunId);
                await SaveWithoutLockAsync(document, ct).ConfigureAwait(false);

                return new NovelAgentRunOperationResult
                {
                    Success = true,
                    Message = "Agent Run 已恢复。",
                    Run = Clone(run)
                };
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<NovelAgentRunOperationResult> CancelRunAsync(
            string runId,
            string reason = "",
            CancellationToken ct = default)
        {
            await _ioLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var document = await LoadWithoutLockAsync(ct).ConfigureAwait(false);
                var run = FindRun(document, runId);
                if (run == null)
                {
                    return new NovelAgentRunOperationResult
                    {
                        Success = false,
                        Message = "未找到指定 Agent Run。"
                    };
                }

                if (run.Status == NovelAgentRunStatus.Completed)
                {
                    return new NovelAgentRunOperationResult
                    {
                        Success = false,
                        Message = "已完成的 Agent Run 不应取消。",
                        Run = Clone(run)
                    };
                }

                run.Status = NovelAgentRunStatus.Cancelled;
                foreach (var step in run.Steps.Where(s =>
                             s.Status is NovelAgentStepStatus.Pending or NovelAgentStepStatus.Running or NovelAgentStepStatus.WaitingUser))
                {
                    step.Status = NovelAgentStepStatus.Skipped;
                }

                run.Notes.Add(string.IsNullOrWhiteSpace(reason)
                    ? "Agent Run 已取消。"
                    : $"Agent Run 已取消：{reason.Trim()}");
                Touch(run);
                Touch(document);
                AddRevision(document, "CancelAgentRun", $"取消 Agent Run：{run.Intent} / {run.RunId}", run.RunId);
                await SaveWithoutLockAsync(document, ct).ConfigureAwait(false);

                return new NovelAgentRunOperationResult
                {
                    Success = true,
                    Message = "Agent Run 已取消。",
                    Run = Clone(run)
                };
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<StoryBibleCommitResult> CommitConstitutionAsync(
            StoryCreativeConstitution constitution,
            IReadOnlyList<MacroStoryConceptCandidate>? macroCandidates,
            string sourceRunId = "",
            bool overwrite = false,
            bool confirmed = false,
            CancellationToken ct = default)
        {
            if (constitution == null)
            {
                return new StoryBibleCommitResult
                {
                    Success = false,
                    Message = "故事创意宪法为空，无法提交。",
                    StoragePath = GetStoragePath()
                };
            }

            await _ioLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var document = await LoadWithoutLockAsync(ct).ConfigureAwait(false);
                if (document.Constitution != null && !overwrite)
                {
                    return new StoryBibleCommitResult
                    {
                        Success = false,
                        RequiresOverwrite = true,
                        Message = "当前项目已经存在 Story Bible。若要覆盖，请明确传入 overwrite=true。",
                        StoragePath = GetStoragePath(),
                        Document = Clone(document)
                    };
                }

                document.Constitution = constitution;
                document.MacroCandidates = macroCandidates?.ToList() ?? new List<MacroStoryConceptCandidate>();
                Touch(document);
                EnsureConstitutionLedger(document, constitution, sourceRunId);
                AddRevision(
                    document,
                    "CommitConstitution",
                    $"提交 Story Bible：{FirstNonEmpty(constitution.ReaderPromise, constitution.CoreHook, constitution.Genre)}",
                    sourceRunId);
                await SaveWithoutLockAsync(document, ct).ConfigureAwait(false);

                return new StoryBibleCommitResult
                {
                    Success = true,
                    Message = "Story Bible 已提交保存。",
                    StoragePath = GetStoragePath(),
                    Document = Clone(document)
                };
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<StoryBibleCommitResult> AddLedgerEntryAsync(
            CanonLedgerEntry entry,
            bool confirmed = false,
            CancellationToken ct = default)
        {
            if (entry == null)
            {
                return new StoryBibleCommitResult
                {
                    Success = false,
                    Message = "设定账本条目为空，无法追加。",
                    StoragePath = GetStoragePath()
                };
            }

            await _ioLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var document = await LoadWithoutLockAsync(ct).ConfigureAwait(false);
                NormalizeEntry(entry);
                document.CanonLedger.RemoveAll(e => string.Equals(e.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
                document.CanonLedger.Insert(0, entry);
                Touch(document);
                AddRevision(
                    document,
                    "AddLedgerEntry",
                    $"新增设定账本条目：{entry.Title} [{entry.Status}]",
                    entry.SourceRunId,
                    entry.SourceChapterId);
                await SaveWithoutLockAsync(document, ct).ConfigureAwait(false);

                return new StoryBibleCommitResult
                {
                    Success = true,
                    Message = "设定账本条目已追加。",
                    StoragePath = GetStoragePath(),
                    Document = Clone(document)
                };
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<StoryBibleCommitResult> UpdateLedgerEntryStatusAsync(
            string entryId,
            CanonLedgerEntryStatus status,
            string conflictCheck = "",
            bool confirmed = false,
            CancellationToken ct = default)
        {
            await _ioLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var document = await LoadWithoutLockAsync(ct).ConfigureAwait(false);
                var entry = document.CanonLedger.FirstOrDefault(e =>
                    string.Equals(e.Id, entryId?.Trim(), StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                {
                    return new StoryBibleCommitResult
                    {
                        Success = false,
                        Message = "未找到指定设定账本条目。",
                        StoragePath = GetStoragePath(),
                        Document = Clone(document)
                    };
                }

                entry.Status = status;
                if (!string.IsNullOrWhiteSpace(conflictCheck))
                    entry.ConflictCheck = conflictCheck.Trim();
                NormalizeEntry(entry);
                Touch(document);
                AddRevision(
                    document,
                    "UpdateLedgerEntryStatus",
                    $"设定账本条目状态更新：{entry.Title} -> {entry.Status}",
                    entry.SourceRunId,
                    entry.SourceChapterId);
                await SaveWithoutLockAsync(document, ct).ConfigureAwait(false);

                return new StoryBibleCommitResult
                {
                    Success = true,
                    Message = $"设定账本条目状态已更新为 {status}。",
                    StoragePath = GetStoragePath(),
                    Document = Clone(document)
                };
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<StoryBibleCommitResult> CommitVolumeArcAsync(
            VolumeArcPlan plan,
            string sourceRunId = "",
            bool overwrite = false,
            bool confirmed = false,
            CancellationToken ct = default)
        {
            if (plan == null)
            {
                return new StoryBibleCommitResult
                {
                    Success = false,
                    Message = "卷级规划为空，无法提交。",
                    StoragePath = GetStoragePath()
                };
            }

            await _ioLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var document = await LoadWithoutLockAsync(ct).ConfigureAwait(false);
                NormalizeVolumeArc(plan);
                var existingIndex = document.VolumeArcs.FindIndex(v =>
                    string.Equals(v.VolumeId, plan.VolumeId, StringComparison.OrdinalIgnoreCase));
                if (existingIndex >= 0 && !overwrite)
                {
                    return new StoryBibleCommitResult
                    {
                        Success = false,
                        RequiresOverwrite = true,
                        Message = "当前 Story Bible 已存在同一卷级规划。若要覆盖，请明确传入 overwrite=true。",
                        StoragePath = GetStoragePath(),
                        Document = Clone(document)
                    };
                }

                plan.Status = VolumeArcStatus.Canon;
                NormalizeVolumeArc(plan);
                if (existingIndex >= 0)
                    document.VolumeArcs[existingIndex] = plan;
                else
                    document.VolumeArcs.Add(plan);

                EnsureVolumeForeshadowLedger(document, plan, sourceRunId);
                Touch(document);
                AddRevision(
                    document,
                    "CommitVolumeArc",
                    $"提交卷级规划：{FirstNonEmpty(plan.Title, plan.VolumeId)}",
                    sourceRunId);
                await SaveWithoutLockAsync(document, ct).ConfigureAwait(false);

                return new StoryBibleCommitResult
                {
                    Success = true,
                    Message = "卷级规划已提交到 Story Bible。",
                    StoragePath = GetStoragePath(),
                    Document = Clone(document)
                };
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<ForeshadowMaintenanceResult> AddForeshadowEntryAsync(
            ForeshadowLedgerEntry entry,
            bool confirmed = false,
            CancellationToken ct = default)
        {
            if (entry == null)
            {
                return new ForeshadowMaintenanceResult
                {
                    Success = false,
                    Message = "伏笔账本条目为空，无法追加。",
                    Document = await LoadAsync(ct).ConfigureAwait(false)
                };
            }

            await _ioLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var document = await LoadWithoutLockAsync(ct).ConfigureAwait(false);
                NormalizeForeshadowEntry(entry);
                document.ForeshadowLedger.RemoveAll(e => string.Equals(e.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
                document.ForeshadowLedger.Insert(0, entry);
                Touch(document);
                AddRevision(
                    document,
                    "AddForeshadowEntry",
                    $"新增伏笔账本条目：{entry.Name} [{entry.Status}]",
                    entry.SourceRunId,
                    entry.SourceChapterId);
                await SaveWithoutLockAsync(document, ct).ConfigureAwait(false);

                return new ForeshadowMaintenanceResult
                {
                    Success = true,
                    Message = "伏笔账本条目已追加。",
                    ImportedEntries = new List<ForeshadowLedgerEntry> { entry },
                    Document = Clone(document)
                };
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<ForeshadowMaintenanceResult> UpdateForeshadowEntryStatusAsync(
            string entryId,
            ForeshadowLedgerStatus status,
            string chapterId = "",
            string note = "",
            bool confirmed = false,
            CancellationToken ct = default)
        {
            await _ioLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var document = await LoadWithoutLockAsync(ct).ConfigureAwait(false);
                var entry = document.ForeshadowLedger.FirstOrDefault(e =>
                    string.Equals(e.Id, entryId?.Trim(), StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                {
                    return new ForeshadowMaintenanceResult
                    {
                        Success = false,
                        Message = "未找到指定伏笔账本条目。",
                        Document = Clone(document)
                    };
                }

                ApplyForeshadowStatus(entry, status, chapterId, note);
                NormalizeForeshadowEntry(entry);
                Touch(document);
                AddRevision(
                    document,
                    "UpdateForeshadowEntryStatus",
                    $"伏笔账本状态更新：{entry.Name} -> {entry.Status}",
                    entry.SourceRunId,
                    FirstNonEmpty(chapterId, entry.SourceChapterId));
                await SaveWithoutLockAsync(document, ct).ConfigureAwait(false);

                return new ForeshadowMaintenanceResult
                {
                    Success = true,
                    Message = $"伏笔账本条目状态已更新为 {status}。",
                    UpdatedEntries = new List<ForeshadowLedgerEntry> { entry },
                    Document = Clone(document)
                };
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<CharacterMaintenanceResult> AddCharacterEntryAsync(
            CharacterLedgerEntry entry,
            bool confirmed = false,
            CancellationToken ct = default)
        {
            if (entry == null)
            {
                return new CharacterMaintenanceResult
                {
                    Success = false,
                    Message = "角色状态账本条目为空，无法追加。",
                    Document = await LoadAsync(ct).ConfigureAwait(false)
                };
            }

            await _ioLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var document = await LoadWithoutLockAsync(ct).ConfigureAwait(false);
                NormalizeCharacterEntry(entry);
                document.CharacterLedger.RemoveAll(e => string.Equals(e.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
                document.CharacterLedger.Insert(0, entry);
                Touch(document);
                AddRevision(
                    document,
                    "AddCharacterEntry",
                    $"新增角色状态账本条目：{entry.CharacterName} / {entry.Type} [{entry.Status}]",
                    entry.SourceRunId,
                    entry.SourceChapterId);
                await SaveWithoutLockAsync(document, ct).ConfigureAwait(false);

                return new CharacterMaintenanceResult
                {
                    Success = true,
                    Message = "角色状态账本条目已追加。",
                    ImportedEntries = new List<CharacterLedgerEntry> { entry },
                    Document = Clone(document)
                };
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public async Task<CharacterMaintenanceResult> UpdateCharacterEntryStatusAsync(
            string entryId,
            CharacterLedgerStatus status,
            string chapterId = "",
            string note = "",
            bool confirmed = false,
            CancellationToken ct = default)
        {
            await _ioLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var document = await LoadWithoutLockAsync(ct).ConfigureAwait(false);
                var entry = document.CharacterLedger.FirstOrDefault(e =>
                    string.Equals(e.Id, entryId?.Trim(), StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                {
                    return new CharacterMaintenanceResult
                    {
                        Success = false,
                        Message = "未找到指定角色状态账本条目。",
                        Document = Clone(document)
                    };
                }

                ApplyCharacterStatus(entry, status, chapterId, note);
                NormalizeCharacterEntry(entry);
                Touch(document);
                AddRevision(
                    document,
                    "UpdateCharacterEntryStatus",
                    $"角色状态账本更新：{entry.CharacterName} / {entry.Type} -> {entry.Status}",
                    entry.SourceRunId,
                    FirstNonEmpty(chapterId, entry.SourceChapterId));
                await SaveWithoutLockAsync(document, ct).ConfigureAwait(false);

                return new CharacterMaintenanceResult
                {
                    Success = true,
                    Message = $"角色状态账本条目状态已更新为 {status}。",
                    UpdatedEntries = new List<CharacterLedgerEntry> { entry },
                    Document = Clone(document)
                };
            }
            finally
            {
                _ioLock.Release();
            }
        }

        private async Task<StoryBibleDocument> LoadWithoutLockAsync(CancellationToken ct)
        {
            if (_cache != null) return _cache;

            _cache = _documentStore == null
                ? new StoryBibleDocument()
                : await _documentStore.LoadAsync(ct).ConfigureAwait(false) ?? new StoryBibleDocument();
            Normalize(_cache);
            return _cache;
        }

        private async Task SaveWithoutLockAsync(StoryBibleDocument document, CancellationToken ct)
        {
            Normalize(document);
            if (_documentStore != null)
                await _documentStore.SaveAsync(Clone(document), ct).ConfigureAwait(false);
            _cache = Clone(document);
        }

        private static void EnsureConstitutionLedger(
            StoryBibleDocument document,
            StoryCreativeConstitution constitution,
            string sourceRunId)
        {
            if (document.CanonLedger.Any(e => e.Type == CanonLedgerEntryType.Constraint &&
                                              string.Equals(e.Title, "故事创意宪法", StringComparison.Ordinal)))
                return;

            document.CanonLedger.Insert(0, new CanonLedgerEntry
            {
                Type = CanonLedgerEntryType.Constraint,
                Status = CanonLedgerEntryStatus.Canon,
                Title = "故事创意宪法",
                Content = $"{constitution.ReaderPromise}\n{constitution.CoreHook}\n{constitution.WorldCoreRule}",
                Rationale = "整书级创作约束，作为后续章节规划和一致性校验的最高层依据。",
                ImpactScope = "全书",
                ConflictCheck = "首次提交，未发现既有 Story Bible 冲突。",
                SourceRunId = sourceRunId ?? string.Empty
            });
        }

        private static void EnsureVolumeForeshadowLedger(
            StoryBibleDocument document,
            VolumeArcPlan plan,
            string sourceRunId)
        {
            if (plan.ForeshadowingPlan == null || plan.ForeshadowingPlan.Count == 0)
                return;

            foreach (var foreshadow in plan.ForeshadowingPlan)
            {
                if (string.IsNullOrWhiteSpace(foreshadow.Name))
                    continue;

                var exists = document.ForeshadowLedger.Any(e =>
                    string.Equals(e.Name, foreshadow.Name, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(e.SourceVolumeId, plan.VolumeId, StringComparison.OrdinalIgnoreCase));
                if (exists)
                    continue;

                var entry = new ForeshadowLedgerEntry
                {
                    Name = foreshadow.Name,
                    Type = InferForeshadowType(foreshadow.Name + " " + foreshadow.Setup + " " + foreshadow.Payoff),
                    Status = ForeshadowLedgerStatus.Planned,
                    Setup = foreshadow.Setup,
                    Payoff = foreshadow.Payoff,
                    SourceVolumeId = plan.VolumeId,
                    PlannedSetupChapterId = plan.StartChapterId,
                    PlannedPayoffChapterId = ResolveBeatChapterId(plan, foreshadow.PayoffBeatIndex),
                    Importance = 7,
                    SourceRunId = sourceRunId ?? string.Empty,
                    Notes = new List<string> { $"由卷级规划「{FirstNonEmpty(plan.Title, plan.VolumeId)}」自动创建。" }
                };
                NormalizeForeshadowEntry(entry);
                document.ForeshadowLedger.Insert(0, entry);
            }
        }

        private static void Normalize(StoryBibleDocument document)
        {
            document.SchemaVersion = Math.Max(1, document.SchemaVersion);
            document.MacroCandidates ??= new List<MacroStoryConceptCandidate>();
            document.VolumeArcs ??= new List<VolumeArcPlan>();
            document.ForeshadowLedger ??= new List<ForeshadowLedgerEntry>();
            document.CharacterLedger ??= new List<CharacterLedgerEntry>();
            document.CanonLedger ??= new List<CanonLedgerEntry>();
            document.AgentRuns ??= new List<NovelAgentRun>();
            document.Revisions ??= new List<StoryBibleRevision>();
            if (document.CreatedAt == default) document.CreatedAt = DateTime.Now;
            if (document.UpdatedAt == default) document.UpdatedAt = document.CreatedAt;
            foreach (var volumeArc in document.VolumeArcs)
                NormalizeVolumeArc(volumeArc);
            foreach (var entry in document.ForeshadowLedger)
                NormalizeForeshadowEntry(entry);
            foreach (var entry in document.CharacterLedger)
                NormalizeCharacterEntry(entry);
            foreach (var entry in document.CanonLedger)
                NormalizeEntry(entry);
            foreach (var revision in document.Revisions)
                NormalizeRevision(revision);
        }

        private static NovelAgentRun? FindRun(StoryBibleDocument document, string runId)
        {
            return document.AgentRuns.FirstOrDefault(r =>
                string.Equals(r.RunId, runId?.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static void Touch(NovelAgentRun run)
        {
            if (run.CreatedAt == default) run.CreatedAt = DateTime.Now;
            run.UpdatedAt = DateTime.Now;
        }

        private static void Touch(StoryBibleDocument document)
        {
            Normalize(document);
            document.UpdatedAt = DateTime.Now;
        }

        private static void NormalizeEntry(CanonLedgerEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
                entry.Id = Guid.NewGuid().ToString("N");
            entry.Title = entry.Title?.Trim() ?? string.Empty;
            entry.Content = entry.Content?.Trim() ?? string.Empty;
            entry.Rationale = entry.Rationale?.Trim() ?? string.Empty;
            entry.ImpactScope = entry.ImpactScope?.Trim() ?? string.Empty;
            entry.ConflictCheck = entry.ConflictCheck?.Trim() ?? string.Empty;
            entry.SourceRunId = entry.SourceRunId?.Trim() ?? string.Empty;
            entry.SourceChapterId = entry.SourceChapterId?.Trim() ?? string.Empty;
            if (entry.CreatedAt == default) entry.CreatedAt = DateTime.Now;
            entry.UpdatedAt = DateTime.Now;
        }

        private static void NormalizeForeshadowEntry(ForeshadowLedgerEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
                entry.Id = Guid.NewGuid().ToString("N");
            entry.Name = entry.Name?.Trim() ?? string.Empty;
            entry.Setup = entry.Setup?.Trim() ?? string.Empty;
            entry.Payoff = entry.Payoff?.Trim() ?? string.Empty;
            entry.SourceVolumeId = entry.SourceVolumeId?.Trim() ?? string.Empty;
            entry.PlannedSetupChapterId = entry.PlannedSetupChapterId?.Trim() ?? string.Empty;
            entry.PlannedPayoffChapterId = entry.PlannedPayoffChapterId?.Trim() ?? string.Empty;
            entry.ActualSetupChapterIds = NormalizeList(entry.ActualSetupChapterIds);
            entry.ActualReinforceChapterIds = NormalizeList(entry.ActualReinforceChapterIds);
            entry.ActualPayoffChapterId = entry.ActualPayoffChapterId?.Trim() ?? string.Empty;
            entry.Importance = Math.Clamp(entry.Importance, 1, 10);
            entry.Evidence = NormalizeList(entry.Evidence);
            entry.Notes = NormalizeList(entry.Notes);
            entry.SourceRunId = entry.SourceRunId?.Trim() ?? string.Empty;
            entry.SourceChapterId = entry.SourceChapterId?.Trim() ?? string.Empty;
            if (entry.CreatedAt == default) entry.CreatedAt = DateTime.Now;
            entry.UpdatedAt = DateTime.Now;
        }

        private static void NormalizeCharacterEntry(CharacterLedgerEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
                entry.Id = Guid.NewGuid().ToString("N");
            entry.CharacterName = entry.CharacterName?.Trim() ?? string.Empty;
            entry.Role = entry.Role?.Trim() ?? string.Empty;
            entry.Summary = entry.Summary?.Trim() ?? string.Empty;
            entry.CurrentGoal = entry.CurrentGoal?.Trim() ?? string.Empty;
            entry.CurrentIntent = entry.CurrentIntent?.Trim() ?? string.Empty;
            entry.NextPressure = entry.NextPressure?.Trim() ?? string.Empty;
            entry.Secret ??= new CharacterSecretState();
            entry.Relationship ??= new CharacterRelationshipState();
            entry.AbilityCost ??= new CharacterAbilityCostState();
            entry.Psychology ??= new CharacterPsychologyState();
            entry.Secret.Content = entry.Secret.Content?.Trim() ?? string.Empty;
            entry.Secret.KnownBy = NormalizeList(entry.Secret.KnownBy);
            entry.Relationship.TargetCharacter = entry.Relationship.TargetCharacter?.Trim() ?? string.Empty;
            entry.Relationship.Tension = entry.Relationship.Tension?.Trim() ?? string.Empty;
            entry.Relationship.Change = entry.Relationship.Change?.Trim() ?? string.Empty;
            entry.AbilityCost.Ability = entry.AbilityCost.Ability?.Trim() ?? string.Empty;
            entry.AbilityCost.LevelOrBoundary = entry.AbilityCost.LevelOrBoundary?.Trim() ?? string.Empty;
            entry.AbilityCost.Cost = entry.AbilityCost.Cost?.Trim() ?? string.Empty;
            entry.AbilityCost.Debt = entry.AbilityCost.Debt?.Trim() ?? string.Empty;
            entry.AbilityCost.Limitation = entry.AbilityCost.Limitation?.Trim() ?? string.Empty;
            entry.Psychology.Emotion = entry.Psychology.Emotion?.Trim() ?? string.Empty;
            entry.Psychology.Wound = entry.Psychology.Wound?.Trim() ?? string.Empty;
            entry.Psychology.CopingStrategy = entry.Psychology.CopingStrategy?.Trim() ?? string.Empty;
            entry.Psychology.StressLevel = Math.Clamp(entry.Psychology.StressLevel, 1, 10);
            entry.BeliefShift = entry.BeliefShift?.Trim() ?? string.Empty;
            entry.IdentityState = entry.IdentityState?.Trim() ?? string.Empty;
            entry.Importance = Math.Clamp(entry.Importance, 1, 10);
            entry.Evidence = NormalizeList(entry.Evidence);
            entry.Notes = NormalizeList(entry.Notes);
            entry.SourceRunId = entry.SourceRunId?.Trim() ?? string.Empty;
            entry.SourceChapterId = entry.SourceChapterId?.Trim() ?? string.Empty;
            if (entry.CreatedAt == default) entry.CreatedAt = DateTime.Now;
            entry.UpdatedAt = DateTime.Now;
        }

        private static void NormalizeVolumeArc(VolumeArcPlan plan)
        {
            if (string.IsNullOrWhiteSpace(plan.Id))
                plan.Id = Guid.NewGuid().ToString("N");
            plan.VolumeId = plan.VolumeId?.Trim() ?? string.Empty;
            plan.Title = plan.Title?.Trim() ?? string.Empty;
            plan.StartChapterId = plan.StartChapterId?.Trim() ?? string.Empty;
            plan.EndChapterId = plan.EndChapterId?.Trim() ?? string.Empty;
            plan.ExpectedChapterCount = Math.Max(1, plan.ExpectedChapterCount);
            plan.VolumePromise = plan.VolumePromise?.Trim() ?? string.Empty;
            plan.EntryState = plan.EntryState?.Trim() ?? string.Empty;
            plan.ExitState = plan.ExitState?.Trim() ?? string.Empty;
            plan.CoreQuestion = plan.CoreQuestion?.Trim() ?? string.Empty;
            plan.MainConflictUpgrade = plan.MainConflictUpgrade?.Trim() ?? string.Empty;
            plan.MidpointReversal = plan.MidpointReversal?.Trim() ?? string.Empty;
            plan.Climax = plan.Climax?.Trim() ?? string.Empty;
            plan.AftermathHook = plan.AftermathHook?.Trim() ?? string.Empty;
            plan.ChapterBeats ??= new List<VolumeChapterBeat>();
            plan.ForeshadowingPlan ??= new List<VolumeForeshadowPlan>();
            plan.CharacterArcPlan ??= new List<VolumeCharacterArc>();
            plan.WorldbuildingIncrements = plan.WorldbuildingIncrements?
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
            plan.MustAvoid = plan.MustAvoid?
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
            foreach (var beat in plan.ChapterBeats)
            {
                beat.Role = beat.Role?.Trim() ?? string.Empty;
                beat.Goal = beat.Goal?.Trim() ?? string.Empty;
                beat.Turn = beat.Turn?.Trim() ?? string.Empty;
                beat.Cost = beat.Cost?.Trim() ?? string.Empty;
            }
            foreach (var foreshadow in plan.ForeshadowingPlan)
            {
                foreshadow.Name = foreshadow.Name?.Trim() ?? string.Empty;
                foreshadow.Setup = foreshadow.Setup?.Trim() ?? string.Empty;
                foreshadow.Payoff = foreshadow.Payoff?.Trim() ?? string.Empty;
                foreshadow.PayoffBeatIndex = Math.Max(1, foreshadow.PayoffBeatIndex);
            }
            foreach (var arc in plan.CharacterArcPlan)
            {
                arc.CharacterName = arc.CharacterName?.Trim() ?? string.Empty;
                arc.StartingBelief = arc.StartingBelief?.Trim() ?? string.Empty;
                arc.Pressure = arc.Pressure?.Trim() ?? string.Empty;
                arc.Choice = arc.Choice?.Trim() ?? string.Empty;
                arc.ChangedState = arc.ChangedState?.Trim() ?? string.Empty;
            }
            if (plan.CreatedAt == default) plan.CreatedAt = DateTime.Now;
            plan.UpdatedAt = DateTime.Now;
        }

        private static void NormalizeRevision(StoryBibleRevision revision)
        {
            if (string.IsNullOrWhiteSpace(revision.Id))
                revision.Id = Guid.NewGuid().ToString("N");
            revision.Action = revision.Action?.Trim() ?? string.Empty;
            revision.Summary = revision.Summary?.Trim() ?? string.Empty;
            revision.SourceRunId = revision.SourceRunId?.Trim() ?? string.Empty;
            revision.SourceChapterId = revision.SourceChapterId?.Trim() ?? string.Empty;
            if (revision.CreatedAt == default) revision.CreatedAt = DateTime.Now;
        }

        private static void AddRevision(
            StoryBibleDocument document,
            string action,
            string summary,
            string sourceRunId = "",
            string sourceChapterId = "")
        {
            document.Revisions.Insert(0, new StoryBibleRevision
            {
                Action = action,
                Summary = summary,
                SourceRunId = sourceRunId ?? string.Empty,
                SourceChapterId = sourceChapterId ?? string.Empty
            });

            if (document.Revisions.Count > MaxRevisions)
                document.Revisions.RemoveRange(MaxRevisions, document.Revisions.Count - MaxRevisions);
        }

        private static bool IsHighRiskLedgerStatus(CanonLedgerEntryStatus status)
        {
            return status is CanonLedgerEntryStatus.Canon
                or CanonLedgerEntryStatus.Deprecated
                or CanonLedgerEntryStatus.Conflict;
        }

        private static bool IsHighRiskForeshadowStatus(ForeshadowLedgerStatus status)
        {
            return status is ForeshadowLedgerStatus.PaidOff
                or ForeshadowLedgerStatus.Abandoned
                or ForeshadowLedgerStatus.Conflict;
        }

        private static bool IsHighRiskCharacterStatus(CharacterLedgerStatus status)
        {
            return status is CharacterLedgerStatus.SecretRevealed
                or CharacterLedgerStatus.RelationshipReversed
                or CharacterLedgerStatus.AbilityRuleChanged
                or CharacterLedgerStatus.IdentityRewritten
                or CharacterLedgerStatus.LeftStage
                or CharacterLedgerStatus.Dead
                or CharacterLedgerStatus.Conflict
                or CharacterLedgerStatus.Rejected;
        }

        private static void ApplyForeshadowStatus(
            ForeshadowLedgerEntry entry,
            ForeshadowLedgerStatus status,
            string chapterId,
            string note)
        {
            entry.Status = status;
            var normalizedChapterId = chapterId?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(normalizedChapterId))
            {
                if (status == ForeshadowLedgerStatus.Setup)
                    entry.ActualSetupChapterIds.Add(normalizedChapterId);
                else if (status == ForeshadowLedgerStatus.Reinforced)
                    entry.ActualReinforceChapterIds.Add(normalizedChapterId);
                else if (status == ForeshadowLedgerStatus.PaidOff)
                    entry.ActualPayoffChapterId = normalizedChapterId;

                if (string.IsNullOrWhiteSpace(entry.SourceChapterId))
                    entry.SourceChapterId = normalizedChapterId;
            }

            if (!string.IsNullOrWhiteSpace(note))
                entry.Notes.Add(note.Trim());
        }

        private static void ApplyCharacterStatus(
            CharacterLedgerEntry entry,
            CharacterLedgerStatus status,
            string chapterId,
            string note)
        {
            entry.Status = status;
            var normalizedChapterId = chapterId?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(normalizedChapterId)
                && string.IsNullOrWhiteSpace(entry.SourceChapterId))
            {
                entry.SourceChapterId = normalizedChapterId;
            }

            if (status == CharacterLedgerStatus.SecretRevealed)
                entry.Secret.Status = CharacterSecretStatus.Revealed;
            else if (status == CharacterLedgerStatus.SecretSeeded && entry.Secret.Status == CharacterSecretStatus.None)
                entry.Secret.Status = CharacterSecretStatus.Seeded;
            else if (status == CharacterLedgerStatus.RelationshipReversed
                     && entry.Relationship.Status == CharacterRelationshipStatus.Unknown)
                entry.Relationship.Status = CharacterRelationshipStatus.Betrayed;

            if (!string.IsNullOrWhiteSpace(note))
                entry.Notes.Add(note.Trim());
        }

        private static string ResolveBeatChapterId(VolumeArcPlan plan, int beatIndex)
        {
            var startIndex = ExtractTrailingNumber(plan.StartChapterId);
            if (startIndex <= 0 || beatIndex <= 0)
                return plan.EndChapterId;

            var prefix = ExtractPrefixBeforeTrailingNumber(plan.StartChapterId);
            var startNumberToken = ExtractTrailingNumberToken(plan.StartChapterId);
            var resolvedIndex = startIndex + beatIndex - 1;
            var resolvedNumber = startNumberToken.Length > 1 && startNumberToken[0] == '0'
                ? resolvedIndex.ToString().PadLeft(startNumberToken.Length, '0')
                : resolvedIndex.ToString();
            return $"{prefix}{resolvedNumber}";
        }

        private static int ExtractTrailingNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return -1;

            var end = value.Length - 1;
            while (end >= 0 && !char.IsDigit(value[end])) end--;
            if (end < 0) return -1;

            var start = end;
            while (start >= 0 && char.IsDigit(value[start])) start--;
            return int.TryParse(value.Substring(start + 1, end - start), out var number)
                ? number
                : -1;
        }

        private static string ExtractPrefixBeforeTrailingNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            var end = value.Length - 1;
            while (end >= 0 && !char.IsDigit(value[end])) end--;
            if (end < 0) return value.Trim();

            var start = end;
            while (start >= 0 && char.IsDigit(value[start])) start--;
            return value.Substring(0, start + 1).Trim();
        }

        private static string ExtractTrailingNumberToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            var end = value.Length - 1;
            while (end >= 0 && !char.IsDigit(value[end])) end--;
            if (end < 0) return string.Empty;

            var start = end;
            while (start >= 0 && char.IsDigit(value[start])) start--;
            return value.Substring(start + 1, end - start);
        }

        private static ForeshadowLedgerEntryType InferForeshadowType(string text)
        {
            if (text.Contains("规则", StringComparison.OrdinalIgnoreCase)
                || text.Contains("异常", StringComparison.OrdinalIgnoreCase))
                return ForeshadowLedgerEntryType.WorldRule;
            if (text.Contains("关系", StringComparison.OrdinalIgnoreCase)
                || text.Contains("盟友", StringComparison.OrdinalIgnoreCase))
                return ForeshadowLedgerEntryType.Relationship;
            if (text.Contains("秘密", StringComparison.OrdinalIgnoreCase)
                || text.Contains("身份", StringComparison.OrdinalIgnoreCase))
                return ForeshadowLedgerEntryType.CharacterSecret;
            if (text.Contains("代价", StringComparison.OrdinalIgnoreCase)
                || text.Contains("威胁", StringComparison.OrdinalIgnoreCase))
                return ForeshadowLedgerEntryType.Threat;
            return ForeshadowLedgerEntryType.Plot;
        }

        private static List<string> NormalizeList(IEnumerable<string>? values)
        {
            return values?
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }

            return string.Empty;
        }

        private static StoryBibleDocument Clone(StoryBibleDocument document)
        {
            var json = JsonSerializer.Serialize(document, JsonHelper.CnDefault);
            return JsonSerializer.Deserialize<StoryBibleDocument>(json, JsonHelper.CnDefault) ?? new StoryBibleDocument();
        }

        private static NovelAgentRun Clone(NovelAgentRun run)
        {
            var json = JsonSerializer.Serialize(run, JsonHelper.CnDefault);
            return JsonSerializer.Deserialize<NovelAgentRun>(json, JsonHelper.CnDefault) ?? run;
        }
    }
}
