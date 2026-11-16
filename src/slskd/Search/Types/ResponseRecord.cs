// <copyright file="ResponseRecord.cs" company="JP Dillingham">
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
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    ///     A response to a search, as stored in the SearchResponses table.
    /// </summary>
    /// <remarks>
    ///     The attributes responses are hidden and sorted by have columns of their own, so that pages can be selected without
    ///     reading any files. Files are only ever matched against a filter in memory (filename matching is ordinal and case
    ///     insensitive, which SQL can't express exactly), so they are kept as compact JSON, one document per list.
    /// </remarks>
    public class ResponseRecord
    {
        private static readonly JsonSerializerOptions FileSerializerOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>
        ///     Gets the unique identifier of the search to which this is a response.
        /// </summary>
        public Guid SearchId { get; init; }

        /// <summary>
        ///     Gets the position of this response among the responses to the search, in order of arrival (zero based).
        /// </summary>
        public int Ordinal { get; init; }

        /// <summary>
        ///     Gets the username of the responding user.
        /// </summary>
        public string Username { get; init; }

        /// <summary>
        ///     Gets the token of the response.
        /// </summary>
        public int Token { get; init; }

        /// <summary>
        ///     Gets a value indicating whether the responding user has a free upload slot.
        /// </summary>
        public bool HasFreeUploadSlot { get; init; }

        /// <summary>
        ///     Gets the upload speed of the responding user.
        /// </summary>
        public int UploadSpeed { get; init; }

        /// <summary>
        ///     Gets the queue length of the responding user.
        /// </summary>
        public long QueueLength { get; init; }

        /// <summary>
        ///     Gets the number of files reported by the response.
        /// </summary>
        public int FileCount { get; init; }

        /// <summary>
        ///     Gets the number of locked files reported by the response.
        /// </summary>
        public int LockedFileCount { get; init; }

        /// <summary>
        ///     Gets the JSON array of the files of the response, or null if there are none.
        /// </summary>
        public string FilesJson { get; init; }

        /// <summary>
        ///     Gets the JSON array of the locked files of the response, or null if there are none.
        /// </summary>
        public string LockedFilesJson { get; init; }

        /// <summary>
        ///     Creates a record of the specified <paramref name="response"/>.
        /// </summary>
        /// <param name="searchId">The unique identifier of the search to which the response belongs.</param>
        /// <param name="ordinal">The position of the response among the responses to the search.</param>
        /// <param name="response">The response.</param>
        /// <returns>The record.</returns>
        public static ResponseRecord FromResponse(Guid searchId, int ordinal, Response response)
        {
            return new ResponseRecord
            {
                SearchId = searchId,
                Ordinal = ordinal,
                Username = response.Username,
                Token = response.Token,
                HasFreeUploadSlot = response.HasFreeUploadSlot,
                UploadSpeed = response.UploadSpeed,
                QueueLength = response.QueueLength,
                FileCount = response.FileCount,
                LockedFileCount = response.LockedFileCount,
                FilesJson = SerializeFiles(response.Files),
                LockedFilesJson = SerializeFiles(response.LockedFiles),
            };
        }

        /// <summary>
        ///     Creates the response described by this record.
        /// </summary>
        /// <returns>The response.</returns>
        public Response ToResponse()
        {
            return new Response
            {
                Username = Username,
                Token = Token,
                HasFreeUploadSlot = HasFreeUploadSlot,
                UploadSpeed = UploadSpeed,
                QueueLength = QueueLength,
                FileCount = FileCount,
                LockedFileCount = LockedFileCount,
                Files = DeserializeFiles(FilesJson),
                LockedFiles = DeserializeFiles(LockedFilesJson),
            };
        }

        private static string SerializeFiles(ICollection<File> files)
            => files is null || files.Count == 0 ? null : JsonSerializer.Serialize(files, FileSerializerOptions);

        private static List<File> DeserializeFiles(string json)
            => string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<File>>(json, FileSerializerOptions) ?? [];
    }
}
