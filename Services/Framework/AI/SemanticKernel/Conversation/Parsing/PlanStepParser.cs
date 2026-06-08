using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TM.Services.Framework.AI.SemanticKernel.Conversation.Models;

namespace TM.Services.Framework.AI.SemanticKernel.Conversation.Parsing
{
    public class PlanStepParser : IPlanParser
    {
        private static readonly Regex StepLineRegex = new(@"^\s*\d+[\.、]", RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex StepPattern = new(
            @"^[\*\s]*\*?\*?" +
            @"(?:" +
                @"(\d+)[\.、\)\:：]\s*(.+)" +
                @"|" +
                @"(?:步骤|Step|STEP)\s*(\d+|[一二三四五六七八九十百千万零壹贰叁肆伍陆柒捌玖拾佰仟萬两〇]+)[：:\s]+(.+)" +
                @"|" +
                @"第\s*(\d+|[一二三四五六七八九十百千万零壹贰叁肆伍陆柒捌玖拾佰仟萬两〇]+)\s*步[：:\s]+(.+)" +
            @")" +
            @"\*?\*?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public IReadOnlyList<PlanStep> Parse(string content)
        {
            var result = new List<PlanStep>();
            if (string.IsNullOrWhiteSpace(content))
                return result;

            var lines = content.Split('\n');
            int currentIndex = 0;
            var currentDetail = new StringBuilder();

            foreach (var rawLine in lines)
            {
                var trimmed = rawLine.Trim();
                var match = StepPattern.Match(trimmed);

                if (match.Success)
                {
                    SavePreviousStep(result, currentIndex, currentDetail);

                    int idx = 0;
                    string title = "";

                    if (!string.IsNullOrEmpty(match.Groups[1].Value))
                    {
                        idx = int.Parse(match.Groups[1].Value);
                        title = match.Groups[2].Value;
                    }
                    else if (!string.IsNullOrEmpty(match.Groups[3].Value))
                    {
                        idx = ChineseNumberParser.Parse(match.Groups[3].Value);
                        title = match.Groups[4].Value;
                    }
                    else if (!string.IsNullOrEmpty(match.Groups[5].Value))
                    {
                        idx = ChineseNumberParser.Parse(match.Groups[5].Value);
                        title = match.Groups[6].Value;
                    }

                    title = CleanTitle(title);
                    if (idx > 0 && !string.IsNullOrEmpty(title) && title.Length > 2)
                    {
                        result.Add(new PlanStep { Index = idx, Title = title });
                        currentIndex = idx;
                        currentDetail.Clear();
                        continue;
                    }
                }

                if (currentIndex > 0 && !string.IsNullOrEmpty(trimmed))
                {
                    currentDetail.AppendLine(trimmed);
                }
            }

            SavePreviousStep(result, currentIndex, currentDetail);

            TM.App.Log($"[PlanStepParser] 解析完成，共 {result.Count} 个步骤");
            return result;
        }

        public int CountSteps(string content)
        {
            if (string.IsNullOrEmpty(content))
                return 0;

            if (!content.Contains("步骤") && !content.Contains("计划") &&
                !content.Contains("Step") && !content.Contains("**目标**") &&
                !StepLineRegex.IsMatch(content))
            {
                return 0;
            }

            var lines = content.Split('\n');
            int count = 0;

            foreach (var line in lines)
            {
                if (StepPattern.IsMatch(line.Trim()))
                {
                    count++;
                }
            }

            return count;
        }

        private static void SavePreviousStep(List<PlanStep> result, int currentIndex, StringBuilder detail)
        {
            if (currentIndex > 0 && result.Count > 0)
            {
                result[result.Count - 1].Detail = detail.ToString().Trim();
            }
        }

        private static string CleanTitle(string title)
        {
            return title.Replace("**", "").Trim();
        }
    }
}
