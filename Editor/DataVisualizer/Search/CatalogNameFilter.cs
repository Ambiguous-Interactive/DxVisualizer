namespace WallstopStudios.DataVisualizer.Editor.Search
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Matches catalog display names against space-separated filter terms without per-item
    /// allocations. Callers parse the terms once per filter change and reuse the ranges while
    /// every managed type is tested.
    /// </summary>
    public static class CatalogNameFilter
    {
        /// <summary>Writes the space-separated term ranges of the text range into the caller's list.</summary>
        public static void ParseTerms(string text, int start, int end, List<(int Start, int Length)> terms)
        {
            if (terms == null)
            {
                throw new ArgumentNullException(nameof(terms));
            }

            terms.Clear();
            if (text == null)
            {
                return;
            }

            for (int index = start; index < end; )
            {
                if (text[index] == ' ')
                {
                    index++;
                    continue;
                }

                int termStart = index++;
                while (index < end && text[index] != ' ')
                {
                    index++;
                }

                terms.Add((termStart, index - termStart));
            }
        }

        /// <summary>Reports whether the name contains every parsed term, ignoring case.</summary>
        public static bool MatchesAll(string name, string text, IReadOnlyList<(int Start, int Length)> terms)
        {
            if (terms == null)
            {
                throw new ArgumentNullException(nameof(terms));
            }

            if (terms.Count == 0)
            {
                return true;
            }

            if (text == null)
            {
                return false;
            }

            ReadOnlySpan<char> nameSpan = name.AsSpan();
            ReadOnlySpan<char> textSpan = text.AsSpan();
            for (int index = 0; index < terms.Count; index++)
            {
                (int start, int length) = terms[index];
                if (nameSpan.IndexOf(textSpan.Slice(start, length), StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
