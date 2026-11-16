// <copyright file="SearchDbContext.cs" company="JP Dillingham">
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
    using Microsoft.EntityFrameworkCore;

    public class SearchDbContext : DbContext
    {
        public SearchDbContext(DbContextOptions<SearchDbContext> options)
            : base(options)
        {
        }

        public DbSet<Search> Searches { get; set; }

        /// <summary>
        ///     Gets or sets the responses of completed searches; see <see cref="ResponseRecord"/>.
        /// </summary>
        public DbSet<ResponseRecord> Responses { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<Search>()
                .Property(e => e.StartedAt)
                .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

            modelBuilder
                .Entity<Search>()
                .Property(e => e.EndedAt)
                .HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null);

            // covers every query of the Searches table: the search list, lookups by id, and pruning. responses used to be
            // stored in this table (see Z2026_11_16_SearchResponsesTableMigration), and the index predates their move
            modelBuilder
                .Entity<Search>()
                .HasIndex(e => new
                {
                    e.Id,
                    e.StartedAt,
                    e.EndedAt,
                    e.FileCount,
                    e.LockedFileCount,
                    e.ResponseCount,
                    e.SearchText,
                    e.State,
                    e.Token,
                })
                .HasDatabaseName("IDX_Searches_Summary");

            // the key leads with SearchId, so it serves both the cascade on delete and reading the responses of a search,
            // and rows are kept in order of arrival, which breaks ties in every sort order
            modelBuilder
                .Entity<ResponseRecord>(entity =>
                {
                    entity.ToTable("SearchResponses");
                    entity.HasKey(e => new { e.SearchId, e.Ordinal });
                    entity
                        .HasOne<Search>()
                        .WithMany()
                        .HasForeignKey(e => e.SearchId)
                        .OnDelete(DeleteBehavior.Cascade);
                });
        }
    }
}
