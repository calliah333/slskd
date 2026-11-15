// <copyright file="BrowseCache.cs" company="JP Dillingham">
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
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Soulseek;

    /// <summary>
    ///     Caches the results of browse operations, bounded by the total number of cached files.
    /// </summary>
    /// <remarks>
    ///     Entries expire after going unused for the sliding expiration period. When adding an entry would exceed the file
    ///     limit, the least recently used entries are evicted; the newest entry is always kept, even if it alone exceeds the limit.
    /// </remarks>
    public class BrowseCache : IBrowseCache
    {
        /// <summary>
        ///     The default maximum number of files cached across all users.
        /// </summary>
        /// <remarks>
        ///     A cached file costs roughly 440 bytes of memory, so this bounds the cache at around 450 MiB.
        /// </remarks>
        public const long DefaultMaxFiles = 1_000_000;

        /// <summary>
        ///     Initializes a new instance of the <see cref="BrowseCache"/> class.
        /// </summary>
        /// <param name="maxFiles">The maximum number of files cached across all users.</param>
        /// <param name="slidingExpiration">The amount of time an unused entry is retained. Defaults to 15 minutes.</param>
        public BrowseCache(long maxFiles = DefaultMaxFiles, TimeSpan? slidingExpiration = null)
        {
            MaxFiles = maxFiles;
            SlidingExpirationMilliseconds = (long)(slidingExpiration ?? TimeSpan.FromMinutes(15)).TotalMilliseconds;

            Clock.EveryMinute += (_, _) => Prune();
        }

        private Dictionary<string, Slot> Entries { get; } = new Dictionary<string, Slot>(StringComparer.Ordinal);
        private long MaxFiles { get; }

        // a counter, not a timestamp, orders entries for eviction; several accesses can land in the same millisecond
        private long AccessSequence { get; set; }
        private long SlidingExpirationMilliseconds { get; }
        private object SyncRoot { get; } = new object();
        private long TotalFiles { get; set; }

        /// <summary>
        ///     Caches the specified browse <paramref name="response"/> for the specified <paramref name="username"/>, replacing
        ///     any previously cached response for the user.
        /// </summary>
        /// <param name="username">The user whose shares were browsed.</param>
        /// <param name="response">The browse response.</param>
        /// <returns>The cached entry.</returns>
        public BrowseCacheEntry AddOrUpdate(string username, BrowseResponse response)
        {
            // index outside of the lock; this is the expensive part
            var entry = new BrowseCacheEntry(response);

            lock (SyncRoot)
            {
                var now = Environment.TickCount64;

                Remove(username);
                PruneExpired(now);

                while (Entries.Count > 0 && TotalFiles + entry.TotalFileCount > MaxFiles)
                {
                    Remove(Entries.MinBy(e => e.Value.LastAccessSequence).Key);
                }

                Entries[username] = new Slot(entry) { LastAccessed = now, LastAccessSequence = ++AccessSequence };
                TotalFiles += entry.TotalFileCount;
            }

            return entry;
        }

        /// <summary>
        ///     Gets the cached browse response for the specified <paramref name="username"/>, if one exists and has not expired.
        /// </summary>
        /// <param name="username">The user whose cached browse response to retrieve.</param>
        /// <param name="entry">The cached entry, if found.</param>
        /// <returns>A value indicating whether an entry was found.</returns>
        public bool TryGet(string username, out BrowseCacheEntry entry)
        {
            lock (SyncRoot)
            {
                var now = Environment.TickCount64;

                if (Entries.TryGetValue(username, out var slot))
                {
                    if (!IsExpired(slot, now))
                    {
                        slot.LastAccessed = now;
                        slot.LastAccessSequence = ++AccessSequence;
                        entry = slot.Entry;
                        return true;
                    }

                    Remove(username);
                }

                entry = null;
                return false;
            }
        }

        private bool IsExpired(Slot slot, long now) => now - slot.LastAccessed >= SlidingExpirationMilliseconds;

        private void Prune()
        {
            lock (SyncRoot)
            {
                PruneExpired(Environment.TickCount64);
            }
        }

        private void PruneExpired(long now)
        {
            foreach (var username in Entries.Where(e => IsExpired(e.Value, now)).Select(e => e.Key).ToList())
            {
                Remove(username);
            }
        }

        private void Remove(string username)
        {
            if (Entries.Remove(username, out var slot))
            {
                TotalFiles -= slot.Entry.TotalFileCount;
            }
        }

        private sealed class Slot
        {
            public Slot(BrowseCacheEntry entry)
            {
                Entry = entry;
            }

            public BrowseCacheEntry Entry { get; }
            public long LastAccessed { get; set; }
            public long LastAccessSequence { get; set; }
        }
    }
}
