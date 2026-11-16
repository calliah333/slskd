namespace slskd.Tests.Unit.Search;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
    public async Task GetResponsesAsync_Deserializes_A_Completed_Search_Once()
    {
        using var fixture = new Fixture();
        var id = fixture.Insert(Completed, Responses("a", "b"));

        var first = await fixture.Service.GetResponsesAsync(id);

        Assert.Equal(new[] { "a", "b" }, first.Select(r => r.Username));
        Assert.Equal("a/file", first[0].Files.Single().Filename);

        // change the stored responses behind the service's back; the cached copy is served
        fixture.SetResponsesJson(id, "[]");

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
    public async Task DeleteAsync_Deletes_And_Evicts_Cached_Responses()
    {
        using var fixture = new Fixture();
        var id = fixture.Insert(Completed, Responses("a"));
        await fixture.Service.GetResponsesAsync(id);

        await fixture.Service.DeleteAsync(await fixture.Service.FindAsync(s => s.Id == id));

        Assert.Null(await fixture.Service.GetResponsesAsync(id));
        Assert.Null(await fixture.Service.FindAsync(s => s.Id == id));
        fixture.VerifyBroadcast(SearchHubMethods.Delete, Times.Once());
    }

    [Fact]
    public async Task PruneAsync_Deletes_Expired_Searches_With_A_Single_Broadcast()
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

    private static List<Response> Responses(params string[] usernames)
        => usernames.Select(u => new Response
        {
            Username = u,
            FileCount = 1,
            Files = [new File { Filename = $"{u}/file", Size = 1 }],
        }).ToList();

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

        public Mock<IClientProxy> HubClients { get; } = new();
        public SearchService Service { get; }

        public Guid Insert(SearchStates state, List<Response> responses, DateTime? endedAt = default)
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
                ResponseCount = responses.Count,
                FileCount = responses.Sum(r => r.FileCount),
                Responses = responses,
            });
            context.SaveChanges();

            return id;
        }

        public void SetResponsesJson(Guid id, string json)
        {
            using var context = CreateContext();
            context.Searches.Where(s => s.Id == id).ExecuteUpdate(s => s.SetProperty(x => x.ResponsesJson, json));
        }

        public void Complete(Guid id, List<Response> responses)
        {
            using var context = CreateContext();
            var search = context.Searches.Single(s => s.Id == id);
            search.State = Completed;
            search.EndedAt = DateTime.UtcNow;
            search.Responses = responses;
            context.SaveChanges();
        }

        public void VerifyBroadcast(string method, Times times)
            => HubClients.Verify(c => c.SendCoreAsync(method, It.IsAny<object[]>(), It.IsAny<CancellationToken>()), times);

        public void Dispose() => anchor.Dispose();

        private SearchDbContext CreateContext() => new(options);
    }
}
