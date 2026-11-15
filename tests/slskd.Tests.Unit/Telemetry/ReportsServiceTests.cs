using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using Microsoft.Data.Sqlite;
using slskd.Telemetry;
using Soulseek;
using Xunit;

namespace slskd.Tests.Unit.Telemetry;

public class ReportsServiceTests : IDisposable
{
    private readonly SqliteConnection _anchor;
    private readonly string _connectionString;

    public ReportsServiceTests()
    {
        var dbName = $"test_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        _anchor = new SqliteConnection(_connectionString);
        _anchor.Open();

        _anchor.Execute(@"
            CREATE TABLE Transfers (
                Id TEXT PRIMARY KEY,
                Username TEXT NOT NULL,
                Direction TEXT NOT NULL,
                Filename TEXT NOT NULL,
                Size INTEGER NOT NULL DEFAULT 0,
                State INTEGER NOT NULL DEFAULT 0,
                StateDescription TEXT,
                RequestedAt TEXT,
                EnqueuedAt TEXT,
                StartedAt TEXT,
                EndedAt TEXT,
                BytesTransferred INTEGER NOT NULL DEFAULT 0,
                AverageSpeed REAL NOT NULL DEFAULT 0.0,
                PlaceInQueue INTEGER,
                Exception TEXT,
                Removed INTEGER NOT NULL DEFAULT 0,
                BatchId TEXT,
                Attempts INTEGER NOT NULL DEFAULT 0,
                NextAttemptAt TEXT
            )");
    }

    public void Dispose()
    {
        _anchor.Dispose();
    }

    private ReportsService CreateService() =>
        new(new ConnectionStringDictionary(
            new Dictionary<Database, ConnectionString> { { Database.Transfers, _connectionString } }));

    private void Insert(string username, TransferDirection direction, string filename, TransferStates state, string endedAt = "2026-01-04 00:10:00", long size = 1000, double averageSpeed = 100)
    {
        _anchor.Execute(@"
            INSERT INTO Transfers (Id, Username, Direction, Filename, Size, State, StateDescription,
                RequestedAt, EnqueuedAt, StartedAt, EndedAt, BytesTransferred, AverageSpeed, Exception)
            VALUES (@Id, @Username, @Direction, @Filename, @Size, @State, @StateDescription,
                '2026-01-04 00:00:00', '2026-01-04 00:00:00', '2026-01-04 00:01:00', @EndedAt, @Size, @AverageSpeed, @Exception)",
            new
            {
                Id = Guid.NewGuid().ToString(),
                Username = username,
                Direction = direction.ToString(),
                Filename = filename,
                Size = size,
                State = (int)state,
                StateDescription = state.ToString(),
                EndedAt = endedAt,
                AverageSpeed = averageSpeed,
                Exception = state == (TransferStates.Completed | TransferStates.Succeeded) ? null : "Transfer failed: boom",
            });
    }

    [Fact]
    public void GetTransferDirectoryFrequency_Matches_Legacy_Sql_Directory_Expression()
    {
        var succeeded = TransferStates.Completed | TransferStates.Succeeded;

        string[] filenames =
        [
            @"@@abc\Music\Artist\Album\01 - Track.flac",
            @"@@abc\Music\Artist\Album\02 - Track.flac",
            "music/Artist/Album/03.mp3",
            @"mixed/sep\dir/file.ogg",
            @"double\\sep\\file.flac",
            @"trailing\dir\",
            "nosep.mp3",
            @"unicode\Björk\Homogenic\Jóga.flac",
        ];

        for (var i = 0; i < filenames.Length; i++)
        {
            Insert($"user{i % 3}", TransferDirection.Upload, filenames[i], succeeded);
        }

        var legacy = _anchor.Query<(string Directory, long Count, long DistinctUsers)>(@"
            SELECT RTRIM(RTRIM(filename, REPLACE(REPLACE(filename, '/', ''), '\', '')), '/\') AS Directory,
                COUNT(*) AS Count, COUNT(DISTINCT Username) AS DistinctUsers
            FROM Transfers
            GROUP BY Directory")
            .OrderBy(r => r.Directory, StringComparer.Ordinal)
            .ToList();

        var actual = CreateService().GetTransferDirectoryFrequency(limit: 100)
            .Select(r => (r.Directory, (long)r.Count, (long)r.DistinctUsers))
            .OrderBy(r => r.Directory, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(legacy, actual);
        Assert.Contains(actual, r => r.Directory == @"@@abc\Music\Artist\Album" && r.Item2 == 2);
        Assert.Contains(actual, r => r.Directory == string.Empty);
    }

    [Fact]
    public void GetTransferSummary_Keys_By_Direction_And_State_Without_Completed_Flag()
    {
        var succeeded = TransferStates.Completed | TransferStates.Succeeded;
        var errored = TransferStates.Completed | TransferStates.Errored;

        Insert("alice", TransferDirection.Upload, @"a\b.flac", succeeded, size: 100);
        Insert("bob", TransferDirection.Upload, @"a\c.flac", succeeded, size: 300);
        Insert("alice", TransferDirection.Upload, @"a\d.flac", errored);
        Insert("carol", TransferDirection.Download, @"x\y.flac", succeeded);

        var summary = CreateService().GetTransferSummary();

        var uploads = summary[TransferDirection.Upload];
        Assert.Equal(2, uploads[TransferStates.Succeeded].Count);
        Assert.Equal(2, uploads[TransferStates.Succeeded].DistinctUsers);
        Assert.Equal(400, uploads[TransferStates.Succeeded].TotalBytes);
        Assert.Equal(540, uploads[TransferStates.Succeeded].AverageDuration);
        Assert.Equal(60, uploads[TransferStates.Succeeded].AverageWait);
        Assert.Equal(1, uploads[TransferStates.Errored].Count);
        Assert.Equal(1, summary[TransferDirection.Download][TransferStates.Succeeded].Count);
    }

    [Fact]
    public void GetTransferLeaderboard_Counts_Only_Succeeded_Transfers_Per_User()
    {
        var succeeded = TransferStates.Completed | TransferStates.Succeeded;
        var errored = TransferStates.Completed | TransferStates.Errored;

        Insert("alice", TransferDirection.Upload, @"a\1.flac", succeeded);
        Insert("alice", TransferDirection.Upload, @"a\2.flac", succeeded);
        Insert("alice", TransferDirection.Upload, @"a\3.flac", errored);
        Insert("bob", TransferDirection.Upload, @"b\1.flac", succeeded);
        Insert("carol", TransferDirection.Download, @"c\1.flac", succeeded);

        var leaderboard = CreateService().GetTransferLeaderboard(TransferDirection.Upload);

        Assert.Equal(["alice", "bob"], leaderboard.Select(r => r.Username));
        Assert.Equal([2L, 1L], leaderboard.Select(r => (long)r.Count));
        Assert.Equal(540, leaderboard[0].AverageDuration);
    }

    [Fact]
    public void GetTransferHistogram_DropsTransfersInFirstPartialBucket_WhenStartIsNotOnUnixBucketBoundary()
    {
        // start = 2026-01-04T00:00:00Z has Unix-epoch offset mod 6048 = 4320 seconds,
        // so the first SQL bucket floor-snaps to 2026-01-03T22:33:36Z.  The C# re-snap
        // of that datetime gives 2026-01-03T21:07:12Z — a full 6048 s before the C#
        // pre-fill first key (2026-01-03T22:48:00Z) — so TryGetValue misses and the
        // transfer is silently dropped.
        var state = TransferStates.Completed | TransferStates.Succeeded;

        _anchor.Execute(@"
            INSERT INTO Transfers (Id, Username, Direction, Filename, Size, State, StateDescription,
                RequestedAt, EnqueuedAt, StartedAt, EndedAt, BytesTransferred, AverageSpeed)
            VALUES (@Id, @Username, @Direction, @Filename, @Size, @State, @StateDescription,
                @RequestedAt, @EnqueuedAt, @StartedAt, @EndedAt, @BytesTransferred, @AverageSpeed)",
            new
            {
                Id = "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
                Username = "testuser",
                Direction = TransferDirection.Upload.ToString(),
                Filename = "test.mp3",
                Size = 1_048_576L,
                State = (int)state,
                StateDescription = state.ToString(),
                RequestedAt = "2026-01-04 00:00:00",
                EnqueuedAt = "2026-01-04 00:00:00",
                StartedAt = "2026-01-04 00:01:00",
                EndedAt = "2026-01-04 00:10:00",
                BytesTransferred = 1_048_576L,
                AverageSpeed = 1748.0,
            });

        var sut = CreateService();

        var start = new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 11, 0, 0, 0, DateTimeKind.Utc);

        var histogram = sut.GetTransferHistogram(start: start, end: end, buckets: 100);

        var totalCount = histogram.Values
            .SelectMany(directions => directions.Values)
            .SelectMany(states => states.Values)
            .Sum(summary => summary.Count);

        Assert.Equal(1, totalCount);
    }
}
