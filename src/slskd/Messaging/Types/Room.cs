// <copyright file="Room.cs" company="JP Dillingham">
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
    using System.Collections.Generic;
    using System.Linq;
    using Soulseek;

    public class Room
    {
        /// <summary>
        ///     The room name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        ///     A value indicating whether the room is private.
        /// </summary>
        public bool IsPrivate { get; set; }

        /// <summary>
        ///     The number of operators in the room, if private.
        /// </summary>
        public int? OperatorCount { get; set; }

        /// <summary>
        ///     The operators in the room, if private.
        /// </summary>
        public IList<string> Operators { get; set; }

        /// <summary>
        ///     The owner of the room, if private.
        /// </summary>
        public string Owner { get; set; }

        /// <summary>
        ///     The list of users in the room.
        /// </summary>
        /// <remarks>
        ///     Treat as immutable once assigned; <see cref="RoomTracker"/> replaces the list (and <see cref="UsersRevision"/>)
        ///     on every change so readers can enumerate it without locking.
        /// </remarks>
        public IList<UserData> Users { get; set; } = new List<UserData>();

        /// <summary>
        ///     The revision of <see cref="Users"/>; changes whenever the list changes.
        /// </summary>
        public long UsersRevision { get; set; } = Revision.Next();

        /// <summary>
        ///     The list of messages.
        /// </summary>
        /// <remarks>
        ///     Treat as immutable once assigned; <see cref="RoomTracker"/> replaces the list (and
        ///     <see cref="MessagesRevision"/>) on every change so readers can enumerate it without locking.
        /// </remarks>
        public IList<RoomMessage> Messages { get; set; } = new List<RoomMessage>();

        /// <summary>
        ///     The revision of <see cref="Messages"/>; changes whenever the list changes.
        /// </summary>
        public long MessagesRevision { get; set; } = Revision.Next();

        public static Room FromRoomData(RoomData roomData)
        {
            return new Room()
            {
                Name = roomData.Name,
                IsPrivate = roomData.IsPrivate,
                OperatorCount = roomData.OperatorCount,
                Operators = roomData.Operators?.ToList(),
                Owner = roomData.Owner,
                Users = roomData.Users?.ToList() ?? new List<UserData>(),
                Messages = new List<RoomMessage>(),
            };
        }
    }
}
