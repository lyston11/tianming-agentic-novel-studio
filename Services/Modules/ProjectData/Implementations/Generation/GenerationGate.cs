using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json;
using TM.Services.Modules.ProjectData.Models.Tracking;
using TM.Services.Modules.ProjectData.Implementations.Tracking.Rules;

namespace TM.Services.Modules.ProjectData.Implementations
{
    [Obfuscation(Feature = "controlflow", Exclude = true, ApplyToMembers = true)]
    public partial class GenerationGate
    {
        private readonly LedgerConsistencyChecker _ledgerConsistencyChecker;
        private readonly LedgerRuleSetProvider _ledgerRuleSetProvider;
        private readonly EntityOmissionDetector _omissionDetector;

        private static readonly object _debugLogLock = new();
        private static readonly HashSet<string> _debugLoggedKeys = new();

        private static readonly string[] NegationPatterns =
        {
            "不能", "不可", "禁止", "严禁", "不得", "不准", "不许",
            "不应", "无法", "不会", "绝不可", "绝不能", "绝不允许"
        };

        private static readonly string[] NegationPrefixes =
        {
            "不能", "不可", "禁止", "严禁", "无法", "不得", "不准", "不许",
            "不应", "不会", "别", "莫", "勿", "休要", "切勿", "切不可",
            "未曾", "从未", "并未", "没有", "绝不", "绝无", "岂能", "怎能", "哪能", "焉能"
        };

        private static void DebugLogOnce(string key, Exception ex)
        {
            if (!TM.App.IsDebugMode)
            {
                return;
            }

            lock (_debugLogLock)
            {
                if (_debugLoggedKeys.Count >= 500 || !_debugLoggedKeys.Add(key))
                {
                    return;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[GenerationGate] {key}: {ex.Message}");
        }

        #region 构造函数

        public GenerationGate(
            LedgerConsistencyChecker ledgerConsistencyChecker,
            LedgerRuleSetProvider ledgerRuleSetProvider,
            EntityOmissionDetector omissionDetector)
        {
            _ledgerConsistencyChecker = ledgerConsistencyChecker;
            _ledgerRuleSetProvider = ledgerRuleSetProvider;
            _omissionDetector = omissionDetector;
        }

        #endregion

        #region 常量

        public const string ChangesSeparator = ChapterChanges.ChangesSeparator;

        public const string ChangesXmlOpen = ChapterChanges.ChangesXmlOpen;
        public const string ChangesXmlClose = ChapterChanges.ChangesXmlClose;

        internal static readonly Regex ChangesXmlBlockRegex = new(
            @"<\s*(chapter_changes|changes)\s*>([\s\S]*?)</\s*\1\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        internal static readonly Regex ChangesXmlOpenRegex = new(
            @"<\s*(chapter_changes|changes)\s*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        internal static readonly Regex ChangesSeparatorLineRegex = new(
            @"(?m)^\s*[-\u2010\u2011\u2012\u2013\u2014\u2212]{3}\s*CHANGES\s*[-\u2010\u2011\u2012\u2013\u2014\u2212]{3}\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex BracketAliasRegex = new(@"[\(（\[【](.+?)[\)）\]】]", RegexOptions.Compiled);

        private static readonly JsonSerializerOptions ChangesParseOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new CharacterStateChangeConverter() }
        };

        internal static readonly Regex MdChangesHeaderRegex = new(
            @"(?m)^(?:---\s*\n+\s*)?#{1,3}\s*(?:CHANGES|变更记录|变更摘要|状态变更|关键词)\s*\n",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static IReadOnlyList<string> ChangesSignatureFields => ChapterChanges.TopLevelFieldNames;

        #endregion

    }
}
