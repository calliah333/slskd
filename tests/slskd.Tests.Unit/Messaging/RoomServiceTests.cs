namespace slskd.Tests.Unit.Messaging;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Moq;
using slskd.Messaging;
using slskd.Users;
using Soulseek;
using Xunit;

public class RoomServiceTests
{
    [Fact]
    public async Task JoinAsync_After_A_Message_Arrived_Keeps_The_Message_And_Has_The_Joined_Users()
    {
        var mocks = new Mocks();
        mocks.Tracker.AddOrUpdateMessage("room", new RoomMessage { RoomName = "room", Username = "alice", Message = "early", Timestamp = DateTime.UtcNow });
        mocks.Joins(new RoomData("room", new[] { User("alice"), User("bob") }, isPrivate: true, owner: "owner", operatorList: new[] { "op" }));

        await mocks.Service.JoinAsync("room");

        Assert.True(mocks.Tracker.TryGetMessages("room", out var messages, out _));
        Assert.True(mocks.Tracker.TryGetUsers("room", out var users, out _));
        Assert.Equal(new[] { "early" }, messages.Select(m => m.Message));
        Assert.Equal(new[] { "alice", "bob" }, users.Select(u => u.Username));
        Assert.True(mocks.Tracker.Rooms["room"].IsPrivate);
        Assert.Equal("owner", mocks.Tracker.Rooms["room"].Owner);
        Assert.Equal(new[] { "op" }, mocks.Tracker.Rooms["room"].Operators);
    }

    [Fact]
    public async Task JoinAsync_On_Rejoin_Replaces_The_Users()
    {
        var mocks = new Mocks();
        mocks.Joins(new RoomData("room", new[] { User("alice"), User("gone") }, isPrivate: false, owner: null, operatorList: Array.Empty<string>()));
        await mocks.Service.JoinAsync("room");

        // reconnect; "gone" left while we were disconnected and "carol" joined
        mocks.Joins(new RoomData("room", new[] { User("alice"), User("carol") }, isPrivate: false, owner: null, operatorList: Array.Empty<string>()));
        await mocks.Service.JoinAsync("room");

        Assert.True(mocks.Tracker.TryGetUsers("room", out var users, out _));
        Assert.Equal(new[] { "alice", "carol" }, users.Select(u => u.Username));
    }

    private static UserData User(string username)
        => new(username, UserPresence.Online, averageSpeed: 0, uploadCount: 0, fileCount: 0, directoryCount: 0, countryCode: null);

    private class Mocks
    {
        public Mocks()
        {
            Service = new RoomService(
                Client.Object,
                OptionsMonitor.Object,
                StateMutator.Object,
                Tracker,
                Users.Object,
                eventBus: null);
        }

        public Mock<ISoulseekClient> Client { get; } = new();
        public Mock<IOptionsMonitor<slskd.Options>> OptionsMonitor { get; } = new();
        public Mock<IStateMutator<State>> StateMutator { get; } = new();
        public Mock<IUserService> Users { get; } = new();
        public RoomTracker Tracker { get; } = new();
        public RoomService Service { get; }

        public void Joins(RoomData data)
            => Client.Setup(c => c.JoinRoomAsync(data.Name, It.IsAny<bool>(), It.IsAny<CancellationToken?>()))
                .ReturnsAsync(data);
    }
}
