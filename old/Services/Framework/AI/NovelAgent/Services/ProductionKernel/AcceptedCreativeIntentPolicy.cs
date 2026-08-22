using System;
using System.Linq;
using TM.Framework.Common.Helpers;
using TM.Services.Framework.AI.NovelAgent.Models;

namespace TM.Services.Framework.AI.NovelAgent.Services.ProductionKernel
{
    public static class AcceptedCreativeIntentPolicy
    {
        public static bool IsChapterRequired(AcceptedCreativeIntentSnapshot intent, string chapterId)
        {
            if (!HasText(intent.NormalizedIntent))
                return false;

            if (HasText(intent.TargetChapterId))
                return IsSameChapterIdentity(intent.TargetChapterId, chapterId);

            if (string.Equals(intent.TargetScope, "chapter", StringComparison.OrdinalIgnoreCase))
                return true;

            return string.Equals(intent.ImpactLevel, "chapter_rewrite", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(intent.ImpactLevel, "minor_edit", StringComparison.OrdinalIgnoreCase);
        }

        public static string FormatForDirective(AcceptedCreativeIntentSnapshot intent, string chapterId)
        {
            var applies = IsChapterRequired(intent, chapterId) ? "本章必须执行" : "后续方向参考";
            var target = HasText(intent.TargetChapterId) ? $"目标章节={intent.TargetChapterId}" : $"目标范围={intent.TargetScope}";
            var impact = HasText(intent.ImpactLevel) ? $"影响={intent.ImpactLevel}" : "影响=unspecified";
            var source = FirstNonEmpty(intent.Source, "unknown");
            return $"{applies}：{intent.NormalizedIntent}（{target}；{impact}；来源={source}）";
        }

        private static bool IsSameChapterIdentity(string? left, string? right)
        {
            if (!HasText(left) || !HasText(right))
                return false;

            if (string.Equals(left!.Trim(), right!.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;

            var leftNumber = ExtractChapterNumber(left);
            var rightNumber = ExtractChapterNumber(right);
            return leftNumber > 0 && leftNumber == rightNumber;
        }

        private static int ExtractChapterNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            return ChapterParserHelper.ExtractChapterNumber(value);
        }

        private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

        private static string FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(HasText)?.Trim() ?? string.Empty;
    }
}
