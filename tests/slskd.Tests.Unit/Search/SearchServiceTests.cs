namespace slskd.Tests.Unit.Search;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Moq;
using slskd.Search;
using slskd.Search.API;
using Soulseek;
using Xunit;

using File = slskd.Search.File;
using Options = slskd.Options;
using Search = slskd.Search.Search;

public class SearchServiceTests
{
    private static readonly SearchStates Completed = SearchStates.Completed | SearchStates.ResponseLimitReached;

    [Fact]
    public async Task GetResponsesAsync_Returns_Null_For_Unknown_Search()
    {
        using var fixture = new Fixture();

        Assert.Null(await fixture.Service.GetResponsesAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetResponsesAsync_Reads_A_Completed_Search_Once()
    {
        using var fixture = new Fixture();
        var id = fixture.Insert(Completed, Responses("a", "b"));

        var first = await fixture.Service.GetResponsesAsync(id);

        Assert.Equal(new[] { "a", "b" }, first.Select(r => r.Username));
        Assert.Equal("a/file", first[0].Files.Single().Filename);

        // change the stored responses behind the service's back; the cached copy is served
        fixture.DeleteResponses(id);

        var second = await fixture.Service.GetResponsesAsync(id);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task GetResponsesAsync_Does_Not_Cache_Searches_That_Are_Not_Complete()
    {
        using var fixture = new Fixture();
        var id = fixture.Insert(SearchStates.InProgress, []);

        Assert.Empty(await fixture.Service.GetResponsesAsync(id));

        fixture.Complete(id, Responses("a"));

        Assert.Equal(new[] { "a" }, (await fixture.Service.GetResponsesAsync(id)).Select(r => r.Username));
    }

    [Fact]
    public async Task FindAsync_Does_Not_Read_Responses_Unless_Asked_To()
    {
        using var fixture = new Fixture();
        var id = fixture.Insert(Completed, Responses("a", "b"));
        fixture.Commands.Clear();

        var search = await fixture.Service.FindAsync(s => s.Id == id);

        Assert.Equal(id, search.Id);
        Assert.Empty(search.Responses);
        Assert.NotEmpty(fixture.Commands);
        Assert.DoesNotContain(fixture.Commands, sql => sql.Contains("SearchResponses", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FindAsync_Includes_Responses_From_The_Cache()
    {
        using var fixture = new Fixture();
        var id = fixture.Insert(Completed, Responses("a", "b"));

        var first = await fixture.Service.FindAsync(s => s.Id == id, includeResponses: true);

        Assert.Equal(new[] { "a", "b" }, first.Responses.Select(r => r.Username));

        // served from the cache, rather than read again
        fixture.DeleteResponses(id);

        var second = await fixture.Service.FindAsync(s => s.Id == id, includeResponses: true);

        Assert.Same(first.Responses, second.Responses);
    }

    [Fact]
    public async Task ListAsync_Does_Not_Read_Responses()
    {
        using var fixture = new Fixture();
        fixture.Insert(Completed, Responses("a", "b"));
        fixture.Commands.Clear();

        var searches = await fixture.Service.ListAsync();

        Assert.Empty(searches.Single().Responses);
        Assert.DoesNotContain(fixture.Commands, sql => sql.Contains("SearchResponses", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DeleteAsync_Deletes_Responses_And_Evicts_Cached_Responses()
    {
        using var fixture = new Fixture();
        var id = fixture.Insert(Completed, Responses("a"));
        var other = fixture.Insert(Completed, Responses("b", "c"));
        await fixture.Service.GetResponsesAsync(id);

        await fixture.Service.DeleteAsync(await fixture.Service.FindAsync(s => s.Id == id));

        Assert.Null(await fixture.Service.GetResponsesAsync(id));
        Assert.Null(await fixture.Service.FindAsync(s => s.Id == id));
        Assert.Equal(0, fixture.CountResponses(id));
        Assert.Equal(2, fixture.CountResponses(other));
        fixture.VerifyBroadcast(SearchHubMethods.Delete, Times.Once());
    }

    [Fact]
    public async Task PruneAsync_Deletes_Expired_Searches_And_Their_Responses_With_A_Single_Broadcast()
    {
        using var fixture = new Fixture();
        var old1 = fixture.Insert(Completed, Responses("a"), endedAt: DateTime.UtcNow.AddHours(-3));
        var old2 = fixture.Insert(Completed, Responses("b"), endedAt: DateTime.UtcNow.AddHours(-2));
        var recent = fixture.Insert(Completed, Responses("c"), endedAt: DateTime.UtcNow.AddMinutes(-5));
        var active = fixture.Insert(SearchStates.InProgress, [], endedAt: null);

        await fixture.Service.GetResponsesAsync(old1);

        var pruned = await fixture.Service.PruneAsync(age: 60);

        Assert.Equal(2, pruned);

        var remaining = await fixture.Service.ListAsync();
        Assert.Equal(new[] { recent, active }.OrderBy(i => i), remaining.Select(s => s.Id).OrderBy(i => i));
        Assert.Null(await fixture.Service.GetResponsesAsync(old1));
        Assert.Equal(0, fixture.CountResponses(old1));
        Assert.Equal(0, fixture.CountResponses(old2));
        Assert.Equal(1, fixture.CountResponses(recent));

        fixture.HubClients.Verify(
            c => c.SendCoreAsync(
                SearchHubMethods.DeleteMany,
                It.Is<object[]>(args => args.Length == 1
                    && ((IEnumerable<Guid>)args[0]).OrderBy(i => i).SequenceEqual(new[] { old1, old2 }.OrderBy(i => i))),
                It.IsAny<CancellationToken>()),
            Times.Once);

        fixture.VerifyBroadcast(SearchHubMethods.Delete, Times.Never());
    }

    [Fact]
    public async Task PruneAsync_Does_Not_Broadcast_When_Nothing_Expired()
    {
        using var fixture = new Fixture();
        fixture.Insert(Completed, Responses("a"), endedAt: DateTime.UtcNow);

        Assert.Equal(0, await fixture.Service.PruneAsync(age: 60));

        fixture.VerifyBroadcast(SearchHubMethods.DeleteMany, Times.Never());
    }

    [Fact]
    public async Task GetResponsePageAsync_Returns_Null_For_Unknown_Search()
    {
        using var fixture = new Fixture();

        Assert.Null(await fixture.Service.GetResponsePageAsync(Guid.NewGuid(), new ResponseQuery()));
    }

    public static IEnumerable<object[]> Queries()
    {
        string[] filters = [null, "", "flac", "-mp3", "FLAC -live", "islossless", "islossy", "iscbr", "isvbr", "minbr:256", "minbd:24", "minfs:5000", "minlen:100", "minfif:3", "flac minfif:2", "-"];

        foreach (var filter in filters)
        {
            foreach (var sortBy in new[] { ResponseSortOrder.UploadSpeed, ResponseSortOrder.QueueLength })
            {
                foreach (var hideLocked in new[] { false, true })
                {
                    yield return [filter, sortBy, hideLocked];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Queries))]
    public async Task GetResponsePageAsync_Pages_Large_Searches_In_The_Database_Exactly_As_In_Memory(string filter, ResponseSortOrder sortBy, bool hideLocked)
    {
        using var fixture = new Fixture();
        var responses = PagingFixture();

        // claim more files than fit in the cache, so the page is selected by the database
        var id = fixture.Insert(Completed, responses, fileCount: 10_000_000);

        foreach (var hideNoFreeSlots in new[] { false, true })
        {
            foreach (var hidden in new[] { Array.Empty<string>(), ["user3", "user7", "nobody", "user3"] })
            {
                foreach (var (offset, limit) in new[] { (0, 100), (0, 7), (7, 7), (14, 7), (35, 5), (1000, 10), (0, 0) })
                {
                    var query = new ResponseQuery
                    {
                        Filter = filter,
                        SortBy = sortBy,
                        HideLocked = hideLocked,
                        HideNoFreeSlots = hideNoFreeSlots,
                        HiddenUsernames = hidden,
                        Offset = offset,
                        Limit = limit,
                    };

                    var expected = ResponsePaging.Page(responses, query);
                    var actual = await fixture.Service.GetResponsePageAsync(id, query);

                    Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
                }
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("flac minbr:256")]
    public async Task GetResponsePageAsync_Pages_Small_Searches_Exactly_As_In_Memory(string filter)
    {
        using var fixture = new Fixture();
        var responses = PagingFixture();
        var id = fixture.Insert(Completed, responses);

        var query = new ResponseQuery { Filter = filter, Offset = 3, Limit = 5 };

        var expected = ResponsePaging.Page(responses, query);
        var actual = await fixture.Service.GetResponsePageAsync(id, query);

        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    [Fact]
    public async Task GetResponsePageAsync_Remembers_The_Matches_Of_Large_Searches()
    {
        using var fixture = new Fixture();
        var responses = PagingFixture();
        var id = fixture.Insert(Completed, responses, fileCount: 10_000_000);

        var first = await fixture.Service.GetResponsePageAsync(id, new ResponseQuery { Filter = "flac", Limit = 5 });
        fixture.Commands.Clear();

        var second = await fixture.Service.GetResponsePageAsync(id, new ResponseQuery { Filter = "flac", Offset = 5, Limit = 5 });

        // only the responses in the page are read; there's no scan through all of them (the only ordered query)
        Assert.DoesNotContain(fixture.Commands, sql => sql.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(first.MatchedCount, second.MatchedCount);
        Assert.Equal(
            JsonSerializer.Serialize(ResponsePaging.Page(responses, new ResponseQuery { Filter = "flac", Offset = 5, Limit = 5 })),
            JsonSerializer.Serialize(second));
    }

    [Fact]
    public async Task GetResponsePageAsync_Selects_Unfiltered_Pages_Of_Large_Searches_Without_Reading_Every_Response()
    {
        using var fixture = new Fixture();
        var id = fixture.Insert(Completed, PagingFixture(), fileCount: 10_000_000);
        fixture.Commands.Clear();

        var page = await fixture.Service.GetResponsePageAsync(id, new ResponseQuery { Offset = 10, Limit = 5 });

        Assert.Equal(5, page.Responses.Count);

        // files are only read for the responses in the page (counting refers to them, but doesn't read them)
        var reads = fixture.Commands
            .Where(sql => sql.Contains("FilesJson", StringComparison.Ordinal) && !sql.TrimStart().StartsWith("SELECT COUNT", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.NotEmpty(reads);
        Assert.All(reads, sql => Assert.Contains("OFFSET", sql, StringComparison.OrdinalIgnoreCase));
    }

    private static List<Response> Responses(params string[] usernames)
        => usernames.Select(u => new Response
        {
            Username = u,
            FileCount = 1,
            Files = [new File { Filename = $"{u}/file", Size = 1 }],
        }).ToList();

    /// <summary>
    ///     Responses that exercise every part of a query: ties in both sort orders, users without free slots, locked files,
    ///     responses without files, and files with every kind of attribute (including missing and nonsensical ones).
    /// </summary>
    private static List<Response> PagingFixture()
    {
        var names = new[] { "Song.flac", "song.mp3", "Live Song.FLAC", "track.ogg", "other.flac" };

        return Enumerable.Range(0, 40).Select(i =>
        {
            var files = Enumerable.Range(0, i % 5).Select(j => new File
            {
                Filename = $"user{i}\\album\\{j} {names[(i + j) % names.Length]}",
                Size = (i * 977 + j * 131) % 10_000,
                BitRate = (i + j) % 4 == 0 ? null : ((i + j) % 4 == 3 ? -1 : 128 * ((i + j) % 4)),
                BitDepth = (i + j) % 3 == 0 ? 24 : (i % 2 == 0 ? null : 16),
                SampleRate = (i + j) % 3 == 0 ? 96000 : null,
                IsVariableBitRate = (i + j) % 3 == 1 ? true : ((i + j) % 3 == 2 ? false : null),
                Length = (i + j) % 6 == 0 ? null : 30 * j,
                Extension = "x",
            }).ToList();

            var lockedFiles = Enumerable.Range(0, i % 3 == 0 ? 2 : 0).Select(j => new File
            {
                Filename = $"user{i}\\locked\\{j} {names[j]}",
                Size = 6000,
                BitRate = 320,
                IsLocked = true,
            }).ToList();

            return new Response
            {
                Username = $"user{i % 30}",
                Token = i,
                UploadSpeed = (i * 7) % 5,
                QueueLength = (i * 3) % 4,
                HasFreeUploadSlot = i % 4 != 1,
                Files = files,
                FileCount = files.Count,
                LockedFiles = lockedFiles,
                LockedFileCount = lockedFiles.Count,
            };
        }).ToList();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly SqliteConnection anchor;
        private readonly DbContextOptions<SearchDbContext> options;

        public Fixture()
        {
            var connectionString = $"Data Source=searches_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

            // keep the shared-cache in-memory database alive for the life of this fixture
            anchor = new SqliteConnection(connectionString);
            anchor.Open();

            options = new DbContextOptionsBuilder<SearchDbContext>()
                .UseSqlite(connectionString)
                .AddInterceptors(new CommandRecorder(Commands))
                .Options;

            using (var context = CreateContext())
            {
                context.Database.EnsureCreated();
            }

            var factory = new Mock<IDbContextFactory<SearchDbContext>>();
            factory.Setup(f => f.CreateDbContext()).Returns(() => CreateContext());

            var clients = new Mock<IHubClients>();
            clients.Setup(c => c.All).Returns(HubClients.Object);

            var hub = new Mock<IHubContext<SearchHub>>();
            hub.Setup(h => h.Clients).Returns(clients.Object);

            Service = new SearchService(
                hub.Object,
                Mock.Of<IOptionsMonitor<Options>>(),
                Mock.Of<ISoulseekClient>(),
                factory.Object);
        }

        public ConcurrentQueue<string> Commands { get; } = new();
        public Mock<IClientProxy> HubClients { get; } = new();
        public SearchService Service { get; }

        public Guid Insert(SearchStates state, List<Response> responses, DateTime? endedAt = default, int? fileCount = null)
        {
            var id = Guid.NewGuid();

            using var context = CreateContext();
            context.Searches.Add(new Search
            {
                Id = id,
                SearchText = $"search {id}",
                StartedAt = DateTime.UtcNow.AddHours(-4),
                EndedAt = state.HasFlag(SearchStates.Completed) ? endedAt ?? DateTime.UtcNow : endedAt,
                State = state,
                Token = Random.Shared.Next(),
                ResponseCount = responses.Count,
                FileCount = fileCount ?? responses.Sum(r => r.FileCount),
            });
            context.Responses.AddRange(responses.Select((r, i) => ResponseRecord.FromResponse(id, i, r)));
            context.SaveChanges();

            return id;
        }

        public void DeleteResponses(Guid id)
        {
            using var context = CreateContext();
            context.Responses.Where(r => r.SearchId == id).ExecuteDelete();
        }

        public int CountResponses(Guid id)
        {
            using var context = CreateContext();
            return context.Responses.Count(r => r.SearchId == id);
        }

        public void Complete(Guid id, List<Response> responses)
        {
            using var context = CreateContext();
            var search = context.Searches.Single(s => s.Id == id);
            search.State = Completed;
            search.EndedAt = DateTime.UtcNow;
            context.Responses.AddRange(responses.Select((r, i) => ResponseRecord.FromResponse(id, i, r)));
            context.SaveChanges();
        }

        public void VerifyBroadcast(string method, Times times)
            => HubClients.Verify(c => c.SendCoreAsync(method, It.IsAny<object[]>(), It.IsAny<CancellationToken>()), times);

        public void Dispose() => anchor.Dispose();

        private SearchDbContext CreateContext() => new(options);
    }

    private sealed class CommandRecorder(ConcurrentQueue<string> commands) : DbCommandInterceptor
    {
        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        {
            commands.Enqueue(command.CommandText);
            return result;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override object ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object result)
        {
            commands.Enqueue(command.CommandText);
            return result;
        }

        public override ValueTask<object> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object result, CancellationToken cancellationToken = default)
        {
            commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
