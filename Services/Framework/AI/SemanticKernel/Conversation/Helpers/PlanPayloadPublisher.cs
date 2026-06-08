using System;
using System.Collections.Generic;
using System.Linq;
using TM.Framework.Common.Helpers.Id;
using TM.Framework.UI.Workspace.RightPanel.Modes;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Models;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Helpers
{
    public static class PlanPayloadPublisher
    {
        public static int PublishToEventHub(PlanPayload planPayload, Guid? runId = null)
        {
            if (planPayload == null || planPayload.Steps.Count == 0)
            {
                return 0;
            }

            var effectiveRunId = runId ?? ShortIdGenerator.NewGuid();

            ExecutionEventHub.Publish(new ExecutionEvent
            {
                RunId = effectiveRunId,
                Mode = ChatMode.Plan,
                RunType = RunType.Execution,
                EventType = ExecutionEventType.RunStarted,
                Title = $"生成计划（{planPayload.Steps.Count} 步骤）"
            });

            foreach (var step in planPayload.Steps)
            {
                string fullDetail;
                if (!string.IsNullOrWhiteSpace(step.Detail) && step.Detail.Trim() != step.Title.Trim())
                {
                    fullDetail = step.Detail;
                }
                else if (!string.IsNullOrWhiteSpace(planPayload.RawContent))
                {
                    fullDetail = planPayload.RawContent;
                }
                else
                {
                    fullDetail = "等待执行...";
                }

                var evt = new ExecutionEvent
                {
                    RunId = effectiveRunId,
                    Mode = ChatMode.Plan,
                    RunType = RunType.Execution,
                    EventType = ExecutionEventType.PlanStepStarted,
                    StepIndex = step.Index,
                    Title = step.Title,
                    Detail = fullDetail
                };
                ExecutionEventHub.Publish(evt);
            }

            return planPayload.Steps.Count;
        }

        public static IReadOnlyList<PlanStep> ToCachedSteps(PlanPayload planPayload)
        {
            if (planPayload == null || planPayload.Steps.Count == 0)
            {
                return Array.Empty<PlanStep>();
            }

            return planPayload.Steps.Select(s => new PlanStep
            {
                Index = s.Index,
                Title = s.Title,
                Detail = s.Detail,
                ChapterNumber = s.ChapterNumber
            }).ToList();
        }

        public static IReadOnlyList<PlanStep>? PublishAndCache(PlanPayload? planPayload, Guid? runId = null)
        {
            if (planPayload == null || planPayload.Steps.Count == 0)
            {
                return null;
            }

            var count = PublishToEventHub(planPayload, runId);
            TM.App.Log($"[PlanPayloadPublisher] 发布 {count} 个计划步骤");

            return ToCachedSteps(planPayload);
        }
    }
}
