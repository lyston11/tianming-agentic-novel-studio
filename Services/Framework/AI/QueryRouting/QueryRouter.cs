using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TM.Services.Framework.AI.QueryRouting
{
    public enum QueryRoute
    {
        Precise,
        Semantic,
        Hybrid
    }

    public class QueryRouter
    {
        private static readonly Regex ChapterIdRegex = new(@"vol\d+_ch\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex GuidRegex = new(@"[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly HashSet<string> _nameIndex = new(StringComparer.OrdinalIgnoreCase);

        public QueryRouter() { }

        public void UpdateNameIndex(IEnumerable<string> names)
        {
            _nameIndex.Clear();
            foreach (var name in names)
            {
                if (!string.IsNullOrEmpty(name))
                    _nameIndex.Add(name);
            }
        }

        public QueryRoute RouteQuery(string query)
        {
            if (string.IsNullOrEmpty(query))
                return QueryRoute.Precise;

            if (ChapterIdRegex.IsMatch(query))
                return QueryRoute.Precise;

            if (GuidRegex.IsMatch(query))
                return QueryRoute.Precise;

            if (query.Contains("哪章") || query.Contains("之前") ||
                query.Contains("什么时候") || query.Contains("原文") ||
                query.Contains("具体") || query.Contains("详细"))
                return QueryRoute.Semantic;

            if (ContainsKnownName(query))
                return QueryRoute.Precise;

            return QueryRoute.Hybrid;
        }

        public QueryRouteResult RouteWithDetails(string query)
        {
            var route = RouteQuery(query);
            return new QueryRouteResult
            {
                Route = route,
                Query = query,
                MatchedNames = ExtractMatchedNames(query),
                ChapterIds = ExtractChapterIds(query),
                EntityIds = ExtractEntityIds(query)
            };
        }

        private bool ContainsKnownName(string query)
        {
            return _nameIndex.Any(name => query.Contains(name, StringComparison.OrdinalIgnoreCase));
        }

        private List<string> ExtractMatchedNames(string query)
        {
            return _nameIndex
                .Where(name => query.Contains(name, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static List<string> ExtractChapterIds(string query)
        {
            var matches = ChapterIdRegex.Matches(query);
            return matches.Select(m => m.Value.ToLowerInvariant()).Distinct().ToList();
        }

        private static List<string> ExtractEntityIds(string query)
        {
            var matches = GuidRegex.Matches(query);
            return matches.Select(m => m.Value.ToLowerInvariant()).Distinct().ToList();
        }
    }

    public class QueryRouteResult
    {
        public QueryRoute Route { get; set; }
        public string Query { get; set; } = string.Empty;
        public List<string> MatchedNames { get; set; } = new();
        public List<string> ChapterIds { get; set; } = new();
        public List<string> EntityIds { get; set; } = new();
    }
}
