using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TM.Services.Modules.ProjectData.Interfaces;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public partial class UnifiedValidationService : IUnifiedValidationService
    {
        #region 规则校验层（无AI，确定性）

        private const string StructuralModuleName = "StructuralConsistency";

        private async Task RunGateChecksAsync(
            ChapterValidationResult result,
            string chapterId,
            string chapterContent,
            TM.Services.Modules.ProjectData.Models.Guides.ContextIdCollection contextIds)
        {
            try
            {
                var protocol = _generationGate.ValidateChangesProtocol(chapterContent);
                if (!protocol.Success || protocol.Changes == null)
                {
                    TM.App.Log($"[UnifiedValidationService] {chapterId} 无 CHANGES 块，跳过规则层校验");
                    return;
                }

                var issues = new List<ValidationIssue>();

                var structResult = _generationGate.ValidateStructuralOnly(protocol.Changes);
                if (!structResult.Success)
                {
                    foreach (var desc in structResult.GetIssueDescriptions())
                    {
                        issues.Add(new ValidationIssue
                        {
                            Type = "StructuralRule",
                            Severity = "Error",
                            Message = desc
                        });
                    }
                    TM.App.Log($"[UnifiedValidationService] {chapterId} 结构性规则问题: {issues.Count} 条");
                }

                try
                {
                    var factSnapshot = await _guideContextService.ExtractFactSnapshotForChapterAsync(chapterId, contextIds).ConfigureAwait(false);
                    if (factSnapshot != null)
                    {
                        var gateResult = await _generationGate.ValidateAsync(chapterId, chapterContent, factSnapshot, contextIds: contextIds).ConfigureAwait(false);
                        if (!gateResult.Success)
                        {
                            var allFailures = gateResult.GetHumanReadableFailures(int.MaxValue);
                            foreach (var msg in allFailures)
                            {
                                if (issues.Any(i => i.Message == msg)) continue;
                                issues.Add(new ValidationIssue
                                {
                                    Type = "GateRule",
                                    Severity = "Warning",
                                    Message = msg
                                });
                            }
                            TM.App.Log($"[UnifiedValidationService] {chapterId} 门禁规则问题: {allFailures.Count} 条");
                        }
                    }
                }
                catch (Exception ex)
                {
                    TM.App.Log($"[UnifiedValidationService] {chapterId} FactSnapshot 加载失败（不影响 AI 校验）: {ex.Message}");
                }

                if (issues.Count > 0)
                    result.IssuesByModule[StructuralModuleName] = issues;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[UnifiedValidationService] 规则层校验异常: {chapterId}, {ex.Message}");
            }
        }

        #endregion
    }
}
