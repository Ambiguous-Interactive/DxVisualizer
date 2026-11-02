namespace WallstopStudios.DataVisualizer.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.DataVisualizer.Editor.Search;

    public sealed class CatalogNameFilterTests
    {
        private static readonly List<(int Start, int Length)> Terms = new();

        private static string[] ParseTerms(string text)
        {
            CatalogNameFilter.ParseTerms(text, 0, text.Length, Terms);
            string[] parsed = new string[Terms.Count];
            for (int index = 0; index < Terms.Count; index++)
            {
                (int start, int length) = Terms[index];
                parsed[index] = text.Substring(start, length);
            }
            return parsed;
        }

        [TestCase("", new string[] { })]
        [TestCase("   ", new string[] { })]
        [TestCase("  Damage  ", new string[] { "Damage" })]
        [TestCase(" Damage   Table ", new string[] { "Damage", "Table" })]
        [TestCase("Damage Table", new string[] { "Damage", "Table" })]
        public void ShouldParseOnlyNonEmptyTermsWhenTextHasSpacing(string text, string[] expected)
        {
            Assert.That(ParseTerms(text), Is.EqualTo(expected));
        }

        [Test]
        public void ShouldSkipTextOutsideTheGivenBoundsWhenParsingTerms()
        {
            CatalogNameFilter.ParseTerms("\tDamage\t", 1, 7, Terms);
            Assert.That(Terms, Is.EqualTo(new[] { (1, 6) }));
        }

        [TestCase("Damage Table", "damage", true)]
        [TestCase("Damage Table", "TABLE", true)]
        [TestCase("Damage Table", "table damage", true)]
        [TestCase("Damage Table", "  damage   table  ", true)]
        [TestCase("Damage Table", "damage missing", false)]
        [TestCase("Damage Table", "Dam Tab", true)]
        [TestCase("Damage Table", "", true)]
        [TestCase("Équipement", "équip", true)]
        public void ShouldMatchWhenEveryTermAppearsInTheName(string name, string filter, bool expected)
        {
            CatalogNameFilter.ParseTerms(filter, 0, filter.Length, Terms);
            Assert.That(CatalogNameFilter.MatchesAll(name, filter, Terms), Is.EqualTo(expected));
        }
    }
}
