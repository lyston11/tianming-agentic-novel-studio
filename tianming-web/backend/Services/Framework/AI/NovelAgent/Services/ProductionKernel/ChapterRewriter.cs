using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TM.Services.Framework.AI.NovelAgent.Models;
using TM.Services.Modules.ProjectData.Implementations;

namespace TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel
{
    public sealed class ChapterRewriter : IChapterRewriter
    {
        private readonly IChapterPromptBuilder _promptBuilder;

        public ChapterRewriter(IChapterPromptBuilder promptBuilder)
        {
            _promptBuilder = promptBuilder;
        }

        public async Task<ChapterDraftArtifact> RepairAsync(
            ChapterRewriteRequest request,
            CancellationToken ct = default)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (request.CompleteAsync == null)
                throw new InvalidOperationException("未提供章节修订模型调用器。");

            var changesOnlyRepair = ShouldRepairChangesOnly(request.GateReport);
            var raw = await request.CompleteAsync(
                    changesOnlyRepair ? _promptBuilder.BuildChangesOnlyRepairSystemPrompt() : _promptBuilder.BuildWritingSystemPrompt(),
                    changesOnlyRepair
                        ? _promptBuilder.BuildChangesOnlyRepairUserPrompt(request.Run, request.ContextPackage, request.Draft, request.GateReport)
                        : _promptBuilder.BuildRepairUserPrompt(request.Run, request.ContextPackage, request.Draft, request.GateReport),
                    ct)
                .ConfigureAwait(false);

            if (changesOnlyRepair)
                raw = ChapterChangesText.MergeChangesOnlyRepair(request.Draft.DraftContent, raw);

            var repaired = new ChapterDraftArtifact
            {
                ChapterId = request.Run.TargetChapterId,
                DraftContent = raw.Trim(),
                ChangesJson = ChapterChangesText.ExtractChangesJson(raw),
                HasChanges = GenerationGate.HasChangesRegion(raw),
                ArtifactId = request.Draft.ArtifactId,
                RepairAttemptCount = request.Draft.RepairAttemptCount + 1,
                Status = "repairing"
            };
            return repaired;
        }

        private static bool ShouldRepairChangesOnly(GenerationGateReport report)
        {
            if (report.Issues.Count == 0) return false;
            return report.Issues.All(IsChangesProtocolIssue);
        }

        private static bool IsChangesProtocolIssue(string issue)
        {
            if (string.IsNullOrWhiteSpace(issue)) return false;
            if (ContainsNonProtocolFailure(issue)) return false;
            return issue.Contains("CHANGES", StringComparison.OrdinalIgnoreCase) ||
                   issue.Contains("修订记录", StringComparison.OrdinalIgnoreCase) ||
                   issue.Contains("JSON", StringComparison.OrdinalIgnoreCase) ||
                   issue.Contains("协议", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsNonProtocolFailure(string issue) =>
            issue.Contains("知识库硬事实", StringComparison.Ordinal) ||
            issue.Contains("能力边界", StringComparison.Ordinal) ||
            issue.Contains("核心连续性", StringComparison.Ordinal) ||
            issue.Contains("上一章", StringComparison.Ordinal) ||
            issue.Contains("主角", StringComparison.Ordinal) ||
            issue.Contains("正文出现", StringComparison.Ordinal) ||
            issue.Contains("用户要求", StringComparison.Ordinal) ||
            issue.Contains("已采纳创意", StringComparison.Ordinal);
    }
}
