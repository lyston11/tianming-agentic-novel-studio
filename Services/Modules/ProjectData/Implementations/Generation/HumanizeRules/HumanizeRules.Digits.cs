using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TM.Services.Modules.ProjectData.Implementations.Generation
{
    internal static partial class HumanizeRules
    {
        private static readonly Dictionary<char, char> DigitToChineseMap = new()
        {
            ['0'] = '零',
            ['1'] = '一',
            ['2'] = '二',
            ['3'] = '三',
            ['4'] = '四',
            ['5'] = '五',
            ['6'] = '六',
            ['7'] = '七',
            ['8'] = '八',
            ['9'] = '九',
        };

        private static readonly Dictionary<string, string> ChineseDecadeMap = new()
        {
            ["10"] = "十",
            ["20"] = "二十",
            ["30"] = "三十",
            ["40"] = "四十",
            ["50"] = "五十",
            ["60"] = "六十",
            ["70"] = "七十",
            ["80"] = "八十",
            ["90"] = "九十",
        };

        private static readonly Regex DigitRule_Year4DigitStrict =
            new(@"(?<!\d)((?:19|20)\d{2})(?=年)", RegexOptions.Compiled);

        private static readonly Regex DigitRule_YearDecade =
            new(@"(?<!\d)(\d0)年代", RegexOptions.Compiled);

        private static readonly Regex DigitRule_MonthStrict =
            new(@"(?<![\d年个])([1-9]|1[0-2])(?=月(?:\D|$))", RegexOptions.Compiled);

        private static readonly Regex DigitRule_DayStrict =
            new(@"(?<![\d月])([1-9]|[12]\d|3[01])(?=[日号])", RegexOptions.Compiled);

    }
}
