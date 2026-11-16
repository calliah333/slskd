// <copyright file="ResponsePaging.cs" company="JP Dillingham">
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

    /// <summary>
    ///     The order in which search responses are paged.
    /// </summary>
    public enum ResponseSortOrder
    {
        /// <summary>
        ///     Fastest upload speed first.
        /// </summary>
        UploadSpeed = 0,

        /// <summary>
        ///     Shortest queue first.
        /// </summary>
        QueueLength = 1,
    }

    /// <summary>
    ///     A query over the responses of a search.
    /// </summary>
    public record ResponseQuery
    {
        /// <summary>
        ///     Gets the file filter, in the syntax understood by <see cref="ResponseFilter.Parse"/>.
        /// </summary>
        public string Filter { get; init; }

        /// <summary>
        ///     Gets the sort order.
        /// </summary>
        public ResponseSortOrder SortBy { get; init; } = ResponseSortOrder.UploadSpeed;

        /// <summary>
        ///     Gets a value indicating whether locked files are to be removed from responses.
        /// </summary>
        public bool HideLocked { get; init; }

        /// <summary>
        ///     Gets a value indicating whether responses from users without a free upload slot are to be omitted.
        /// </summary>
        public bool HideNoFreeSlots { get; init; }

        /// <summary>
        ///     Gets the usernames whose responses are to be omitted.
        /// </summary>
        public IReadOnlyCollection<string> HiddenUsernames { get; init; } = [];

        /// <summary>
        ///     Gets the number of matching responses to skip.
        /// </summary>
        public int Offset { get; init; }

        /// <summary>
        ///     Gets the maximum number of responses to return.
        /// </summary>
        public int Limit { get; init; } = 100;
    }

    /// <summary>
    ///     A page of search responses.
    /// </summary>
    public record ResponsePage
    {
        /// <summary>
        ///     Gets the total number of responses to the search, before filtering.
        /// </summary>
        public int TotalCount { get; init; }

        /// <summary>
        ///     Gets the number of responses matching the query.
        /// </summary>
        public int MatchedCount { get; init; }

        /// <summary>
        ///     Gets the offset of the first response in the page.
        /// </summary>
        public int Offset { get; init; }

        /// <summary>
        ///     Gets the requested page size.
        /// </summary>
        public int Limit { get; init; }

        /// <summary>
        ///     Gets the responses in the page, with files filtered according to the query.
        /// </summary>
        public IReadOnlyList<Response> Responses { get; init; } = [];
    }

    /// <summary>
    ///     Filters, sorts, and pages search responses.
    /// </summary>
    public static class ResponsePaging
    {
        /// <summary>
        ///     Returns the page of <paramref name="responses"/> described by the specified <paramref name="query"/>.
        /// </summary>
        /// <remarks>
        ///     Responses with no matching files are omitted. Sorting is stable, so responses that compare equal keep the
        ///     order in which they were received and successive pages of the same query neither overlap nor skip.
        /// </remarks>
        /// <param name="responses">The responses to page.</param>
        /// <param name="query">The query.</param>
        /// <returns>The requested page.</returns>
        public static ResponsePage Page(IReadOnlyList<Response> responses, ResponseQuery query)
        {
            ArgumentNullException.ThrowIfNull(responses);
            ArgumentNullException.ThrowIfNull(query);
            ArgumentOutOfRangeException.ThrowIfNegative(query.Offset);
            ArgumentOutOfRangeException.ThrowIfNegative(query.Limit);

            var filter = ResponseFilter.Parse(query.Filter);

            HashSet<string> hidden = query.HiddenUsernames is { Count: > 0 }
                ? new HashSet<string>(query.HiddenUsernames, StringComparer.Ordinal)
                : null;

            // decide membership without copying anything; only the responses in the page are materialized
            var matched = new List<Response>();

            foreach (var response in responses)
            {
                if (hidden is not null && hidden.Contains(response.Username))
                {
                    continue;
                }

                if (query.HideNoFreeSlots && !response.HasFreeUploadSlot)
                {
                    continue;
                }

                if (filter.Matches(response, query.HideLocked))
                {
                    matched.Add(response);
                }
            }

            IEnumerable<Response> sorted = query.SortBy == ResponseSortOrder.QueueLength
                ? matched.OrderBy(r => r.QueueLength)
                : matched.OrderByDescending(r => r.UploadSpeed);

            var page = sorted
                .Skip(query.Offset)
                .Take(query.Limit)
                .Select(r => filter.Apply(r, query.HideLocked))
                .ToList();

            return new ResponsePage
            {
                TotalCount = responses.Count,
                MatchedCount = matched.Count,
                Offset = query.Offset,
                Limit = query.Limit,
                Responses = page,
            };
        }
    }
}
