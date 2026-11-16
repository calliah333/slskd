// <copyright file="RevisionTag.cs" company="JP Dillingham">
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

namespace slskd.Core.API
{
    using System.Linq;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Net.Http.Headers;

    /// <summary>
    ///     Revision-based conditional GET support: the revision of the requested state is sent as an ETag, and a request
    ///     whose If-None-Match carries the current revision can be answered with 304 Not Modified and no body.
    /// </summary>
    public static class RevisionTag
    {
        /// <summary>
        ///     Sets the ETag (and a Cache-Control requiring revalidation) for the specified <paramref name="revision"/> on
        ///     the response, and returns a value indicating whether the request's If-None-Match already matches it.
        /// </summary>
        /// <remarks>
        ///     Read the revision before reading the state it describes; a concurrent change then produces, at worst, a
        ///     fresh body with an old tag (refetched next time) and never a stale body with a new tag.
        /// </remarks>
        /// <param name="context">The HTTP context of the request.</param>
        /// <param name="revision">The current revision of the requested state.</param>
        /// <returns>A value indicating whether the client already has the current revision.</returns>
        public static bool IsNotModified(HttpContext context, long revision)
        {
            var tag = new EntityTagHeaderValue($"\"{revision}\"");

            var responseHeaders = context.Response.GetTypedHeaders();
            responseHeaders.ETag = tag;
            responseHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };

            var ifNoneMatch = context.Request.GetTypedHeaders().IfNoneMatch;

            return ifNoneMatch != null && ifNoneMatch.Any(candidate => candidate.Compare(tag, useStrongComparison: false));
        }
    }
}
