// <copyright file="ShareSearchQuery.cs" company="JP Dillingham">
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

namespace slskd.Shares
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Soulseek;

    /// <summary>
    ///     A <see cref="SearchQuery"/> prepared for execution against one or more share repositories.
    /// </summary>
    /// <remarks>
    ///     Create one instance per search and reuse it across repositories so that the full text MATCH expression and the
    ///     exclusion matcher are built at most once. Instances are not thread safe.
    /// </remarks>
    public sealed class ShareSearchQuery
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ShareSearchQuery"/> class.
        /// </summary>
        /// <param name="query">The search query to prepare.</param>
        public ShareSearchQuery(SearchQuery query)
        {
            Query = query;
            Exclusions = query.Exclusions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            // an empty exclusion is a substring of every filename, so nothing can match
            ExcludesEverything = Exclusions.Any(string.IsNullOrEmpty);
            Match = ExcludesEverything ? null : BuildMatch(query.Terms, Exclusions);
        }

        /// <summary>
        ///     Gets the original search query.
        /// </summary>
        public SearchQuery Query { get; }

        /// <summary>
        ///     Gets the FTS5 MATCH expression for the query, including token-level exclusions.
        /// </summary>
        public string Match { get; }

        /// <summary>
        ///     Gets a value indicating whether the query has any exclusions.
        /// </summary>
        public bool HasExclusions => Exclusions.Length > 0;

        /// <summary>
        ///     Gets a value indicating whether the exclusions exclude every possible filename.
        /// </summary>
        public bool ExcludesEverything { get; }

        private string[] Exclusions { get; }
        private SearchValues<string> ExclusionMatcher { get; set; }

        /// <summary>
        ///     Returns a value indicating whether the specified <paramref name="filename"/> contains any of the query's
        ///     exclusions as a (case insensitive) substring.
        /// </summary>
        /// <remarks>
        ///     The full text index excludes whole tokens only; this catches exclusions that appear inside a token, e.g. "bar"
        ///     within "foo xbarx baz".
        /// </remarks>
        /// <param name="filename">The filename to check.</param>
        /// <returns>A value indicating whether the filename is excluded.</returns>
        public bool IsExcluded(string filename)
        {
            if (Exclusions.Length == 0)
            {
                return false;
            }

            if (ExcludesEverything)
            {
                return true;
            }

            // built lazily; most incoming searches match nothing and never need it
            ExclusionMatcher ??= SearchValues.Create(Exclusions, StringComparison.OrdinalIgnoreCase);
            return filename.AsSpan().ContainsAny(ExclusionMatcher);
        }

        private static string BuildMatch(IEnumerable<string> terms, string[] exclusions)
        {
            var builder = new StringBuilder();

            builder.Append('(');
            AppendPhrases(builder, terms, " AND ");
            builder.Append(')');

            if (exclusions.Length > 0)
            {
                builder.Append(" NOT (");
                AppendPhrases(builder, exclusions, " OR ");
                builder.Append(')');
            }

            return builder.ToString();
        }

        private static void AppendPhrases(StringBuilder builder, IEnumerable<string> phrases, string separator)
        {
            var first = true;

            foreach (var phrase in phrases)
            {
                if (!first)
                {
                    builder.Append(separator);
                }

                first = false;
                builder.Append('"');

                // path separators, colons and quotes would be interpreted by FTS5 as syntax; replace them with token separators
                foreach (var c in phrase)
                {
                    switch (c)
                    {
                        case '/':
                        case '\\':
                        case ':':
                        case '"':
                            builder.Append(' ');
                            break;
                        case '\'':
                            builder.Append("''");
                            break;
                        default:
                            builder.Append(c);
                            break;
                    }
                }

                builder.Append('"');
            }
        }
    }
}
