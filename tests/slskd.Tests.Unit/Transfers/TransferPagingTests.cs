namespace slskd.Tests.Unit.Transfers;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using slskd.Transfers;
using Soulseek;
using Xunit;

using Transfer = slskd.Transfers.Transfer;

public class TransferPagingTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private const TransferStates Succeeded = TransferStates.Completed | TransferStates.Succeeded;
    private const TransferStates Errored = TransferStates.Completed | TransferStates.Errored;
    private const TransferStates Cancelled = TransferStates.Completed | TransferStates.Cancelled;
    private const TransferStates QueuedLocally = TransferStates.Queued | TransferStates.Locally;

    [Theory]
    [InlineData(null, TransferFilter.All)]
    [InlineData("", TransferFilter.All)]
    [InlineData("all", TransferFilter.All)]
    [InlineData("ACTIVE", TransferFilter.Active)]
    [InlineData("Completed", TransferFilter.Completed)]
    [InlineData("succeeded", TransferFilter.Succeeded)]
    [InlineData("errored", TransferFilter.Errored)]
    [InlineData("cancelled", TransferFilter.Cancelled)]
    public void TryParseFilter_Parses_Known_Values_Ignoring_Case(string value, TransferFilter expected)
    {
        Assert.True(TransferPaging.TryParseFilter(value, out var filter));
        Assert.Equal(expected, filter);
    }

    [Theory]
    [InlineData("bogus")]
    [InlineData("1")]
    [InlineData("all,active")]
    public void TryParseFilter_Rejects_Unknown_Values(string value)
    {
        Assert.False(TransferPaging.TryParseFilter(value, out _));
    }

    [Theory]
    [InlineData(null, TransferCancelFilter.All)]
    [InlineData("all", TransferCancelFilter.All)]
    [InlineData("Queued", TransferCancelFilter.Queued)]
    [InlineData("INPROGRESS", TransferCancelFilter.InProgress)]
    public void TryParseCancelFilter_Parses_Known_Values_Ignoring_Case(string value, TransferCancelFilter expected)
    {
        Assert.True(TransferPaging.TryParseCancelFilter(value, out var filter));
        Assert.Equal(expected, filter);
    }

    [Theory]
    [InlineData("completed")]
    [InlineData("2")]
    public void TryParseCancelFilter_Rejects_Unknown_Values(string value)
    {
        Assert.False(TransferPaging.TryParseCancelFilter(value, out _));
    }

    [Fact]
    public void CountByState_Counts_NonRemoved_Transfers_In_Direction()
    {
        using var db = new Db();
        db.Insert(Succeeded, endedAt: T0);
        db.Insert(Succeeded, endedAt: T0);
        db.Insert(Errored, endedAt: T0);
        db.Insert(QueuedLocally);
        db.Insert(Succeeded, endedAt: T0, removed: true);
        db.Insert(Succeeded, endedAt: T0, direction: TransferDirection.Download);

        using var context = db.CreateContext();
        var counts = TransferPaging.CountByState(context, TransferDirection.Upload);

        Assert.Equal(3, counts.Count);
        Assert.Equal(2, counts[Succeeded]);
        Assert.Equal(1, counts[Errored]);
        Assert.Equal(1, counts[QueuedLocally]);
    }

    [Fact]
    public void Page_Orders_Active_By_RequestedAt_Then_Completed_By_EndedAt_Descending()
    {
        using var db = new Db();
        var (a1, a2, c1, c2, c3) = db.InsertStandardSet();

        var page = db.Page(TransferFilter.All, offset: 0, limit: 100);

        Assert.Equal(new[] { a2, a1, c2, c1, c3 }, page.Transfers.Select(t => t.Id));
        Assert.Equal(5, page.Total);
    }

    [Fact]
    public void Page_Excludes_Removed_And_Other_Direction()
    {
        using var db = new Db();
        db.InsertStandardSet();
        db.Insert(Succeeded, endedAt: T0.AddDays(1), removed: true);
        db.Insert(QueuedLocally, removed: true);
        db.Insert(Succeeded, endedAt: T0.AddDays(1), direction: TransferDirection.Download);

        var page = db.Page(TransferFilter.All, offset: 0, limit: 100);

        Assert.Equal(5, page.Total);
        Assert.Equal(5, page.Transfers.Count);
        Assert.All(page.Transfers, t => Assert.False(t.Removed));
        Assert.All(page.Transfers, t => Assert.Equal(TransferDirection.Upload, t.Direction));
    }

    [Theory]
    [InlineData(0, 2, new[] { 1, 0 })]
    [InlineData(1, 2, new[] { 0, 3 })]
    [InlineData(2, 2, new[] { 3, 2 })]
    [InlineData(3, 10, new[] { 2, 4 })]
    [InlineData(4, 1, new[] { 4 })]
    [InlineData(5, 10, new int[0])]
    [InlineData(500, 10, new int[0])]
    public void Page_Offset_And_Limit_Span_Active_Completed_Boundary(int offset, int limit, int[] expectedIndexes)
    {
        using var db = new Db();
        var (a1, a2, c1, c2, c3) = db.InsertStandardSet();
        var ids = new[] { a1, a2, c1, c2, c3 };

        var page = db.Page(TransferFilter.All, offset, limit);

        Assert.Equal(expectedIndexes.Select(i => ids[i]), page.Transfers.Select(t => t.Id));
        Assert.Equal(5, page.Total);
    }

    [Fact]
    public void Page_Pages_Through_Every_Transfer_Exactly_Once()
    {
        using var db = new Db();
        var expected = new List<Guid>();

        for (int i = 0; i < 7; i++)
        {
            expected.Add(db.Insert(QueuedLocally, requestedAt: T0.AddMinutes(i)));
        }

        for (int i = 0; i < 13; i++)
        {
            expected.Add(db.Insert(i % 2 == 0 ? Succeeded : Errored, endedAt: T0.AddHours(100 - i)));
        }

        var actual = new List<Guid>();

        for (int offset = 0; offset < 20; offset += 3)
        {
            actual.AddRange(db.Page(TransferFilter.All, offset, 3).Transfers.Select(t => t.Id));
        }

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Page_Breaks_EndedAt_Ties_By_Id_Descending()
    {
        using var db = new Db();
        var ids = Enumerable.Range(0, 5).Select(_ => db.Insert(Succeeded, endedAt: T0)).ToList();

        var page = db.Page(TransferFilter.All, offset: 0, limit: 100);

        Assert.Equal(ids.OrderByDescending(id => id.ToString().ToUpperInvariant(), StringComparer.Ordinal), page.Transfers.Select(t => t.Id));
    }

    [Theory]
    [InlineData(TransferFilter.All, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 })]
    [InlineData(TransferFilter.Active, new[] { 0, 1, 2, 3, 4, 5, 6 })]
    [InlineData(TransferFilter.Completed, new[] { 7, 8, 9, 10, 11, 12 })]
    [InlineData(TransferFilter.Succeeded, new[] { 7 })]
    [InlineData(TransferFilter.Errored, new[] { 9, 10, 11, 12 })]
    [InlineData(TransferFilter.Cancelled, new[] { 8 })]
    public void Page_Filter_Selects_Expected_States(TransferFilter filter, int[] expectedIndexes)
    {
        // one transfer per state, inserted so the expected order is the insertion order
        var states = new[]
        {
            TransferStates.None,
            TransferStates.Requested,
            TransferStates.Queued,
            QueuedLocally,
            TransferStates.Queued | TransferStates.Remotely,
            TransferStates.Initializing,
            TransferStates.InProgress,
            Succeeded,
            Cancelled,
            TransferStates.Completed | TransferStates.TimedOut,
            Errored,
            TransferStates.Completed | TransferStates.Rejected,
            TransferStates.Completed | TransferStates.Aborted,
        };

        using var db = new Db();
        var ids = states
            .Select((s, i) => s.HasFlag(TransferStates.Completed)
                ? db.Insert(s, endedAt: T0.AddHours(-i))
                : db.Insert(s, requestedAt: T0.AddMinutes(i)))
            .ToList();

        var page = db.Page(filter, offset: 0, limit: 100);

        Assert.Equal(expectedIndexes.Select(i => ids[i]), page.Transfers.Select(t => t.Id));
        Assert.Equal(expectedIndexes.Length, page.Total);

        // counts always cover every state, regardless of filter
        Assert.Equal(states.Length, page.Counts.Count);
        Assert.All(states, s => Assert.Equal(1, page.Counts[s]));
    }

    [Fact]
    public void Page_Total_Reflects_Filter_Not_Page_Size()
    {
        using var db = new Db();
        db.InsertStandardSet();

        var page = db.Page(TransferFilter.Completed, offset: 0, limit: 1);

        Assert.Single(page.Transfers);
        Assert.Equal(3, page.Total);
    }

    [Fact]
    public void Page_Returns_Empty_Page_For_Empty_Table()
    {
        using var db = new Db();

        var page = db.Page(TransferFilter.All, offset: 0, limit: 500);

        Assert.Empty(page.Transfers);
        Assert.Empty(page.Counts);
        Assert.Equal(0, page.Total);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    public void Page_Throws_On_Invalid_Offset_Or_Limit(int offset, int limit)
    {
        using var db = new Db();

        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => db.Page(TransferFilter.All, offset, limit));
    }

    [Theory]
    [InlineData(TransferCancelFilter.All, new[] { 0, 1, 2, 3, 4, 5, 6 })]
    [InlineData(TransferCancelFilter.Queued, new[] { 1, 2, 3, 4 })]
    [InlineData(TransferCancelFilter.InProgress, new[] { 5, 6 })]
    public void ListActive_Selects_Expected_States(TransferCancelFilter filter, int[] expectedIndexes)
    {
        var states = new[]
        {
            TransferStates.None,
            TransferStates.Requested,
            TransferStates.Queued,
            QueuedLocally,
            TransferStates.Queued | TransferStates.Remotely,
            TransferStates.Initializing,
            TransferStates.InProgress,
        };

        using var db = new Db();
        var ids = states.Select((s, i) => db.Insert(s, requestedAt: T0.AddMinutes(i))).ToList();

        // none of these should ever match
        db.Insert(Succeeded, endedAt: T0);
        db.Insert(Cancelled, endedAt: T0);
        db.Insert(QueuedLocally, removed: true);
        db.Insert(TransferStates.InProgress, removed: true);
        db.Insert(QueuedLocally, direction: TransferDirection.Download);
        db.Insert(TransferStates.InProgress, direction: TransferDirection.Download);

        using var context = db.CreateContext();
        var active = TransferPaging.ListActive(context, TransferDirection.Upload, filter);

        Assert.Equal(expectedIndexes.Select(i => ids[i]), active.Select(t => t.Id));
    }

    [Fact]
    public void ListActive_Returns_Empty_When_Nothing_Is_Active()
    {
        using var db = new Db();
        db.Insert(Succeeded, endedAt: T0);

        using var context = db.CreateContext();

        Assert.Empty(TransferPaging.ListActive(context, TransferDirection.Upload, TransferCancelFilter.All));
    }

    [Theory]
    [InlineData(TransferFilter.All, 7)]
    [InlineData(TransferFilter.Completed, 7)]
    [InlineData(TransferFilter.Succeeded, 1)]
    [InlineData(TransferFilter.Errored, 4)]
    [InlineData(TransferFilter.Cancelled, 1)]
    [InlineData(TransferFilter.Active, 0)]
    public void CompletedStates_Contains_Only_Completed_States(TransferFilter filter, int expectedCount)
    {
        var states = TransferPaging.CompletedStates(filter);

        Assert.Equal(expectedCount, states.Count);
        Assert.All(states, s => Assert.True(s.HasFlag(TransferStates.Completed)));
        Assert.All(states, s => Assert.Contains(s, TransferStateCategories.Completed));
    }

    [Fact]
    public void Page_Narrows_Rows_And_Counts_To_Exact_Username()
    {
        using var db = new Db();
        var bob = db.Insert(Succeeded, endedAt: T0, username: "bob");
        var bobQueued = db.Insert(QueuedLocally, username: "bob");
        db.Insert(Succeeded, endedAt: T0, username: "Bob");
        db.Insert(Succeeded, endedAt: T0, username: "bobby");
        db.Insert(Succeeded, endedAt: T0, username: "bob", direction: TransferDirection.Download);

        var page = db.Page(TransferFilter.All, 0, 100, new TransferQuery { Username = "bob" });

        Assert.Equal(2, page.Total);
        Assert.Equal(new[] { bobQueued, bob }, page.Transfers.Select(t => t.Id));
        Assert.Equal(1, page.Counts[Succeeded]);
        Assert.Equal(1, page.Counts[QueuedLocally]);
    }

    [Theory]
    [InlineData("AlBuM", 2)] // case-insensitive, matches the directory of both files
    [InlineData("carol", 1)] // matches the username
    [InlineData("100%", 1)] // '%' is literal, not a wildcard
    [InlineData("a_b", 1)] // '_' is literal, not a wildcard
    [InlineData("x!y", 1)] // the escape character is literal too
    [InlineData("\\Album\\", 2)] // path separators match literally
    [InlineData("nothing", 0)]
    public void Page_Search_Matches_Filename_Or_Username_Literally_Ignoring_Case(string search, int expected)
    {
        using var db = new Db();
        db.Insert(Succeeded, endedAt: T0, username: "alice", filename: "@@m\\Album\\100% a_b.flac");
        db.Insert(Succeeded, endedAt: T0, username: "alice", filename: "@@m\\Album\\x!y.flac");
        db.Insert(Succeeded, endedAt: T0, username: "carol", filename: "@@m\\Other\\100 axb.flac");

        var page = db.Page(TransferFilter.All, 0, 100, new TransferQuery { Search = search });

        Assert.Equal(expected, page.Total);
        Assert.Equal(expected, page.Transfers.Count);
        Assert.Equal(expected, page.Counts.Values.Sum());
    }

    [Fact]
    public void Summarize_By_Directory_Counts_Distinct_Users_And_Sorts_By_Them()
    {
        using var db = new Db();

        // popular album: three users, one of them twice; a removed row and a download are ignored
        db.Insert(Succeeded, endedAt: T0, username: "a", filename: "@@m\\Pop\\1.flac", bytesTransferred: 10);
        db.Insert(Succeeded, endedAt: T0.AddHours(1), username: "a", filename: "@@m\\Pop\\2.flac", bytesTransferred: 10);
        db.Insert(Errored, endedAt: T0, username: "b", filename: "@@m\\Pop\\1.flac", bytesTransferred: 5);
        db.Insert(QueuedLocally, requestedAt: T0.AddHours(2), username: "c", filename: "@@m\\Pop\\3.flac");
        db.Insert(Succeeded, endedAt: T0, username: "d", filename: "@@m\\Pop\\1.flac", removed: true);
        db.Insert(Succeeded, endedAt: T0, username: "e", filename: "@@m\\Pop\\1.flac", direction: TransferDirection.Download);

        // niche album: one user, many files
        db.Insert(Succeeded, endedAt: T0, username: "z", filename: "@@m\\Niche\\1.flac", bytesTransferred: 100);
        db.Insert(Succeeded, endedAt: T0, username: "z", filename: "@@m\\Niche\\2.flac", bytesTransferred: 100);
        db.Insert(Succeeded, endedAt: T0, username: "z", filename: "@@m\\Niche\\3.flac", bytesTransferred: 100);
        db.Insert(Succeeded, endedAt: T0, username: "z", filename: "@@m\\Niche\\4.flac", bytesTransferred: 100);
        db.Insert(Succeeded, endedAt: T0, username: "z", filename: "@@m\\Niche\\5.flac", bytesTransferred: 100);

        var summary = db.Summarize(TransferSummaryGrouping.Directory);

        Assert.Equal(2, summary.Groups);
        Assert.Equal(9, summary.Transfers);
        Assert.Equal(new[] { "@@m\\Pop", "@@m\\Niche" }, summary.Rows.Select(r => r.Key));

        var pop = summary.Rows[0];
        Assert.Equal(3, pop.Users);
        Assert.Equal(4, pop.Transfers);
        Assert.Equal(2, pop.Succeeded);
        Assert.Equal(25, pop.Bytes);
        Assert.Equal(T0.AddHours(2), pop.LastActivityAt);
        Assert.Equal(DateTimeKind.Utc, pop.LastActivityAt.Kind);

        Assert.Equal(new[] { "@@m\\Niche", "@@m\\Pop" }, db.Summarize(TransferSummaryGrouping.Directory, TransferSummarySort.Transfers).Rows.Select(r => r.Key));
        Assert.Equal(new[] { "@@m\\Niche", "@@m\\Pop" }, db.Summarize(TransferSummaryGrouping.Directory, TransferSummarySort.Bytes).Rows.Select(r => r.Key));
        Assert.Equal(new[] { "@@m\\Pop", "@@m\\Niche" }, db.Summarize(TransferSummaryGrouping.Directory, TransferSummarySort.Recent).Rows.Select(r => r.Key));
    }

    [Fact]
    public void Summarize_Applies_Filter_Query_And_Limit()
    {
        using var db = new Db();
        db.Insert(Succeeded, endedAt: T0, username: "a", filename: "@@m\\Pop\\1.flac");
        db.Insert(Succeeded, endedAt: T0, username: "b", filename: "@@m\\Pop\\1.flac");
        db.Insert(Errored, endedAt: T0, username: "c", filename: "@@m\\Pop\\1.flac");
        db.Insert(QueuedLocally, username: "a", filename: "@@m\\Pop\\2.flac");
        db.Insert(Succeeded, endedAt: T0, username: "a", filename: "@@m\\Rock\\1.mp3");

        var succeeded = db.Summarize(TransferSummaryGrouping.File, filter: TransferFilter.Succeeded);
        Assert.Equal(new[] { ("@@m\\Pop\\1.flac", 2), ("@@m\\Rock\\1.mp3", 1) }, succeeded.Rows.Select(r => (r.Key, r.Users)));

        var active = db.Summarize(TransferSummaryGrouping.File, filter: TransferFilter.Active);
        Assert.Equal(new[] { "@@m\\Pop\\2.flac" }, active.Rows.Select(r => r.Key));

        var userA = db.Summarize(TransferSummaryGrouping.Directory, query: new TransferQuery { Username = "a" });
        Assert.Equal(new[] { ("@@m\\Pop", 2), ("@@m\\Rock", 1) }, userA.Rows.Select(r => (r.Key, r.Transfers)));

        var flac = db.Summarize(TransferSummaryGrouping.User, TransferSummarySort.Transfers, query: new TransferQuery { Search = ".FLAC" }, limit: 1);
        Assert.Equal(3, flac.Groups);
        Assert.Equal(4, flac.Transfers);
        Assert.Equal(("a", 2), (flac.Rows.Single().Key, flac.Rows.Single().Transfers));
    }

    [Fact]
    public void Summarize_Returns_Empty_When_Nothing_Matches()
    {
        using var db = new Db();

        var summary = db.Summarize(TransferSummaryGrouping.User);

        Assert.Equal(0, summary.Groups);
        Assert.Empty(summary.Rows);
    }

    [Theory]
    [InlineData(null, TransferSummaryGrouping.Directory, true)]
    [InlineData("USER", TransferSummaryGrouping.User, true)]
    [InlineData(" file ", TransferSummaryGrouping.File, true)]
    [InlineData("0", TransferSummaryGrouping.Directory, false)]
    [InlineData("user,file", TransferSummaryGrouping.Directory, false)]
    public void TryParseName_Accepts_Only_Member_Names(string value, TransferSummaryGrouping expected, bool parsed)
    {
        Assert.Equal(parsed, TransferPaging.TryParseName(value, TransferSummaryGrouping.Directory, out var result));

        if (parsed)
        {
            Assert.Equal(expected, result);
        }
    }

    private sealed class Db : IDisposable
    {
        private readonly SqliteConnection _anchor;
        private readonly DbContextOptions<TransfersDbContext> _options;

        public Db()
        {
            var connectionString = $"Data Source=paging_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

            // keep the shared-cache in-memory database alive for the life of this fixture
            _anchor = new SqliteConnection(connectionString);
            _anchor.Open();

            _options = new DbContextOptionsBuilder<TransfersDbContext>()
                .UseSqlite(connectionString)
                .Options;

            // derive the schema (including indexes) from the EF model
            using var context = CreateContext();
            context.Database.EnsureCreated();
        }

        public TransfersDbContext CreateContext() => new(_options);

        public TransferPage Page(TransferFilter filter, int offset, int limit, TransferQuery query = null)
        {
            using var context = CreateContext();
            return TransferPaging.Page(context, TransferDirection.Upload, filter, query, offset, limit);
        }

        public TransferSummaryResult Summarize(
            TransferSummaryGrouping grouping,
            TransferSummarySort sort = TransferSummarySort.Users,
            TransferFilter filter = TransferFilter.All,
            TransferQuery query = null,
            int limit = 100)
        {
            using var context = CreateContext();
            return TransferPaging.Summarize(context, TransferDirection.Upload, filter, query, grouping, sort, limit);
        }

        public Guid Insert(
            TransferStates state,
            DateTime? requestedAt = null,
            DateTime? endedAt = null,
            bool removed = false,
            TransferDirection direction = TransferDirection.Upload,
            string username = "user",
            string filename = null,
            long bytesTransferred = 0)
        {
            var id = Guid.NewGuid();

            using var context = CreateContext();
            context.Transfers.Add(new Transfer
            {
                Id = id,
                Username = username,
                Direction = direction,
                Filename = filename ?? $"@@share\\dir\\{id}.flac",
                Size = 1,
                BytesTransferred = bytesTransferred,
                State = state,
                RequestedAt = requestedAt ?? T0,
                EndedAt = endedAt,
                Removed = removed,
            });
            context.SaveChanges();

            return id;
        }

        /// <summary>
        ///     Inserts two active and three completed uploads; expected order is a2, a1, c2, c1, c3.
        /// </summary>
        public (Guid A1, Guid A2, Guid C1, Guid C2, Guid C3) InsertStandardSet()
        {
            var a1 = Insert(QueuedLocally, requestedAt: T0.AddMinutes(2));
            var a2 = Insert(TransferStates.InProgress, requestedAt: T0.AddMinutes(1));
            var c1 = Insert(Succeeded, endedAt: T0.AddMinutes(10));
            var c2 = Insert(Errored, endedAt: T0.AddMinutes(20));
            var c3 = Insert(Cancelled, endedAt: T0.AddMinutes(5));

            return (a1, a2, c1, c2, c3);
        }

        public void Dispose() => _anchor.Dispose();
    }
}
