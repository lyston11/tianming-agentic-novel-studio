using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TM.Services.Modules.ProjectData.Models.Tracking;

namespace TM.Services.Modules.ProjectData.Implementations
{
    public class ContentEntityExtractor
    {
        private static readonly Regex QuotedDialogueRegex = new(@"[""\u201c]([^""\u201d]{1,10}?)[""\u201d](?:说|道|问|答|笑|喊|叫|骂)", RegexOptions.Compiled);
        private readonly HashSet<string> _knownEntityNames;

        public ContentEntityExtractor(FactSnapshot factSnapshot)
        {
            _knownEntityNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var state in factSnapshot.CharacterStates ?? new())
            {
                if (!string.IsNullOrEmpty(state.Name))
                    _knownEntityNames.Add(state.Name);
            }

            if (factSnapshot.LocationDescriptions != null)
            {
                foreach (var desc in factSnapshot.LocationDescriptions.Values)
                {
                    if (!string.IsNullOrEmpty(desc.Name))
                        _knownEntityNames.Add(desc.Name);
                }
            }

            if (factSnapshot.CharacterDescriptions != null)
            {
                foreach (var desc in factSnapshot.CharacterDescriptions.Values)
                {
                    if (!string.IsNullOrEmpty(desc.Name))
                        _knownEntityNames.Add(desc.Name);
                }
            }
        }

        public ContentEntityExtractor(IEnumerable<string> knownNames)
        {
            _knownEntityNames = new HashSet<string>(knownNames, StringComparer.OrdinalIgnoreCase);
        }

        public HashSet<string> ExtractMentionedEntities(string content)
        {
            var entities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(content))
                return entities;

            foreach (var name in _knownEntityNames)
            {
                if (EntityNameNormalizeHelper.NameExistsInContent(content, name))
                    entities.Add(name);
            }

            var quotedNames = QuotedDialogueRegex.Matches(content);
            foreach (Match m in quotedNames)
            {
                var potentialName = m.Groups[1].Value.Trim();
                if (!_knownEntityNames.Contains(potentialName) && IsLikelyName(potentialName))
                    entities.Add($"[未知]{potentialName}");
            }

            return entities;
        }

        public List<string> GetUnknownEntities(string content)
        {
            var mentioned = ExtractMentionedEntities(content);
            return mentioned
                .Where(e => e.StartsWith("[未知]"))
                .Select(e => e.Replace("[未知]", ""))
                .ToList();
        }

        private static bool IsLikelyName(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 6 || text.Length < 2)
                return false;

            if (text.Any(c => char.IsPunctuation(c) || char.IsDigit(c)))
                return false;

            var commonWords = new HashSet<string>
            {
                "什么", "怎么", "为什么", "这个", "那个", "他们", "我们", "你们",
                "是的", "不是", "好的", "可以", "没有", "知道", "应该", "可能",
                "不要", "不行", "别怕", "等等", "快走", "住手", "小心", "闭嘴",
                "是谁", "哪里", "如今", "现在", "以后", "罢了", "够了", "真的",
                "假的", "原来", "如此", "当然", "不必", "不许", "不能", "为何"
            };

            return !commonWords.Contains(text);
        }
    }
}
