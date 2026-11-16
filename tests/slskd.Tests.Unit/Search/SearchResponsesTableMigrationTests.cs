namespace slskd.Tests.Unit.Search;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using slskd.Migrations;
using slskd.Search;
using slskd.Search.API;
using Soulseek;
using Xunit;

using File = slskd.Search.File;
using Options = slskd.Options;
using Search = slskd.Search.Search;

public class SearchResponsesTableMigrationTests
{
    [Fact]
    public void Is_Not_Needed_For_A_Database_Created_From_The_Model()
    {
        using var db = new Db();

        Assert.False(db.Migration.NeedsToBeApplied());
    }

    [Fact]
    public void Is_Not_Needed_Without_A_Searches_Table()
    {
        using var db = new Db(create: false);

        Assert.False(db.Migration.NeedsToBeApplied());
    }

    [Fact]
    public async Task Moves_Responses_Out_Of_The_Searches_Table()
    {
        using var db = Db.Legacy();
        var first = Responses();
        var second = new List<Response> { Response("only", files: ["x"]) };

        var a = db.InsertLegacy(first);
        var b = db.InsertLegacy(second);
        var none = db.InsertLegacy([]);

        Assert.True(db.Migration.NeedsToBeApplied());

        db.Migration.Apply();

        Assert.False(db.Migration.NeedsToBeApplied());
        Assert.DoesNotContain("ResponsesJson", db.Columns("Searches"));
        Assert.Equal(first.Count + second.Count, db.Count("SELECT COUNT(*) FROM SearchResponses"));

        var service = db.CreateService();

        Assert.Equal(Json(first), Json(await service.GetResponsesAsync(a)));
        Assert.Equal(Json(second), Json(await service.GetResponsesAsync(b)));
        Assert.Empty(await service.GetResponsesAsync(none));
        Assert.Equal(3, (await service.ListAsync()).Count);
    }

    [Fact]
    public async Task Is_Idempotent()
    {
        using var db = Db.Legacy();
        var responses = Responses();
        var id = db.InsertLegacy(responses);

        db.Migration.Apply();
        db.Migration.Apply();
        new Z2026_11_16_SearchResponsesTableMigration(db.Databases).Apply();

        Assert.Equal(responses.Count, db.Count("SELECT COUNT(*) FROM SearchResponses"));
        Assert.Equal(Json(responses), Json(await db.CreateService().GetResponsesAsync(id)));
    }

    [Fact]
    public void Creates_The_Same_Table_As_The_Model()
    {
        using var expected = new Db();
        using var migrated = Db.Legacy();

        migrated.Migration.Apply();

        Assert.Equal(expected.Describe("SearchResponses"), migrated.Describe("SearchResponses"));
    }

    [Fact]
    public async Task Malformed_Responses_Are_Dropped_Without_Failing()
    {
        using var db = Db.Legacy();
        var good = db.InsertLegacy(Responses());
        var bad = db.InsertLegacy([]);
        db.SetLegacyJson(bad, "[{\"Username\": ");

        db.Migration.Apply();

        var service = db.CreateService();

        Assert.Equal(Responses().Count, (await service.GetResponsesAsync(good)).Count);
        Assert.Empty(await service.GetResponsesAsync(bad));
    }

    [Fact]
    public async Task Migrated_Responses_Are_Deleted_With_Their_Search()
    {
        using var db = Db.Legacy();
        var id = db.InsertLegacy(Responses());
        db.Migration.Apply();

        var service = db.CreateService();
        await service.DeleteAsync(await service.FindAsync(s => s.Id == id));

        Assert.Equal(0, db.Count("SELECT COUNT(*) FROM SearchResponses"));
    }

    private static string Json(IEnumerable<Response> responses) => JsonSerializer.Serialize(responses);

    private static List<Response> Responses() =>
    [
        Response("a", uploadSpeed: 10, queueLength: 3, files: ["a/1.flac", "a/2.mp3"], lockedFiles: ["a/locked.flac"]),
        Response("b", hasFreeUploadSlot: false, files: []),
        Response("ünïcödé \"quoted\"", files: ["dir\\ünïcödé ☃.flac"]),
        Response("d", files: [], lockedFiles: ["d/1", "d/2"]),
    ];

    private static Response Response(
        string username,
        int uploadSpeed = 0,
        long queueLength = 0,
        bool hasFreeUploadSlot = true,
        string[] files = null,
        string[] lockedFiles = null)
    {
        var fileList = (files ?? []).Select((f, i) => new File
        {
            Filename = f,
            Size = 1000 + i,
            Extension = "flac",
            BitRate = i == 0 ? null : 320,
            BitDepth = i == 0 ? 24 : null,
            SampleRate = i == 0 ? 96000 : null,
            IsVariableBitRate = i == 0 ? null : false,
            Length = 200,
            Code = 1,
        }).ToList();

        var lockedFileList = (lockedFiles ?? []).Select(f => new File { Filename = f, Size = 5, IsLocked = true }).ToList();

        return new Response
        {
            Username = username,
            Token = username.Length,
            UploadSpeed = uploadSpeed,
            QueueLength = queueLength,
            HasFreeUploadSlot = hasFreeUploadSlot,
            Files = fileList,
            FileCount = fileList.Count,
            LockedFiles = lockedFileList,
            LockedFileCount = lockedFileList.Count,
        };
    }

