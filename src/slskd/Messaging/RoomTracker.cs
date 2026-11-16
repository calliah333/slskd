// <copyright file="RoomTracker.cs" company="JP Dillingham">
//           ▄▄▄▄     ▄▄▄▄     ▄▄▄▄
//     ▄▄▄▄▄▄█  █▄▄▄▄▄█  █▄▄▄▄▄█  █
//     █__ --█  █__ --█    ◄█  -  █
//     █▄▄▄▄▄█▄▄█▄▄▄▄▄█▄▄█▄▄█▄▄▄▄▄█
//   ┍━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ ━━━━ ━  ━┉   ┉     ┉
//   │ Copyright (c) JP Dillingham.
//   │
//   │ This program is free software: you can redistribute it and/or modify
//   │ it under the terms of the GNU Affero General Public License as published
//   │ by the Free Software Foundation, version 3.
//   │
//   │ This program is distributed in the hope that it will be useful,
//   │ but WITHOUT ANY WARRANTY; without even the implied warranty of
//   │ MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//   │ GNU Affero General Public License for more details.
//   │
//   │ You should have received a copy of the GNU Affero General Public License
//   │ along with this program.  If not, see https://www.gnu.org/licenses/.
//   │
//   │ This program is distributed with Additional Terms pursuant to Section 7
//   │ of the AGPLv3.  See the LICENSE file in the root directory of this
//   │ project for the complete terms and conditions.
//   │
//   │ https://slskd.org
//   │
//   ├╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌ ╌ ╌╌╌╌ ╌
//   │ SPDX-FileCopyrightText: JP Dillingham
//   │ SPDX-License-Identifier: AGPL-3.0-only
//   ╰───────────────────────────────────────────╶──── ─ ─── ─  ── ──┈  ┈
// </copyright>

namespace slskd.Messaging
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using Soulseek;

    /// <summary>
    ///     Tracks rooms.
    /// </summary>
    /// <remarks>
    ///     Room user and message lists are copy-on-write: every change publishes a new list together with a new revision
    ///     (under a per-room lock), so readers can enumerate a list while it is being changed and can tell whether it
    ///     changed since they last read it.
    /// </remarks>
    public class RoomTracker : IRoomTracker
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="RoomTracker"/> class.
        /// </summary>
        /// <param name="messageLimit"></param>
        public RoomTracker(int messageLimit = 25)
        {
            MessageLimit = messageLimit;
        }

        /// <summary>
        ///     Tracked rooms.
        /// </summary>
        public ConcurrentDictionary<string, Room> Rooms { get; } = new ConcurrentDictionary<string, Room>();

        private int MessageLimit { get; }

        /// <summary>
        ///     Adds a room and appends the specified <paramref name="message"/>, or just appends the message if the room exists.
        /// </summary>
        /// <param name="roomName"></param>
        /// <param name="message"></param>
        public void AddOrUpdateMessage(string roomName, RoomMessage message)
        {
            var room = Rooms.GetOrAdd(roomName, name => new Room() { Name = name });

            lock (room)
            {
                var current = room.Messages;
                var skip = Math.Max(0, current.Count - (MessageLimit - 1));
                var next = new List<RoomMessage>(current.Count - skip + 1);

                for (var i = skip; i < current.Count; i++)
                {
                    next.Add(current[i]);
                }

                next.Add(message);

                room.Messages = next;
                room.MessagesRevision = Revision.Next();
            }
        }

        /// <summary>
        ///     Adds the specified room to the tracker.
        /// </summary>
        /// <param name="roomName"></param>
        /// <param name="room"></param>
        public void TryAdd(string roomName, Room room) => Rooms.TryAdd(roomName, room);

        /// <summary>
        ///     Adds the specified <paramref name="userData"/> to the specified room, replacing any existing entry for the
        ///     same user.
        /// </summary>
        /// <param name="roomName"></param>
        /// <param name="userData"></param>
        public void TryAddUser(string roomName, UserData userData)
        {
            if (Rooms.TryGetValue(roomName, out var room))
            {
                lock (room)
                {
                    var current = room.Users;
                    var next = new List<UserData>(current.Count + 1);

                    foreach (var user in current)
                    {
                        if (user.Username != userData.Username)
                        {
                            next.Add(user);
                        }
                    }

                    next.Add(userData);

                    room.Users = next;
                    room.UsersRevision = Revision.Next();
                }
            }
        }

        /// <summary>
        ///     Returns the tracked room with the specified <paramref name="roomName"/>, if it is tracked.
        /// </summary>
        /// <param name="roomName"></param>
        /// <param name="room"></param>
        /// <returns>A value indicating whether the room is tracked.</returns>
        public bool TryGet(string roomName, out Room room) => Rooms.TryGetValue(roomName, out room);

        /// <summary>
        ///     Returns the current list of messages for the specified <paramref name="roomName"/> and its revision, if
        ///     the room is tracked.
        /// </summary>
        /// <param name="roomName"></param>
        /// <param name="messages">The current (immutable) list of messages.</param>
        /// <param name="revision">The revision of <paramref name="messages"/>.</param>
        /// <returns>A value indicating whether the room is tracked.</returns>
        public bool TryGetMessages(string roomName, out IList<RoomMessage> messages, out long revision)
        {
            if (Rooms.TryGetValue(roomName, out var room))
            {
                lock (room)
                {
                    messages = room.Messages;
                    revision = room.MessagesRevision;
                    return true;
                }
            }

            messages = null;
            revision = 0;
            return false;
        }

        /// <summary>
        ///     Returns the current list of users for the specified <paramref name="roomName"/> and its revision, if the
        ///     room is tracked.
        /// </summary>
        /// <param name="roomName"></param>
        /// <param name="users">The current (immutable) list of users.</param>
        /// <param name="revision">The revision of <paramref name="users"/>.</param>
        /// <returns>A value indicating whether the room is tracked.</returns>
        public bool TryGetUsers(string roomName, out IList<UserData> users, out long revision)
        {
            if (Rooms.TryGetValue(roomName, out var room))
            {
                lock (room)
                {
                    users = room.Users;
                    revision = room.UsersRevision;
                    return true;
                }
            }

            users = null;
            revision = 0;
            return false;
        }

        /// <summary>
        ///     Removes a tracked room.
        /// </summary>
        /// <param name="roomName"></param>
        public void TryRemove(string roomName) => Rooms.TryRemove(roomName, out _);

        /// <summary>
        ///     Removes the specified <paramref name="username"/> from the specified room.
        /// </summary>
        /// <param name="roomName"></param>
        /// <param name="username"></param>
        public void TryRemoveUser(string roomName, string username)
        {
            if (Rooms.TryGetValue(roomName, out var room))
            {
                lock (room)
                {
                    var current = room.Users;
                    var next = new List<UserData>(current.Count);

                    foreach (var user in current)
                    {
                        if (user.Username != username)
                        {
                            next.Add(user);
                        }
                    }

                    if (next.Count != current.Count)
                    {
                        room.Users = next;
                        room.UsersRevision = Revision.Next();
                    }
                }
            }
        }
    }
}
