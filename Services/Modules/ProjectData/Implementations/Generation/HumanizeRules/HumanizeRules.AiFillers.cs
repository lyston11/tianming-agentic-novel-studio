using System.Text.RegularExpressions;

namespace TM.Services.Modules.ProjectData.Implementations.Generation
{
    internal static partial class HumanizeRules
    {
        private static readonly string[] AiFillerPhrases =
        {
            @"综上所述",
            @"值得注意的是",
            @"不难发现",
            @"总而言之",
            @"不可否认",
            @"毫无疑问",
            @"显而易见",
            @"众所周知",
            @"由此可见",
            @"需要指出的是",
            @"值得一提的是",
            @"不言而喻",
            @"毋庸置疑",
            @"事实上",
            @"实际上",
            @"严格来说",
            @"换句话说",
            @"从某种意义上说",
            @"在一定程度上",
            @"就目前来看",
            @"总的来说",
            @"概括来说",
            @"归根结底",
            @"不仅如此",
            @"需要强调的是",
            @"正如我们所知",
            @"如前所述",
            @"由此可以看出",
            @"不得不说",
            @"正因如此",
        };

        private static readonly Regex[] AiTemplateRegexes =
        {
            new(@"首先[，,].*?其次[，,].*?最后", RegexOptions.Compiled),
            new(@"一方面[，,].*?另一方面", RegexOptions.Compiled),
            new(@"第一[，,].*?第二[，,].*?第三", RegexOptions.Compiled),
            new(@"第一点.*?第二点.*?第三点", RegexOptions.Compiled),
            new(@"其一[，,].*?其二[，,].*?其三", RegexOptions.Compiled),
            new(@"虽然.*?但是.*?同时", RegexOptions.Compiled),
            new(@"一方面.*?另一方面.*?总的来说", RegexOptions.Compiled),
            new(@"既有.*?也有.*?更有", RegexOptions.Compiled),
            new(@"不仅.*?而且.*?更", RegexOptions.Compiled),
            new(@"优点.*?缺点.*?总体", RegexOptions.Compiled),
            new(@"随着.*?的(不断)?发展", RegexOptions.Compiled),
            new(@"在.*?的背景下", RegexOptions.Compiled),
            new(@"在当今.*?时代", RegexOptions.Compiled),
            new(@"作为.*?的重要(组成部分|环节|手段)", RegexOptions.Compiled),
            new(@"对于.*?而言[，,].*?至关重要", RegexOptions.Compiled),
            new(@"这不仅.*?更是", RegexOptions.Compiled),
            new(@"从.*?角度(来看|来说|而言)", RegexOptions.Compiled),
            new(@"无论是.*?还是.*?都", RegexOptions.Compiled),
            new(@"可以说[，,]", RegexOptions.Compiled),
            new(@"总的来说[，,]", RegexOptions.Compiled),
            new(@"(?:(?:①|②|③|④|⑤|1\.|2\.|3\.|4\.|5\.).*?
){3,}", RegexOptions.Compiled),
        };

    }
}