    private sealed class Db : IDisposable
    {
        private readonly SqliteConnection anchor;
        private readonly string connectionString = $"Data Source=search_migration_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

        public Db(bool create = true)
        {
            // keep the shared in-memory database alive for the life of the fixture
            anchor = new SqliteConnection(connectionString);
            anchor.Open();

            var strings = new Dictionary<Database, ConnectionString>();

            foreach (var database in Database.List)
            {
                strings[database] = connectionString;
            }

            Databases = new ConnectionStringDictionary(strings);
            Options = new DbContextOptionsBuilder<SearchDbContext>().UseSqlite(connectionString).Options;
            Migration = new Z2026_11_16_SearchResponsesTableMigration(Databases);

            if (create)
            {
                using var context = CreateContext();
                context.Database.EnsureCreated();
            }
        }

        public ConnectionStringDictionary Databases { get; }
        public DbContextOptions<SearchDbContext> Options { get; }
        public Z2026_11_16_SearchResponsesTableMigration Migration { get; }

        /// <summary>
        ///     Creates a database with the schema from before the migration: responses in Searches.ResponsesJson.
        /// </summary>
        public static Db Legacy()
        {
            var db = new Db();
            db.Execute("DROP TABLE SearchResponses;");
            db.Execute("ALTER TABLE Searches ADD COLUMN ResponsesJson TEXT NULL;");
            return db;
        }

        public Guid InsertLegacy(List<Response> responses)
        {
            var id = Guid.NewGuid();

            using (var context = CreateContext())
            {
                context.Searches.Add(new Search
                {
                    Id = id,
                    SearchText = $"search {id}",
                    StartedAt = DateTime.UtcNow,
                    EndedAt = DateTime.UtcNow,
                    State = SearchStates.Completed,
                    ResponseCount = responses.Count,
                    FileCount = responses.Sum(r => r.FileCount),
                });

                context.SaveChanges();
            }

            // serialized exactly as the Search.ResponsesJson property did
            SetLegacyJson(id, JsonSerializer.Serialize(responses));

            return id;
        }

        public void SetLegacyJson(Guid id, string json)
        {
            using var context = CreateContext();
            context.Database.ExecuteSql($"UPDATE Searches SET ResponsesJson = {json} WHERE Id = {id}");
        }

        public List<string> Columns(string table)
            => Query($"SELECT name FROM pragma_table_info('{table}');").Select(row => row[0]).ToList();

        public long Count(string sql)
        {
            using var connection = Open();
            using var command = new SqliteCommand(sql, connection);
            return (long)command.ExecuteScalar();
        }

        /// <summary>
        ///     Describes the columns, keys, foreign keys, and indexes of the specified table.
        /// </summary>
        public List<string> Describe(string table)
        {
            var description = new List<string>();

            description.AddRange(Query($"SELECT cid, name, type, \"notnull\", dflt_value, pk FROM pragma_table_info('{table}');").Select(r => "column " + string.Join('|', r)));
            description.AddRange(Query($"SELECT \"table\", \"from\", \"to\", on_update, on_delete FROM pragma_foreign_key_list('{table}');").Select(r => "fk " + string.Join('|', r)));
            description.AddRange(Query($"SELECT name, \"unique\", origin, partial FROM pragma_index_list('{table}') ORDER BY name;").Select(r => "index " + string.Join('|', r)));

            return description;
        }

        public SearchService CreateService()
        {
            var factory = new Mock<IDbContextFactory<SearchDbContext>>();
            factory.Setup(f => f.CreateDbContext()).Returns(() => CreateContext());

            var clients = new Mock<IHubClients>();
            clients.Setup(c => c.All).Returns(Mock.Of<IClientProxy>());

            var hub = new Mock<IHubContext<SearchHub>>();
            hub.Setup(h => h.Clients).Returns(clients.Object);

            return new SearchService(hub.Object, Mock.Of<IOptionsMonitor<Options>>(), Mock.Of<ISoulseekClient>(), factory.Object);
        }

        public void Dispose() => anchor.Dispose();

        private SearchDbContext CreateContext() => new(Options);

        private SqliteConnection Open()
        {
            var connection = new SqliteConnection(connectionString);
            connection.Open();
            return connection;
        }

        private void Execute(string sql)
        {
            using var connection = Open();
            using var command = new SqliteCommand(sql, connection);
            command.ExecuteNonQuery();
        }

        private List<string[]> Query(string sql)
        {
            using var connection = Open();
            using var command = new SqliteCommand(sql, connection);
            using var reader = command.ExecuteReader();

            var rows = new List<string[]>();

            while (reader.Read())
            {
                rows.Add(Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "null" : reader.GetValue(i).ToString()).ToArray());
            }

            return rows;
        }
    }
}
