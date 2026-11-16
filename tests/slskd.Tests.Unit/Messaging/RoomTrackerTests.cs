namespace slskd.Tests.Unit.Messaging;

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
        tracker.TryAdd("room", new Room { Name = "room" });

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
        tracker.TryAdd("room", new Room { Name = "room" });
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
        tracker.TryAdd("room", new Room { Name = "room" });
        Assert.True(tracker.TryGetMessages("room", out _, out var first));

        // leave and rejoin; a client holding the old revision must not be told the new room is unchanged
        tracker.TryRemove("room");
        tracker.TryAdd("room", new Room { Name = "room" });
        Assert.True(tracker.TryGetMessages("room", out _, out var second));

        Assert.NotEqual(first, second);
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
