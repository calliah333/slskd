// <copyright file="Search.cs" company="JP Dillingham">
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
    using System.ComponentModel.DataAnnotations;
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Soulseek;

    public record Search
    {
        public DateTime? EndedAt { get; set; }
        public int FileCount { get; set; }

        [Key]
        public Guid Id { get; init; }

        [NotMapped]
        public bool IsComplete => State.HasFlag(SearchStates.Completed);

        public int LockedFileCount { get; set; }
        public int ResponseCount { get; set; }

        [NotMapped]
        public IEnumerable<Response> Responses { get; set; } = new List<Response>();

        /// <summary>
        ///     Gets or sets the JSON representation of <see cref="Responses"/>, as stored in the database.
        /// </summary>
        /// <remarks>
        ///     EF Core reads this several times per save (snapshot, change detection, parameter), and the responses of a
        ///     large search serialize to tens of megabytes, so the result is remembered for as long as <see cref="Responses"/>
        ///     refers to the same collection. Assign a new collection rather than modifying the current one in place.
        /// </remarks>
        [JsonIgnore]
        public string ResponsesJson
        {
            get
            {
                var responses = Responses;
                var memo = serializedResponses;

                if (memo is not null && ReferenceEquals(memo.Source, responses))
                {
                    return memo.Json;
                }

                var json = JsonSerializer.Serialize(responses);
                serializedResponses = new SerializedResponses(responses, json);
                return json;
            }

            set
            {
                var responses = JsonSerializer.Deserialize<List<Response>>(value);
                Responses = responses;
                serializedResponses = new SerializedResponses(responses, value);
            }
        }

        public string SearchText { get; init; }
        public DateTime StartedAt { get; init; } = DateTime.Now;
        public SearchStates State { get; set; }
        public int Token { get; init; }

        // a reference type, so that the source and its serialization are read and written together
        private SerializedResponses serializedResponses;

        private sealed record SerializedResponses(object Source, string Json);
    }
}