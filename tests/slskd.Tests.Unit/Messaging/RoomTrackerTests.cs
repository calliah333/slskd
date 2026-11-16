namespace slskd.Tests.Unit.Messaging;

using System.Collections.Generic;
using System.Linq;
using slskd.Messaging;
using Soulseek;
using Xunit;

public class RoomTrackerTests
{
    [Fact]
    public void AddOrUpdateMessage_Keeps_The_Most_Recent_Messages_Without_Mutating_Published_Lists()
    {
        var tracker = new RoomTracker(messageLimit: 3);

        tracker.AddOrUpdateMessage("room", Message("1"));
        tracker.AddOrUpdateMessage("room", Message("2"));
        Assert.True(tracker.TryGetMessages("room", out var before, out var beforeRevision));

        tracker.AddOrUpdateMessage("room", Message("3"));
        tracker.AddOrUpdateMessage("room", Message("4"));
        Assert.True(tracker.TryGetMessages("room", out var after, out var afterRevision));

        Assert.Equal(new[] { "1", "2" }, before.Select(m => m.Message));
        Assert.Equal(new[] { "2", "3", "4" }, after.Select(m => m.Message));
        Assert.NotEqual(beforeRevision, afterRevision);
        Assert.Equal("room", tracker.Rooms["room"].Name);
    }

    [Fact]
    public void TryAddUser_Replaces_An_Existing_Entry_For_The_Same_User()
    {
        var tracker = new RoomTracker();
        tracker.AddOrUpdate("room", new Room { Name = "room" });

        tracker.TryAddUser("room", User("alice", UserPresence.Away));
        tracker.TryAddUser("room", User("bob", UserPresence.Online));
        Assert.True(tracker.TryGetUsers("room", out _, out var revision));

        tracker.TryAddUser("room", User("alice", UserPresence.Online));
        Assert.True(tracker.TryGetUsers("room", out var users, out var nextRevision));

        Assert.Equal(new[] { "bob", "alice" }, users.Select(u => u.Username));
        Assert.Equal(UserPresence.Online, users.Single(u => u.Username == "alice").Status);
        Assert.NotEqual(revision, nextRevision);
    }

    [Fact]
    public void TryRemoveUser_Changes_The_Revision_Only_When_A_User_Is_Removed()
    {
        var tracker = new RoomTracker();
        tracker.AddOrUpdate("room", new Room { Name = "room" });
        tracker.TryAddUser("room", User("alice"));
        Assert.True(tracker.TryGetUsers("room", out var before, out var revision));

        tracker.TryRemoveUser("room", "nobody");
        Assert.True(tracker.TryGetUsers("room", out _, out var unchanged));
        Assert.Equal(revision, unchanged);

        tracker.TryRemoveUser("room", "alice");
        Assert.True(tracker.TryGetUsers("room", out var after, out var removed));
        Assert.NotEqual(revision, removed);
        Assert.Empty(after);
        Assert.Single(before); // the previously published list is untouched
    }

    [Fact]
    public void Rooms_Created_Separately_Never_Share_A_Revision()
    {
        var tracker = new RoomTracker();
        tracker.AddOrUpdate("room", new Room { Name = "room" });
        Assert.True(tracker.TryGetMessages("room", out _, out var first));

        // leave and rejoin; a client holding the old revision must not be told the new room is unchanged
        tracker.TryRemove("room");
        tracker.AddOrUpdate("room", new Room { Name = "room" });
        Assert.True(tracker.TryGetMessages("room", out _, out var second));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void AddOrUpdate_After_A_Message_Keeps_The_Message_And_Takes_The_Joined_Users_And_Metadata()
    {
        var tracker = new RoomTracker();

        // a message arrives before the join completes, creating a placeholder room
        tracker.AddOrUpdateMessage("room", Message("early"));
        Assert.True(tracker.TryGetMessages("room", out _, out var messagesRevision));
        Assert.True(tracker.TryGetUsers("room", out _, out var usersRevision));

        tracker.AddOrUpdate("room", new Room
        {
            Name = "room",
            IsPrivate = true,
            Owner = "owner",
            Operators = new List<string> { "op" },
            OperatorCount = 1,
            Users = new List<UserData> { User("alice"), User("bob") },
        });

        Assert.True(tracker.TryGetMessages("room", out var messages, out var nextMessagesRevision));
        Assert.True(tracker.TryGetUsers("room", out var users, out var nextUsersRevision));
        Assert.Equal(new[] { "early" }, messages.Select(m => m.Message));
        Assert.Equal(messagesRevision, nextMessagesRevision);
        Assert.Equal(new[] { "alice", "bob" }, users.Select(u => u.Username));
        Assert.NotEqual(usersRevision, nextUsersRevision);

        var room = tracker.Rooms["room"];
        Assert.True(room.IsPrivate);
        Assert.Equal("owner", room.Owner);
        Assert.Equal(new[] { "op" }, room.Operators);
        Assert.Equal(1, room.OperatorCount);
    }

    [Fact]
    public void AddOrUpdate_On_Rejoin_Replaces_The_Users_And_Keeps_The_Messages()
    {
        var tracker = new RoomTracker();
        tracker.AddOrUpdate("room", new Room { Name = "room", Users = new List<UserData> { User("alice"), User("gone") } });
        tracker.AddOrUpdateMessage("room", Message("hello"));
        Assert.True(tracker.TryGetUsers("room", out var before, out var revision));

        // reconnect; "gone" left while we were disconnected and "carol" joined
        tracker.AddOrUpdate("room", new Room { Name = "room", Users = new List<UserData> { User("alice"), User("carol") } });

        Assert.True(tracker.TryGetUsers("room", out var after, out var nextRevision));
        Assert.True(tracker.TryGetMessages("room", out var messages, out _));
        Assert.Equal(new[] { "alice", "carol" }, after.Select(u => u.Username));
        Assert.NotEqual(revision, nextRevision);
        Assert.Equal(new[] { "alice", "gone" }, before.Select(u => u.Username)); // the previously published list is untouched
        Assert.Equal(new[] { "hello" }, messages.Select(m => m.Message));
    }

    [Fact]
    public void TryGet_Snapshots_Return_False_For_Untracked_Rooms()
    {
        var tracker = new RoomTracker();

        Assert.False(tracker.TryGetMessages("room", out var messages, out _));
        Assert.False(tracker.TryGetUsers("room", out var users, out _));
        Assert.Null(messages);
        Assert.Null(users);
    }

    private static RoomMessage Message(string text) => new() { RoomName = "room", Username = "someone", Message = text };

    private static UserData User(string username, UserPresence status = UserPresence.Online)
        => new(username, status, averageSpeed: 0, uploadCount: 0, fileCount: 0, directoryCount: 0, countryCode: null);
}
