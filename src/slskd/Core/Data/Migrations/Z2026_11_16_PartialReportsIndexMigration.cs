// <copyright file="SearchService.cs" company="JP Dillingham">
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

namespace slskd.Migrations;

using System;
using System.Linq;
using Microsoft.Data.Sqlite;
using Serilog;

/// <summary>
///     Rebuilds IDX_Transfers_Reports as a partial index over ended transfers only.
/// </summary>
/// <remarks>
///     <para>
///         The index covers AverageSpeed, which every progress update of an in-progress transfer rewrites, so each
///         progress write also rewrote an entry of this 8-column index. Every report query filters on
///         'EndedAt IS NOT NULL', so excluding transfers that haven't ended loses nothing and takes the index off the
///         progress-update path entirely.
///     </para>
///     <para>
///         Z2026_11_15_TransferViewAndReportIndexesMigration only checks that the index exists by name, so the two
///         don't fight: after this runs, that one still sees the index and skips.
///     </para>
/// </remarks>
public class Z2026_11_16_PartialReportsIndexMigration : IMigration
{
    public Z2026_11_16_PartialReportsIndexMigration(ConnectionStringDictionary connectionStrings)
    {
        ConnectionString = connectionStrings[Database.Transfers];
    }

    private ILogger Log { get; } = Serilog.Log.ForContext<Z2026_11_16_PartialReportsIndexMigration>();
    private string ConnectionString { get; }

    public bool NeedsToBeApplied()
    {
        var reports = SchemaInspector.GetDatabaseIndexes(ConnectionString)["Transfers"]
            .FirstOrDefault(i => i.Name.Equals("IDX_Transfers_Reports", StringComparison.OrdinalIgnoreCase));

        return reports is null || !reports.Partial;
    }

    public void Apply()
    {
        if (!NeedsToBeApplied())
        {
            Log.Information("> Migration {Name} is not necessary or has already been applied", nameof(Z2026_11_16_PartialReportsIndexMigration));
            return;
        }

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            void Exec(string sql)
            {
                using var command = new SqliteCommand(sql, connection, transaction);
                command.ExecuteNonQuery();
            }

            Log.Information("> Rebuilding IDX_Transfers_Reports as a partial index...");

            Exec("DROP INDEX IF EXISTS IDX_Transfers_Reports");
            Exec("CREATE INDEX IDX_Transfers_Reports ON Transfers (State, EndedAt, Direction, Username, Size, AverageSpeed, StartedAt, EnqueuedAt) WHERE EndedAt IS NOT NULL");

            transaction.Commit();
            Log.Information("> Done!");
        }
        catch (Exception)
        {
            transaction.Rollback();
            throw;
        }
    }
}
