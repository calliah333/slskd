// <copyright file="IBrowseCache.cs" company="JP Dillingham">
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

namespace slskd.Users
{
    using Soulseek;

    /// <summary>
    ///     Caches the results of browse operations so that directory contents can be served without browsing again.
    /// </summary>
    public interface IBrowseCache
    {
        /// <summary>
        ///     Caches the specified browse <paramref name="response"/> for the specified <paramref name="username"/>, replacing
        ///     any previously cached response for the user.
        /// </summary>
        /// <param name="username">The user whose shares were browsed.</param>
        /// <param name="response">The browse response.</param>
        /// <returns>The cached entry.</returns>
        BrowseCacheEntry AddOrUpdate(string username, BrowseResponse response);

        /// <summary>
        ///     Gets the cached browse response for the specified <paramref name="username"/>, if one exists and has not expired.
        /// </summary>
        /// <param name="username">The user whose cached browse response to retrieve.</param>
        /// <param name="entry">The cached entry, if found.</param>
        /// <returns>A value indicating whether an entry was found.</returns>
        bool TryGet(string username, out BrowseCacheEntry entry);
    }
}
