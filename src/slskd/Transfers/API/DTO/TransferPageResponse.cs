// <copyright file="TransferPageResponse.cs" company="JP Dillingham">
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


namespace slskd.Transfers.API
{
    using System.Collections.Generic;

    /// <summary>
    ///     A page of transfers.
    /// </summary>
    public record TransferPageResponse
    {
        /// <summary>
        ///     Gets the applied filter.
        /// </summary>
        public string Filter { get; init; }

        /// <summary>
        ///     Gets the number of matching transfers skipped.
        /// </summary>
        public int Offset { get; init; }

        /// <summary>
        ///     Gets the maximum number of transfers on the page.
        /// </summary>
        public int Limit { get; init; }

        /// <summary>
        ///     Gets the number of transfers matching the filter.
        /// </summary>
        public int Total { get; init; }

        /// <summary>
        ///     Gets the number of transfers in each state, regardless of the filter, keyed by state as it appears on a transfer.
        /// </summary>
        public IDictionary<string, int> Counts { get; init; }

        /// <summary>
        ///     Gets the transfers on the page, grouped by user and directory.
        /// </summary>
        public IEnumerable<UserResponse> Users { get; init; }
    }
}
