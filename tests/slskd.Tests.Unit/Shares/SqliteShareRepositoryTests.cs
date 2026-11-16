namespace slskd.Tests.Unit.Shares
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using slskd.Shares;
    using Soulseek;
    using Xunit;

    public class SqliteShareRepositoryTests
    {
        public class Search
        {
            [Fact]
            public void Returns_Only_Files_Matching_All_Terms()
            {
                var (a, b, c, d, e) = (Random(), Random(), Random(), Random(), Random());

                string[] expected =
                [
                    $@"@@share\{a}\{b}.dat",
                    $@"@@share\{a}\{c}\{b}.dat",
                    $@"@@share\{c}\{b} {a}.dat",
                ];

                using var repository = GetRepository(
                    [
                        .. expected,
                        $@"@@share\{a}\{c}.dat",
                        $@"@@share\{d}\{b}.dat",
                        $@"@@share\{a}{b}.dat",
                        $@"@@share\{c}\{a}{e}\{b}{e}.dat",
                        $@"@@share\{e}\{d}.dat",
                    ]);

                var results = repository.Search(new SearchQuery(terms: [a, b], exclusions: []));

                Assert.Equal(expected.Order(StringComparer.Ordinal), results.Select(f => f.Filename));
            }

            [Fact]
            public void Returns_Nothing_When_No_File_Matches_All_Terms()
            {
                var (a, b, c, d) = (Random(), Random(), Random(), Random());

                using var repository = GetRepository(
                    [
                        $@"@@share\{a}\{c}.dat",
                        $@"@@share\{a}\{d}.dat",
                        $@"@@share\{b}\{c}.dat",
                        $@"@@share\{b}\{d}.dat",
                        $@"@@share\{a}{b}.dat",
                        $@"@@share\{c}\{d}.dat",
                    ]);

                // make sure the files are searchable, so an empty result below means something
                Assert.Equal(2, repository.Search(new SearchQuery(terms: [a], exclusions: [])).Count());

                var results = repository.Search(new SearchQuery(terms: [a, b], exclusions: []));

                Assert.Empty(results);
            }

            [Fact]
            public void Excludes_Files_Matching_Exclusion()
            {
                var (a, b, c, x, y) = (Random(), Random(), Random(), Random(), Random());

                string[] expected =
                [
                    $@"@@share\{a}\{b}.dat",
                    $@"@@share\{a}\{c}.dat",
                    $@"@@share\{a}\{b}\{c}.dat",
                ];

                using var repository = GetRepository(
                    [
                        .. expected,
                        $@"@@share\{a}\{x}.dat",
                        $@"@@share\{a}\{x}\{b}.dat",
                        $@"@@share\{a}\{c}{x}.dat",
                        $@"@@share\{y}\{b}.dat",
                        $@"@@share\{y}\{x}.dat",
                    ]);

                var results = repository.Search(new SearchQuery(terms: [a], exclusions: [x]));

                Assert.Equal(expected.Order(StringComparer.Ordinal), results.Select(f => f.Filename));
            }

            [Fact]
            public void Excludes_Files_Matching_Any_Of_Several_Exclusions()
            {
                var (a, b, c, x, y, z) = (Random(), Random(), Random(), Random(), Random(), Random());

                string[] expected =
                [
                    $@"@@share\{a}\{b}.dat",
                    $@"@@share\{a}\{c}.dat",
                    $@"@@share\{a}\{b}\{c}.dat",
                ];

                using var repository = GetRepository(
                    [
                        .. expected,
                        $@"@@share\{a}\{x}.dat",
                        $@"@@share\{a}\{y}.dat",
                        $@"@@share\{a}\{x}\{y}.dat",
                        $@"@@share\{a}\{b}{y}.dat",
                        $@"@@share\{a}\{z}\{x}.dat",
                        $@"@@share\{z}\{b}.dat",
                    ]);

                var results = repository.Search(new SearchQuery(terms: [a], exclusions: [x, y]));

                Assert.Equal(expected.Order(StringComparer.Ordinal), results.Select(f => f.Filename));
            }

            [Fact]
            public void Matches_Term_Containing_Single_Quote()
            {
                var (a, b, c, d) = (Random(), Random(), Random(), Random());

                string[] expected =
                [
                    $@"@@share\{a}\{b}'{c}.dat",
                    $@"@@share\{d}\{b}'{c} {a}.dat",
                ];

                using var repository = GetRepository(
                    [
                        .. expected,
                        $@"@@share\{a}\{b}.dat",
                        $@"@@share\{a}\{c}.dat",
                        $@"@@share\{a}\{c}'{b}.dat",
                        $@"@@share\{a}\{b}\{d}\{c}.dat",
                        $@"@@share\{d}\{a}.dat",
                    ]);

                var results = repository.Search(new SearchQuery(terms: [$"{b}'{c}"], exclusions: []));

                Assert.Equal(expected.Order(StringComparer.Ordinal), results.Select(f => f.Filename));
            }

            [Fact]
            public void Excludes_Files_Matching_Exclusion_Containing_Single_Quote()
            {
                var (a, b, c, d, e) = (Random(), Random(), Random(), Random(), Random());

                string[] expected =
                [
                    $@"@@share\{a}\{b}.dat",
                    $@"@@share\{a}\{c}.dat",
                    $@"@@share\{a}\{c}'{b}.dat",
                ];

                using var repository = GetRepository(
                    [
                        .. expected,
                        $@"@@share\{a}\{b}'{c}.dat",
                        $@"@@share\{a}\{d}\{b}'{c}.dat",
                        $@"@@share\{a}\{d}{b}'{c}{e}.dat",
                        $@"@@share\{e}\{b}.dat",
                    ]);

                var results = repository.Search(new SearchQuery(terms: [a], exclusions: [$"{b}'{c}"]));

                Assert.Equal(expected.Order(StringComparer.Ordinal), results.Select(f => f.Filename));
            }

            [Theory]
            [InlineData("\"{0} {1}\"")]
            [InlineData("{0}\"{1}")]
            [InlineData("\"{0}\"{1}\"")]
            [InlineData("{0} \"{1}")]
            public void Matches_Term_Containing_Double_Quotes(string template)
            {
                var (a, b, c, d) = (Random(), Random(), Random(), Random());
                var term = string.Format(template, a, b);

                // a double quote in a term must be treated as a separator. if it were left in place it would end the
                // phrase early, either breaking the query or splitting the term into separate words that match the
                // decoys below. if it were removed instead, '{0}"{1}' would become a single word and match nothing
                string[] expected =
                [
                    $@"@@share\{c}\{a}\{b}.dat",
                    $@"@@share\{a} {b}\{d}.dat",
                ];

                using var repository = GetRepository(
                    [
                        .. expected,
                        $@"@@share\{b}\{a}.dat",
                        $@"@@share\{a}\{c}\{b}.dat",
                        $@"@@share\{a}{b}.dat",
                        $@"@@share\{a}\{d}.dat",
                        $@"@@share\{c}\{b}.dat",
                    ]);

                var results = repository.Search(new SearchQuery(terms: [term], exclusions: []));

                Assert.Equal(expected.Order(StringComparer.Ordinal), results.Select(f => f.Filename));
            }

            [Theory]
            [InlineData("\\")]
            [InlineData("/")]
            [InlineData(":")]
            public void Matches_Term_Containing_Separator(string separator)
            {
                var (a, b, c, d) = (Random(), Random(), Random(), Random());

                string[] expected =
                [
                    $@"@@share\{a}\{b}.dat",
                    $@"@@share\{c}\{a}\{b}\{d}.dat",
                ];

                using var repository = GetRepository(
                    [
                        .. expected,
                        $@"@@share\{b}\{a}.dat",
                        $@"@@share\{a}\{c}\{b}.dat",
                        $@"@@share\{a}\{d}.dat",
                        $@"@@share\{c}\{b}.dat",
                    ]);

                var results = repository.Search(new SearchQuery(terms: [$"{a}{separator}{b}"], exclusions: []));

                Assert.Equal(expected.Order(StringComparer.Ordinal), results.Select(f => f.Filename));
            }

            [Theory]
            [InlineData("{0}') OR 1=1 --{1}")]
            [InlineData("{0}' UNION SELECT maskedFilename, code, size, extension, attributeJson FROM files --{1}")]
            [InlineData("{0}\" OR \"{1}")]
            [InlineData("{0} OR {1}")]
            [InlineData("{0} AND {1}")]
            [InlineData("{0} NOT {1}")]
            [InlineData("NEAR({0} {1})")]
            [InlineData("maskedFilename:{0} {1}")]
            public void Treats_Query_Syntax_In_Term_As_Text(string template)
            {
                var (a, b, c, d) = (Random(), Random(), Random(), Random());
                var term = string.Format(template, a, b);

                // only the file containing the literal term should match. if the term were interpreted as SQL or FTS
                // syntax, either the query would fail and nothing would match, or some of the other files would match
                string[] expected =
                [
                    $@"@@share\{c}\{term}.dat",
                ];

                using var repository = GetRepository(
                    [
                        .. expected,
                        $@"@@share\{c}\{a}.dat",
                        $@"@@share\{c}\{b}.dat",
                        $@"@@share\{c}\{a} {b}.dat",
                        $@"@@share\{c}\{b} {a}.dat",
                        $@"@@share\{c}\{a}\{d}.dat",
                        $@"@@share\{d}\{c}.dat",
                    ]);

                var results = repository.Search(new SearchQuery(terms: [term], exclusions: []));

                Assert.Equal(expected, results.Select(f => f.Filename));
            }

            [Fact]
            public void Does_Not_Treat_Asterisk_As_Prefix_Wildcard()
            {
                var (a, b, c, d) = (Random(), Random(), Random(), Random());

                string[] expected =
                [
                    $@"@@share\{c}\{a}.dat",
                    $@"@@share\{d}\{a}\{b}.dat",
                ];

                using var repository = GetRepository(
                    [
                        .. expected,
                        $@"@@share\{c}\{a}{b}.dat",
                        $@"@@share\{c}\{a}{d}\{b}.dat",
                        $@"@@share\{c}\{b}.dat",
                        $@"@@share\{d}\{c}.dat",
                    ]);

                var results = repository.Search(new SearchQuery(terms: [$"{a}*"], exclusions: []));

                Assert.Equal(expected.Order(StringComparer.Ordinal), results.Select(f => f.Filename));
            }

            [Fact]
            public void Returns_First_Matches_Up_To_Limit()
            {
                var (a, b, c) = (Random(), Random(), Random());

                string[] matching =
                [
                    $@"@@share\{a}\{Random()}.dat",
                    $@"@@share\{a}\{Random()}.dat",
                    $@"@@share\{a}\{Random()}.dat",
                    $@"@@share\{a}\{Random()}.dat",
                    $@"@@share\{a}\{Random()}.dat",
                ];

                using var repository = GetRepository(
                    [
                        .. matching,
                        $@"@@share\{b}\{Random()}.dat",
                        $@"@@share\{b}\{Random()}.dat",
                        $@"@@share\{c}\{Random()}.dat",
                    ]);

                var results = repository.Search(new SearchQuery(terms: [a], exclusions: []), limit: 3);

                Assert.Equal(matching.Order(StringComparer.Ordinal).Take(3), results.Select(f => f.Filename));
            }

            [Fact]
            public void Returns_Details_Of_Matching_File()
            {
                var (a, b, c) = (Random(), Random(), Random());
                var filename = $@"@@share\{a}\{b}.dat";

                using var repository = GetRepository(
                    [
                        $@"@@share\{a}\{c}.dat",
                        $@"@@share\{c}\{b}.dat",
                        $@"@@share\{c}\{a}.dat",
                    ]);

                var attributes = new List<FileAttribute>() { new(FileAttributeType.BitRate, 320), new(FileAttributeType.Length, 180) };
                repository.InsertBatch([new ScannedDirectory($@"@@share\{a}", [new ScannedFile(filename, DateTime.UtcNow, new File(1, filename, 12345, "dat", attributes))])], timestamp: 1);
                repository.RebuildFilenameIndex();

                var result = Assert.Single(repository.Search(new SearchQuery(terms: [a, b], exclusions: [])));

                Assert.Equal(filename, result.Filename);
                Assert.Equal(1, result.Code);
                Assert.Equal(12345, result.Size);
                Assert.Equal("dat", result.Extension);
                Assert.Equal(320, result.BitRate);
                Assert.Equal(180, result.Length);
            }

            [Fact]
            public void Substring_Excluded_Files_Do_Not_Count_Against_Limit()
            {
                var (a, x) = (Random(), Random());

                // the exclusion appears inside a token, so the full text index can't exclude these; they sort ahead of the
                // files that should be returned, and outnumber the limit plus over-fetch so several pages must be read
                var excluded = Enumerable.Range(0, 40).Select(i => $@"@@share\{a}\a{i:D2}q{x}q.dat").ToArray();
                var kept = Enumerable.Range(0, 10).Select(i => $@"@@share\{a}\b{i:D2}.dat").ToArray();

                using var repository = GetRepository([.. excluded, .. kept]);

                var results = repository.Search(new SearchQuery(terms: [a], exclusions: [x.ToUpperInvariant()]), limit: 3);

                Assert.Equal(kept.Order(StringComparer.Ordinal).Take(3), results.Select(f => f.Filename));
            }

            [Fact]
            public void Returns_All_Remaining_Matches_When_Exclusions_Leave_Fewer_Than_Limit()
            {
                var (a, x) = (Random(), Random());

                var excluded = Enumerable.Range(0, 20).Select(i => $@"@@share\{a}\{i:D2}q{x}q.dat").ToArray();
                var kept = Enumerable.Range(0, 3).Select(i => $@"@@share\{a}\{Random()}.dat").ToArray();

                using var repository = GetRepository([.. excluded, .. kept]);

                var results = repository.Search(new SearchQuery(terms: [a], exclusions: [x]), limit: 10);

                Assert.Equal(kept.Order(StringComparer.Ordinal), results.Select(f => f.Filename));
            }

            [Fact]
            public void Returns_Nothing_When_An_Exclusion_Is_Empty()
            {
                var a = Random();

                using var repository = GetRepository([$@"@@share\{a}\{Random()}.dat"]);

                Assert.Empty(repository.Search(new SearchQuery(terms: [a], exclusions: [string.Empty])));
            }
        }

        public class InsertBatch
        {
            [Fact]
            public void Inserts_All_Directories_And_Files()
            {
                var (a, b) = (Random(), Random());

                using var repository = GetRepository([]);

                repository.InsertBatch(
                    [
                        new ScannedDirectory($@"@@share\{a}", [Scanned($@"@@share\{a}\1.dat", size: 1), Scanned($@"@@share\{a}\2.dat", size: 2)]),
                        new ScannedDirectory($@"@@share\{b}", [Scanned($@"@@share\{b}\3.dat", size: 3)]),
                        new ScannedDirectory($@"@@share\{b}\empty", []),
                    ],
                    timestamp: 1);

                Assert.Equal(3, repository.CountDirectories());
                Assert.Equal(3, repository.CountFiles());
                Assert.Equal(($@"local\@@share\{b}\3.dat", 3L), repository.FindFileInfo($@"@@share\{b}\3.dat"));
            }

            [Fact]
            public void Updates_Existing_Records()
            {
                var a = Random();
                var (kept, removed) = ($@"@@share\{a}\kept.dat", $@"@@share\{a}\removed.dat");

                using var repository = GetRepository([]);

                repository.InsertBatch([new ScannedDirectory($@"@@share\{a}", [Scanned(kept, size: 1), Scanned(removed, size: 1)])], timestamp: 1);
                repository.InsertBatch([new ScannedDirectory($@"@@share\{a}", [Scanned(kept, size: 2)])], timestamp: 2);

                Assert.Equal(2, repository.CountFiles());
                Assert.Equal(1, repository.CountDirectories());
                Assert.Equal(2L, repository.FindFileInfo(kept).Size);

                // only the record not touched by the second batch is stale
                Assert.Equal(1, repository.PruneFiles(olderThanTimestamp: 2));
                Assert.Equal(0, repository.PruneDirectories(olderThanTimestamp: 2));
            }

            [Fact]
            public void Writes_Nothing_When_Any_Record_Fails()
            {
                var a = Random();

                using var repository = GetRepository([]);

                Assert.ThrowsAny<Exception>(() => repository.InsertBatch(
                    [
                        new ScannedDirectory($@"@@share\{a}", [Scanned($@"@@share\{a}\1.dat", size: 1)]),
                        new ScannedDirectory($@"@@share\{a}\bad", [new ScannedFile(null, DateTime.UtcNow, new File(1, $@"@@share\{a}\bad\2.dat", 2, "dat", []))]),
                    ],
                    timestamp: 1));

                Assert.Equal(0, repository.CountDirectories());
                Assert.Equal(0, repository.CountFiles());
            }

            private static ScannedFile Scanned(string maskedFilename, long size)
                => new(OriginalFilename: $@"local\{maskedFilename}", TouchedAt: DateTime.UtcNow, File: new File(1, maskedFilename, size, "dat", []));
        }

        private static string Random() => Guid.NewGuid().ToString("N");

        internal static SqliteShareRepository GetRepository(string[] maskedFilenames)
        {
            var repository = new SqliteShareRepository($"Data Source=file:shares.test_{Guid.NewGuid():N}?mode=memory;Cache=shared");
            repository.Create();

            if (maskedFilenames.Length > 0)
            {
                var files = maskedFilenames
                    .Select(maskedFilename => new ScannedFile(maskedFilename, DateTime.UtcNow, new File(1, maskedFilename, 42, "dat", new List<FileAttribute>())))
                    .ToList();

                repository.InsertBatch([new ScannedDirectory("@@share", files)], timestamp: 1);
            }

            repository.RebuildFilenameIndex();

            return repository;
        }
    }

    internal static class SqliteShareRepositoryTestExtensions
    {
        public static IReadOnlyList<File> Search(this IShareRepository repository, SearchQuery query, int? limit = null)
            => repository.Search(new ShareSearchQuery(query), limit);
    }
}
