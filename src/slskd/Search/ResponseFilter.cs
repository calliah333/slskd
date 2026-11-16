// <copyright file="ResponseFilter.cs" company="JP Dillingham">
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

namespace slskd.Search
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;

    /// <summary>
    ///     A filter over the files of search responses, parsed from the filter syntax used by the web UI
    ///     (e.g. <c>lackluster container -bothersome iscbr islossless minbitrate:320 minfilesinfolder:8</c>).
    /// </summary>
    /// <remarks>
    ///     Missing (null) file attributes never fail a minimum check, matching the web UI, which receives them as undefined.
    /// </remarks>
    public sealed class ResponseFilter
    {
        private static readonly HashSet<string> FlagTerms = new(StringComparer.Ordinal) { "isvbr", "iscbr", "islossless", "islossy" };

        // [0-9] rather than \d, which also matches non-ASCII digits in .NET (but not in JavaScript)
        private static readonly Regex MinBitRateRegex = new(@"(minbr|minbitrate):([0-9]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex MinBitDepthRegex = new(@"(minbd|minbitdepth):([0-9]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex MinFileSizeRegex = new(@"(minfs|minfilesize):([0-9]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex MinLengthRegex = new(@"(minlen|minlength):([0-9]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly Regex MinFilesInFolderRegex = new(@"(minfif|minfilesinfolder):([0-9]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        ///     Gets a filter which matches every file.
        /// </summary>
        public static ResponseFilter None { get; } = new ResponseFilter();

        /// <summary>
        ///     Gets the terms which must not appear in a filename (case insensitive).
        /// </summary>
        public IReadOnlyList<string> Exclude { get; init; } = [];

        /// <summary>
        ///     Gets the terms which must all appear in a filename (case insensitive).
        /// </summary>
        public IReadOnlyList<string> Include { get; init; } = [];

        /// <summary>
        ///     Gets a value indicating whether only constant bit rate files match.
        /// </summary>
        public bool IsCBR { get; init; }

        /// <summary>
        ///     Gets a value indicating whether only lossless files (with a sample rate and bit depth) match.
        /// </summary>
        public bool IsLossless { get; init; }

        /// <summary>
        ///     Gets a value indicating whether only lossy files (without a sample rate or bit depth) match.
        /// </summary>
        public bool IsLossy { get; init; }

        /// <summary>
        ///     Gets a value indicating whether only variable bit rate files match.
        /// </summary>
        public bool IsVBR { get; init; }

        /// <summary>
        ///     Gets the minimum bit depth.
        /// </summary>
        public long MinBitDepth { get; init; }

        /// <summary>
        ///     Gets the minimum bit rate.
        /// </summary>
        public long MinBitRate { get; init; }

        /// <summary>
        ///     Gets the minimum number of files a response must contain, before filtering, for any of its files to match.
        /// </summary>
        public long MinFilesInFolder { get; init; }

        /// <summary>
        ///     Gets the minimum file size, in bytes.
        /// </summary>
        public long MinFileSize { get; init; }

        /// <summary>
        ///     Gets the minimum length, in seconds.
        /// </summary>
        public long MinLength { get; init; }

        /// <summary>
        ///     Parses a filter from the specified <paramref name="text"/>.
        /// </summary>
        /// <param name="text">The filter text.</param>
        /// <returns>The parsed filter.</returns>
        public static ResponseFilter Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return None;
            }

            // terms are split on single spaces; anything containing a colon is an option (known or not), and the flags are
            // excluded only when they appear verbatim. note that a lone '-' yields an empty exclusion, which excludes everything.
            var terms = text.ToLowerInvariant()
                .Split(' ')
                .Where(term => !term.Contains(':') && !FlagTerms.Contains(term))
                .ToList();

            return new ResponseFilter
            {
                MinBitRate = GetNumber(MinBitRateRegex, text),
                MinBitDepth = GetNumber(MinBitDepthRegex, text),
                MinFileSize = GetNumber(MinFileSizeRegex, text),
                MinLength = GetNumber(MinLengthRegex, text),
                MinFilesInFolder = GetNumber(MinFilesInFolderRegex, text),

                // flags are recognized anywhere in the text
                IsVBR = text.Contains("isvbr", StringComparison.OrdinalIgnoreCase),
                IsCBR = text.Contains("iscbr", StringComparison.OrdinalIgnoreCase),
                IsLossless = text.Contains("islossless", StringComparison.OrdinalIgnoreCase),
                IsLossy = text.Contains("islossy", StringComparison.OrdinalIgnoreCase),

                // an empty include term matches everything, so drop them
                Include = terms.Where(term => term.Length > 0 && !term.StartsWith('-')).ToList(),
                Exclude = terms.Where(term => term.StartsWith('-')).Select(term => term[1..]).ToList(),
            };
        }

        /// <summary>
        ///     Determines whether the specified <paramref name="response"/> has at least one file that matches this filter.
        /// </summary>
        /// <remarks>
        ///     Equivalent to checking that the result of <see cref="Apply"/> is not null, without allocating.
        /// </remarks>
        /// <param name="response">The response to check.</param>
        /// <param name="hideLocked">A value indicating whether locked files are to be disregarded.</param>
        /// <returns>A value indicating whether the response has at least one matching file.</returns>
        public bool Matches(Response response, bool hideLocked)
        {
            if (!MeetsMinFilesInFolder(response, hideLocked))
            {
                return false;
            }

            if (response.Files.Any(Matches))
            {
                return true;
            }

            return !hideLocked && response.LockedFiles.Any(Matches);
        }

        /// <summary>
        ///     Returns a copy of the specified <paramref name="response"/> containing only the files which match this filter,
        ///     with file counts adjusted accordingly, or null if no files match.
        /// </summary>
        /// <param name="response">The response to filter.</param>
        /// <param name="hideLocked">A value indicating whether locked files are to be removed.</param>
        /// <returns>The filtered response, or null if no files match.</returns>
        public Response Apply(Response response, bool hideLocked)
        {
            if (!MeetsMinFilesInFolder(response, hideLocked))
            {
                return null;
            }

            var files = response.Files.Where(Matches).ToList();
            var lockedFiles = hideLocked ? new List<File>() : response.LockedFiles.Where(Matches).ToList();

            if (files.Count + lockedFiles.Count == 0)
            {
                return null;
            }

            // nothing was removed and the counts agree; hand back the original
            if (files.Count == response.Files.Count
                && lockedFiles.Count == response.LockedFiles.Count
                && response.FileCount == files.Count
                && response.LockedFileCount == lockedFiles.Count)
            {
                return response;
            }

            return new Response
            {
                FileCount = files.Count,
                Files = files,
                HasFreeUploadSlot = response.HasFreeUploadSlot,
                LockedFileCount = lockedFiles.Count,
                LockedFiles = lockedFiles,
                QueueLength = response.QueueLength,
                Token = response.Token,
                UploadSpeed = response.UploadSpeed,
                Username = response.Username,
            };
        }

        /// <summary>
        ///     Determines whether the specified <paramref name="file"/> matches this filter.
        /// </summary>
        /// <param name="file">The file to check.</param>
        /// <returns>A value indicating whether the file matches.</returns>
        public bool Matches(File file)
        {
            if (IsCBR && file.IsVariableBitRate != false)
            {
                return false;
            }

            if (IsVBR && file.IsVariableBitRate != true)
            {
                return false;
            }

            if (IsLossless && ((file.SampleRate ?? 0) == 0 || (file.BitDepth ?? 0) == 0))
            {
                return false;
            }

            if (IsLossy && ((file.SampleRate ?? 0) != 0 || (file.BitDepth ?? 0) != 0))
            {
                return false;
            }

            // lifted comparisons are false when the attribute is missing, so missing attributes pass
            if (file.BitRate < MinBitRate || file.BitDepth < MinBitDepth || file.Size < MinFileSize || file.Length < MinLength)
            {
                return false;
            }

            if (Include.Count > 0 || Exclude.Count > 0)
            {
                var filename = file.Filename ?? string.Empty;

                foreach (var term in Include)
                {
                    if (!filename.Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                foreach (var term in Exclude)
                {
                    if (filename.Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static long GetNumber(Regex regex, string text)
        {
            var match = regex.Match(text);

            if (!match.Success)
            {
                return 0;
            }

            // the pattern only admits digits, so the only way to fail is overflow; treat that as 'as large as possible'
            return long.TryParse(match.Groups[2].ValueSpan, out var value) ? value : long.MaxValue;
        }

        private bool MeetsMinFilesInFolder(Response response, bool hideLocked)
            => response.FileCount + (hideLocked ? 0 : response.LockedFileCount) >= MinFilesInFolder;
    }
}
