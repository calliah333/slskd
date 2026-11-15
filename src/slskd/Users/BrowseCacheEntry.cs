// <copyright file="BrowseCacheEntry.cs" company="JP Dillingham">
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
    using Soulseek;

    /// <summary>
    ///     An immutable browse response, indexed by directory name.
    /// </summary>
    public sealed class BrowseCacheEntry
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="BrowseCacheEntry"/> class.
        /// </summary>
        /// <param name="response">The browse response to index.</param>
        public BrowseCacheEntry(BrowseResponse response)
        {
            var unlocked = response.Directories ?? (IReadOnlyCollection<Directory>)Array.Empty<Directory>();
            var locked = response.LockedDirectories ?? (IReadOnlyCollection<Directory>)Array.Empty<Directory>();

            var directories = new BrowseCacheDirectory[unlocked.Count + locked.Count];
            var position = 0;

            foreach (var directory in unlocked)
            {
                directories[position++] = new BrowseCacheDirectory(directory, Locked: false);
                FileCount += directory.FileCount;
            }

            foreach (var directory in locked)
            {
                directories[position++] = new BrowseCacheDirectory(directory, Locked: true);
                LockedFileCount += directory.FileCount;
            }

            DirectoryCount = unlocked.Count;
            LockedDirectoryCount = locked.Count;
            Directories = directories;

            // sorting tens of thousands of long names takes tens of milliseconds; defer it until directory contents are
            // requested so that browsing (which only needs the counts above) doesn't pay for it
            Index = new Lazy<NameIndex>(() => new NameIndex(directories));
        }

        /// <summary>
        ///     Gets every directory; unlocked directories first, then locked directories, each in the order they were received.
        /// </summary>
        public IReadOnlyList<BrowseCacheDirectory> Directories { get; }

        /// <summary>
        ///     Gets the number of unlocked directories.
        /// </summary>
        public int DirectoryCount { get; }

        /// <summary>
        ///     Gets the number of files in unlocked directories.
        /// </summary>
        public int FileCount { get; }

        /// <summary>
        ///     Gets the number of locked directories.
        /// </summary>
        public int LockedDirectoryCount { get; }

        /// <summary>
        ///     Gets the number of files in locked directories.
        /// </summary>
        public int LockedFileCount { get; }

        /// <summary>
        ///     Gets the total number of files, locked and unlocked.
        /// </summary>
        public int TotalFileCount => FileCount + LockedFileCount;

        private Lazy<NameIndex> Index { get; }

        /// <summary>
        ///     Finds the directories with the specified <paramref name="name"/> and, if <paramref name="recursive"/>, every
        ///     descendant directory.
        /// </summary>
        /// <remarks>
        ///     Names are compared ordinally. A descendant is a directory whose name begins with <paramref name="name"/>
        ///     followed by either '\' or '/'.
        /// </remarks>
        /// <param name="name">The exact name of the directory.</param>
        /// <param name="recursive">A value indicating whether descendant directories should be included.</param>
        /// <returns>
        ///     The directories named <paramref name="name"/>, then descendants, each in the order of <see cref="Directories"/>.
        /// </returns>
        public IReadOnlyList<BrowseCacheDirectory> Find(string name, bool recursive)
        {
            var index = Index.Value;
            var matches = new List<int>();

            index.AddExactMatches(name, matches);
            matches.Sort();

            if (recursive)
            {
                var exactCount = matches.Count;

                index.AddPrefixMatches(name + '\\', matches);
                index.AddPrefixMatches(name + '/', matches);

                matches.Sort(exactCount, matches.Count - exactCount, comparer: null);
            }

            if (matches.Count == 0)
            {
                return Array.Empty<BrowseCacheDirectory>();
            }

            var directories = new BrowseCacheDirectory[matches.Count];

            for (var i = 0; i < directories.Length; i++)
            {
                directories[i] = Directories[matches[i]];
            }

            return directories;
        }

        /// <summary>
        ///     Directory names sorted ordinally, alongside their position in <see cref="Directories"/>.
        /// </summary>
        /// <remarks>
        ///     Every descendant of a directory shares the prefix '&lt;name&gt;\' or '&lt;name&gt;/', and ordinal sorting places
        ///     strings sharing a prefix in one contiguous range, so descendants are found with a binary search instead of
        ///     comparing every name.
        /// </remarks>
        private sealed class NameIndex
        {
            public NameIndex(BrowseCacheDirectory[] directories)
            {
                Names = new string[directories.Length];
                Positions = new int[directories.Length];

                for (var i = 0; i < directories.Length; i++)
                {
                    Names[i] = directories[i].Directory.Name ?? string.Empty;
                    Positions[i] = i;
                }

                Array.Sort(Names, Positions, StringComparer.Ordinal);

                FirstIndexByName = new Dictionary<string, int>(Names.Length, StringComparer.Ordinal);

                for (var i = 0; i < Names.Length; i++)
                {
                    FirstIndexByName.TryAdd(Names[i], i);
                }
            }

            private string[] Names { get; }
            private int[] Positions { get; }
            private Dictionary<string, int> FirstIndexByName { get; }

            public void AddExactMatches(string name, List<int> matches)
            {
                if (!FirstIndexByName.TryGetValue(name, out var first))
                {
                    return;
                }

                for (var i = first; i < Names.Length && string.Equals(Names[i], name, StringComparison.Ordinal); i++)
                {
                    matches.Add(Positions[i]);
                }
            }

            public void AddPrefixMatches(string prefix, List<int> matches)
            {
                // lower bound: the first name that sorts at or after the prefix
                var low = 0;
                var high = Names.Length;

                while (low < high)
                {
                    var middle = low + ((high - low) / 2);

                    if (string.CompareOrdinal(Names[middle], prefix) < 0)
                    {
                        low = middle + 1;
                    }
                    else
                    {
                        high = middle;
                    }
                }

                for (var i = low; i < Names.Length && Names[i].StartsWith(prefix, StringComparison.Ordinal); i++)
                {
                    matches.Add(Positions[i]);
                }
            }
        }
    }
}
