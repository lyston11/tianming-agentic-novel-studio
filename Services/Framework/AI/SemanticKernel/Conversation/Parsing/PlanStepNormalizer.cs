using System;
using System.Collections.Generic;
using System.Text;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Models;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Parsing
{
    public static class PlanStepNormalizer
    {
        public static IReadOnlyList<PlanStep> Normalize(
            string userInput,
            string rawContent,
            IReadOnlyList<PlanStep> parsedSteps)
        {
            if (parsedSteps == null || parsedSteps.Count == 0)
                return parsedSteps ?? Array.Empty<PlanStep>();

            if (Helpers.SingleChapterTaskDetector.IsSingleChapterTask(userInput))
            {
                return MergeToSingleStep(userInput, parsedSteps);
            }

            var (startChapter, endChapter) = ExtractChapterRange(userInput);
            if (startChapter <= 0 || endChapter <= startChapter)
            {
                (startChapter, endChapter) = ExtractChapterRange(rawContent);
            }
            if (startChapter > 0 && endChapter > startChapter)
            {
                return SplitByChapterRange(startChapter, endChapter, parsedSteps);
            }

            var distinctChapters = ExtractDistinctChapters(parsedSteps);
            if (distinctChapters.Count >= 2)
            {
                TM.App.Log($"[PlanStepNormalizer] 多章任务（{distinctChapters.Count} 个章节），保持 {parsedSteps.Count} 个步骤");
                return parsedSteps;
            }

            return MergeToSingleStep(userInput, parsedSteps);
        }

        private static IReadOnlyList<PlanStep> MergeToSingleStep(string userInput, IReadOnlyList<PlanStep> steps)
        {
            if (steps.Count <= 1)
                return steps;

            string chapterTitle = "生成章节";

            foreach (var step in steps)
            {
                if (ChapterParserHelper.IsChapterTitle(step.Title))
                {
                    var (number, name) = ChapterParserHelper.ExtractChapterParts(step.Title);

                    if (number.HasValue)
                    {
                        if (!string.IsNullOrEmpty(name))
                        {
                            chapterTitle = $"第{number.Value}章：{name}";
                        }
                        else
                        {
                            chapterTitle = $"第{number.Value}章";
                        }
                    }
                    else
                    {
                        chapterTitle = step.Title.Trim();
                    }

                    break;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("AI 原始计划：");
            foreach (var s in steps)
            {
                sb.AppendLine($"{s.Index}. {s.Title}");
                if (!string.IsNullOrWhiteSpace(s.Detail))
                {
                    sb.AppendLine(s.Detail);
                }
                sb.AppendLine();
            }

            var merged = new PlanStep
            {
                Index = 1,
                Title = chapterTitle,
                Detail = sb.ToString().Trim()
            };

            TM.App.Log($"[PlanStepNormalizer] 单章任务合并：{steps.Count} 个步骤 → 1 个执行步骤（{chapterTitle}）");
            return new List<PlanStep> { merged };
        }

        private static IReadOnlyList<PlanStep> SplitByChapterRange(
            int startChapter,
            int endChapter,
            IReadOnlyList<PlanStep> originalSteps)
        {
            var chapterCount = endChapter - startChapter + 1;
            var newSteps = new List<PlanStep>();

            var originalContent = new StringBuilder();
            foreach (var s in originalSteps)
            {
                originalContent.AppendLine($"{s.Index}. {s.Title}");
                if (!string.IsNullOrWhiteSpace(s.Detail))
                    originalContent.AppendLine(s.Detail);
            }
            var sharedDetail = originalContent.ToString().Trim();

            for (int i = 0; i < chapterCount; i++)
            {
                var chapterNum = startChapter + i;
                newSteps.Add(new PlanStep
                {
                    Index = i + 1,
                    Title = $"第{chapterNum}章",
                    ChapterNumber = chapterNum,
                    Detail = i == 0
                        ? $"创作计划概要：\n{sharedDetail}"
                        : $"根据前文和大纲，生成第{chapterNum}章内容。"
                });
            }

            TM.App.Log($"[PlanStepNormalizer] 多章任务（第{startChapter}-{endChapter}章），生成 {chapterCount} 个章节步骤");
            return newSteps;
        }

        public static (int start, int end) ExtractChapterRange(string content)
        {
            var range = ChapterParserHelper.ParseChapterRange(content);
            return range ?? (0, 0);
        }

        private static HashSet<string> ExtractDistinctChapters(IReadOnlyList<PlanStep> steps)
        {
            var distinctChapters = new HashSet<string>();

            foreach (var step in steps)
            {
                if (ChapterParserHelper.IsChapterTitle(step.Title))
                {
                    var (number, _) = ChapterParserHelper.ExtractChapterParts(step.Title);
                    if (number.HasValue)
                    {
                        distinctChapters.Add($"第{number.Value}章");
                    }
                }
            }

            return distinctChapters;
        }
    }
}
