using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FitTrackr.API.RAG
{
    public interface IRagGroundingContext
    {
        bool WasSearchInvoked { get; }
        IReadOnlyList<RagGroundingPassage> Passages { get; }
        IReadOnlySet<string> NumericExpressions { get; }
        bool ContainsNumericExpression(string expression);
        void Record(KnowledgeSearchResult result);
    }

    public sealed record RagGroundingPassage(
        string Title,
        string Text,
        string SourceName,
        string SourceUrl,
        string SourceVersion);

    public sealed class RagGroundingContext : IRagGroundingContext
    {
        private static readonly Regex NumericExpressionRegex = new(
            @"(?<![\w])%?\d+(?:[.,]\d+)?(?:\s*[-–‑]\s*%?\d+(?:[.,]\d+)?)?\+?%?(?![\w])",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly List<RagGroundingPassage> _passages = new();
        private readonly HashSet<string> _numericExpressions = new(StringComparer.OrdinalIgnoreCase);

        public bool WasSearchInvoked { get; private set; }
        public IReadOnlyList<RagGroundingPassage> Passages => _passages;
        public IReadOnlySet<string> NumericExpressions => _numericExpressions;

        public void Record(KnowledgeSearchResult result)
        {
            WasSearchInvoked = true;

            foreach (var hit in result.Hits)
            {
                _passages.Add(new RagGroundingPassage(
                    hit.Title,
                    hit.Text,
                    hit.SourceName,
                    hit.SourceUrl,
                    hit.SourceVersion));

                AddNumbers(hit.Title);
                AddNumbers(hit.Text);
                AddNumbers(hit.SourceName);
                AddNumbers(hit.SourceVersion);
            }
        }

        public bool ContainsNumericExpression(string expression) =>
            _numericExpressions.Contains(NormalizeNumber(expression));

        private void AddNumbers(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            foreach (Match match in NumericExpressionRegex.Matches(text))
                _numericExpressions.Add(NormalizeNumber(match.Value));
        }

        internal static string NormalizeNumber(string value) =>
            Regex.Replace(value.Trim().Replace(',', '.'), @"\s+", string.Empty)
            .Replace('–', '-')
            .Replace('‑', '-');
    }
}
