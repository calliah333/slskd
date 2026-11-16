namespace slskd.Tests.Unit.Shares
{
    using slskd.Shares;
    using Soulseek;
    using Xunit;

    public class ShareSearchQueryTests
    {
        [Fact]
        public void Match_Requires_All_Terms_And_Excludes_Any_Exclusion()
        {
            var query = new ShareSearchQuery(new SearchQuery(terms: ["a", "b"], exclusions: ["x", "y"]));

            Assert.Equal("(\"a\" AND \"b\") NOT (\"x\" OR \"y\")", query.Match);
        }

        [Fact]
        public void Match_Replaces_Query_Syntax_With_Separators()
        {
            var query = new ShareSearchQuery(new SearchQuery(terms: ["a/b\\c:d\"e'f"], exclusions: []));

            Assert.Equal("(\"a b c d e''f\")", query.Match);
        }

        [Fact]
        public void Exclusions_Are_Deduplicated_Ignoring_Case()
        {
            var query = new ShareSearchQuery(new SearchQuery(terms: ["a"], exclusions: ["x", "X"]));

            Assert.Equal("(\"a\") NOT (\"x\")", query.Match);
        }

        [Theory]
        [InlineData(@"@@share\foo\xBARx.mp3", true)]
        [InlineData(@"@@share\foo\bar.mp3", true)]
        [InlineData(@"@@share\foo\baz.mp3", false)]
        public void IsExcluded_Matches_Substrings_Ignoring_Case(string filename, bool expected)
        {
            var query = new ShareSearchQuery(new SearchQuery(terms: ["foo"], exclusions: ["qux", "Bar"]));

            Assert.Equal(expected, query.IsExcluded(filename));
        }

        [Fact]
        public void IsExcluded_Is_False_Without_Exclusions()
        {
            var query = new ShareSearchQuery(new SearchQuery(terms: ["foo"], exclusions: []));

            Assert.False(query.HasExclusions);
            Assert.False(query.IsExcluded(@"@@share\foo\bar.mp3"));
        }

        [Fact]
        public void Empty_Exclusion_Excludes_Everything()
        {
            var query = new ShareSearchQuery(new SearchQuery(terms: ["foo"], exclusions: [string.Empty]));

            Assert.True(query.ExcludesEverything);
            Assert.True(query.IsExcluded(@"@@share\foo\bar.mp3"));
        }
    }
}
