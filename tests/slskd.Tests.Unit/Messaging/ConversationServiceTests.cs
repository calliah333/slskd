namespace slskd.Tests.Unit.Messaging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using slskd.Messaging;
using Soulseek;
using Xunit;

public class ConversationServiceTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ListAsync_Counts_Unacknowledged_Messages_Per_Conversation()
    {
        using var db = new Db();
        db.AddConversation("alice");
        db.AddConversation("bob");
        db.AddConversation("carol");
        db.AddConversation("dave", isActive: false);

        db.AddMessage("alice", id: 1, acknowledged: false);
        db.AddMessage("alice", id: 2, acknowledged: false);
        db.AddMessage("alice", id: 3, acknowledged: true);
        db.AddMessage("bob", id: 4, acknowledged: true);
        db.AddMessage("dave", id: 5, acknowledged: false);
        db.AddMessage("eve", id: 6, acknowledged: false); // a message with no conversation

        var conversations = (await db.Service.ListAsync(c => c.IsActive)).ToList();

        Assert.Equal(new[] { "alice", "bob", "carol" }, conversations.Select(c => c.Username));
        Assert.Equal(new[] { 2, 0, 0 }, conversations.Select(c => c.UnAcknowledgedMessageCount));
        Assert.True(conversations[0].HasUnAcknowledgedMessages);
        Assert.All(conversations, c => Assert.Null(c.Messages));
    }

    [Fact]
    public async Task ListAsync_Without_Expression_Returns_All_Conversations()
    {
        using var db = new Db();
        db.AddConversation("alice");
        db.AddConversation("bob", isActive: false);
        db.AddMessage("bob", id: 1, acknowledged: false);

        var conversations = (await db.Service.ListAsync()).ToList();

        Assert.Equal(new[] { "alice", "bob" }, conversations.Select(c => c.Username));
        Assert.Equal(new[] { 0, 1 }, conversations.Select(c => c.UnAcknowledgedMessageCount));
    }

    [Fact]
    public async Task FindAsync_Counts_Every_Unacknowledged_Message_Not_Only_The_Returned_Ones()
    {
        using var db = new Db();
        db.AddConversation("alice");

        for (var i = 0; i < 150; i++)
        {
            db.AddMessage("alice", id: i, acknowledged: false, timestamp: T0.AddSeconds(i));
        }

        var withMessages = await db.Service.FindAsync("alice", includeMessages: true);
        var withoutMessages = await db.Service.FindAsync("alice");

        Assert.Equal(100, withMessages.Messages.Count());
        Assert.Equal(T0.AddSeconds(149), withMessages.Messages.Last().Timestamp); // the most recent, in order
        Assert.Equal(150, withMessages.UnAcknowledgedMessageCount);
        Assert.Equal(150, withoutMessages.UnAcknowledgedMessageCount);
    }

    [Fact]
    public async Task GetRevision_Changes_Only_When_The_Conversation_Changes()
    {
        using var db = new Db();
        db.AddConversation("alice");
        db.AddConversation("bob");
        db.AddMessage("alice", id: 1, acknowledged: false);

        var alice = db.Service.GetRevision("alice");
        var bob = db.Service.GetRevision("bob");

        await db.Service.ListAsync();
        await db.Service.FindAsync("alice", includeMessages: true);
        Assert.Equal(alice, db.Service.GetRevision("alice"));

        await db.Service.AcknowledgeAsync("alice");
        var acknowledged = db.Service.GetRevision("alice");
        Assert.NotEqual(alice, acknowledged);
        Assert.Equal(0, (await db.Service.FindAsync("alice")).UnAcknowledgedMessageCount);

        // nothing left to acknowledge, so nothing changes
        await db.Service.AcknowledgeAsync("alice");
        Assert.Equal(acknowledged, db.Service.GetRevision("alice"));

        await db.Service.SendMessageAsync("alice", "hi");
        Assert.NotEqual(acknowledged, db.Service.GetRevision("alice"));

        Assert.Equal(bob, db.Service.GetRevision("bob"));
    }

    [Fact]
    public void Unacknowledged_Count_Query_Seeks_The_Index()
    {
        using var db = new Db();

        var plan = db.ExplainQueryPlan("SELECT Username, COUNT(*) AS Count FROM PrivateMessages WHERE IsAcknowledged = 0 GROUP BY Username");

        Assert.Contains("IDX_PrivateMessages_IsAcknowledgedUsername", plan);
        Assert.DoesNotContain("TEMP B-TREE", plan);
    }

    private sealed class Db : IDisposable, IDbContextFactory<MessagingDbContext>
    {
        private readonly SqliteConnection _anchor;
        private readonly DbContextOptions<MessagingDbContext> _options;

        public Db()
        {
            var connectionString = $"Data Source=messaging_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

            // keep the shared-cache in-memory database alive for the life of this fixture
            _anchor = new SqliteConnection(connectionString);
            _anchor.Open();

            _options = new DbContextOptionsBuilder<MessagingDbContext>()
                .UseSqlite(connectionString)
                .Options;

            // derive the schema (including indexes) from the EF model
            using var context = CreateDbContext();
            context.Database.EnsureCreated();

            Service = new ConversationService(new Mock<ISoulseekClient>().Object, eventBus: null, contextFactory: this);
        }

        public ConversationService Service { get; }

        public MessagingDbContext CreateDbContext() => new(_options);

        public void AddConversation(string username, bool isActive = true)
        {
            using var context = CreateDbContext();
            context.Conversations.Add(new Conversation { Username = username, IsActive = isActive });
            context.SaveChanges();
        }

        public void AddMessage(string username, int id, bool acknowledged, DateTime? timestamp = null)
        {
            using var context = CreateDbContext();
            context.PrivateMessages.Add(new PrivateMessage
            {
                Username = username,
                Id = id,
                Timestamp = timestamp ?? T0.AddSeconds(id),
                Message = $"message {id}",
                Direction = MessageDirection.In,
                IsAcknowledged = acknowledged,
            });
            context.SaveChanges();
        }

        public string ExplainQueryPlan(string sql)
        {
            using var command = new SqliteCommand($"EXPLAIN QUERY PLAN {sql}", _anchor);
            using var reader = command.ExecuteReader();

            var details = new List<string>();

            while (reader.Read())
            {
                details.Add(reader.GetString(reader.GetOrdinal("detail")));
            }

            return string.Join(" | ", details);
        }

        public void Dispose() => _anchor.Dispose();
    }
}
