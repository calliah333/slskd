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
    ///     The largest groups of a set of transfers.
    /// </summary>
    public record TransferSummaryResponse
    {
        /// <summary>
        ///     Gets the applied grouping; user, directory, or file.
        /// </summary>
        public string By { get; init; }

        /// <summary>
        ///     Gets the applied sort; users, transfers, bytes, or recent.
        /// </summary>
        public string Sort { get; init; }

        /// <summary>
        ///     Gets the applied filter.
        /// </summary>
        public string Filter { get; init; }

        /// <summary>
        ///     Gets the applied username, if any.
        /// </summary>
        public string Username { get; init; }

        /// <summary>
        ///     Gets the applied search text, if any.
        /// </summary>
        public string Search { get; init; }

        /// <summary>
        ///     Gets the maximum number of groups returned.
        /// </summary>
        public int Limit { get; init; }

        /// <summary>
        ///     Gets the number of groups, including those not returned.
        /// </summary>
        public int Groups { get; init; }

        /// <summary>
        ///     Gets the number of transfers in all groups.
        /// </summary>
        public int Transfers { get; init; }

        /// <summary>
        ///     Gets the returned groups, in order.
        /// </summary>
        public IEnumerable<TransferSummaryRow> Rows { get; init; }
    }
}
