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

        public TransferPage Page(TransferFilter filter, int offset, int limit)
        {
            using var context = CreateContext();
            return TransferPaging.Page(context, TransferDirection.Upload, filter, offset, limit);
        }

        public Guid Insert(
            TransferStates state,
            DateTime? requestedAt = null,
            DateTime? endedAt = null,
            bool removed = false,
            TransferDirection direction = TransferDirection.Upload)
        {
            var id = Guid.NewGuid();

            using var context = CreateContext();
            context.Transfers.Add(new Transfer
            {
                Id = id,
                Username = "user",
                Direction = direction,
                Filename = $"@@share\\dir\\{id}.flac",
                Size = 1,
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
