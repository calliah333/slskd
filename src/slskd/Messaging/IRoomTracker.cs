// <copyright file="IRoomTracker.cs" company="JP Dillingham">
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
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using Soulseek;

    /// <summary>
    ///     Tracks rooms.
    /// </summary>
    public interface IRoomTracker
    {
        /// <summary>
        ///     Gets tracked rooms.
        /// </summary>
        ConcurrentDictionary<string, Room> Rooms { get; }

        /// <summary>
        ///     Adds a room and appends the specified <paramref name="message"/>, or just appends the message if the room exists.
        /// </summary>
        /// <param name="roomName"></param>
        /// <param name="message"></param>
        void AddOrUpdateMessage(string roomName, RoomMessage message);

        /// <summary>
        ///     Adds the specified <paramref name="room"/> to the tracker or, if the room is already tracked, replaces the
        ///     tracked room's users and metadata with those of <paramref name="room"/> while keeping its messages.
        /// </summary>
        /// <param name="roomName"></param>
        /// <param name="room"></param>
        void AddOrUpdate(string roomName, Room room);

        /// <summary>
        ///     Adds the specified <paramref name="userData"/> to the specified room.
        /// </summary>
        /// <param name="roomName"></param>
        /// <param name="userData"></param>
        void TryAddUser(string roomName, UserData userData);

        /// <summary>
        ///     Returns the list of messages for the specified <paramref name="roomName"/>, if it is tracked.
        /// </summary>
        /// <param name="roomName"></param>
        /// <param name="room"></param>
        /// <returns></returns>
        bool TryGet(string roomName, out Room room);

        /// <summary>
        ///     Returns the current list of messages for the specified <paramref name="roomName"/> and its revision, if
        ///     the room is tracked.
        /// </summary>
        /// <param name="roomName"></param>
        /// <param name="messages">The current (immutable) list of messages.</param>
        /// <param name="revision">The revision of <paramref name="messages"/>.</param>
        /// <returns>A value indicating whether the room is tracked.</returns>
        bool TryGetMessages(string roomName, out IList<RoomMessage> messages, out long revision);

        /// <summary>
        ///     Returns the current list of users for the specified <paramref name="roomName"/> and its revision, if the
        ///     room is tracked.
        /// </summary>
        /// <param name="roomName"></param>
        /// <param name="users">The current (immutable) list of users.</param>
        /// <param name="revision">The revision of <paramref name="users"/>.</param>
        /// <returns>A value indicating whether the room is tracked.</returns>
        bool TryGetUsers(string roomName, out IList<UserData> users, out long revision);

        /// <summary>
        ///     Removes a tracked room.
        /// </summary>
        /// <param name="roomName"></param>
        void TryRemove(string roomName);

        /// <summary>
        ///     Removes the specified <paramref name="username"/> from the specified room.
        /// </summary>
        /// <param name="roomName"></param>
        /// <param name="username"></param>
        void TryRemoveUser(string roomName, string username);
    }
}