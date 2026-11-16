namespace slskd.Tests.Unit.Events;

using System;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using slskd.Events;
using Xunit;

public class EventServiceTests
{
    [Fact]
    public void Prune_Deletes_Only_Events_Older_Than_Age_Across_Multiple_Batches()
    {
        using var db = new Db();

        var old = Enumerable.Range(0, EventService.PruneBatchSize + 3)
            .Select(i => Record(DateTime.UtcNow.AddMinutes(-60).AddSeconds(-i)));
        var recent = new[] { Record(DateTime.UtcNow.AddMinutes(-5)), Record(DateTime.UtcNow) };

        db.Insert(old.Concat(recent).ToArray());

        var service = db.CreateService();

        var pruned = service.Prune(age: 30);

        Assert.Equal(EventService.PruneBatchSize + 3, pruned);
        Assert.Equal(2, service.Count());
        Assert.Equal(recent.Select(r => r.Id).OrderBy(id => id), service.Get().Select(r => r.Id).OrderBy(id => id));
    }

    [Fact]
    public void Prune_Returns_Zero_When_Nothing_Is_Expired()
    {
        using var db = new Db();
        db.Insert(Record(DateTime.UtcNow));

        var service = db.CreateService();

        Assert.Equal(0, service.Prune(age: 5));
        Assert.Equal(1, service.Count());
    }

    [Fact]
    public void Prune_Throws_If_Age_Is_Negative()
    {
        using var db = new Db();

        Assert.Throws<ArgumentOutOfRangeException>(() => db.CreateService().Prune(age: -1));
    }

    [Fact]
    public void Get_Pages_Newest_First()
    {
        using var db = new Db();

        var records = Enumerable.Range(0, 5).Select(i => Record(DateTime.UtcNow.AddMinutes(-i))).ToArray();
        db.Insert(records);

        var page = db.CreateService().Get(offset: 1, limit: 2);

        Assert.Equal(new[] { records[1].Id, records[2].Id }, page.Select(r => r.Id));
    }

    private static EventRecord Record(DateTime timestamp)
        => EventRecord.From<NoopEvent>(new NoopEvent { Timestamp = timestamp });

    private sealed class Db : IDisposable, IDbContextFactory<EventsDbContext>
    {
        private readonly SqliteConnection _anchor;
        private readonly DbContextOptions<EventsDbContext> _options;

        public Db()
        {
            var connectionString = $"Data Source=events_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

            // keep the shared-cache in-memory database alive for the life of this fixture
            _anchor = new SqliteConnection(connectionString);
            _anchor.Open();

            _options = new DbContextOptionsBuilder<EventsDbContext>()
                .UseSqlite(connectionString)
                .Options;

            using var context = CreateDbContext();
            context.Database.EnsureCreated();
        }

        public EventsDbContext CreateDbContext() => new(_options);

        public EventService CreateService() => new(this, new TestOptionsMonitor<Options>(new Options()));

        public void Insert(params EventRecord[] records)
        {
            using var context = CreateDbContext();
            context.Events.AddRange(records);
            context.SaveChanges();
        }

        public void Dispose() => _anchor.Dispose();
    }
}
