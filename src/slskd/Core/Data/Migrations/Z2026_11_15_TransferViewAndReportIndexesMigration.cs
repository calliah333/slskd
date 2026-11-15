// <copyright file="Z2026_11_15_TransferViewAndReportIndexesMigration.cs" company="JP Dillingham">
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

namespace slskd.Migrations;

using System;
using System.Linq;
using Microsoft.Data.Sqlite;
using Serilog;

/// <summary>
///     Adds indexes to the Transfers table backing the paged transfer view (counts by state and ordering by end time)
///     and the transfer reports (a covering index for summaries, histograms, and leaderboards).
/// </summary>
public class Z2026_11_15_TransferViewAndReportIndexesMigration : IMigration
{
    public Z2026_11_15_TransferViewAndReportIndexesMigration(ConnectionStringDictionary connectionStrings)
    {
        ConnectionString = connectionStrings[Database.Transfers];
    }

    private ILogger Log { get; } = Serilog.Log.ForContext<Z2026_11_15_TransferViewAndReportIndexesMigration>();
    private string ConnectionString { get; }

    public bool NeedsToBeApplied()
    {
        // check to see if *ALL* of the indexes are in place. if any are missing, we must apply
        var idxes = SchemaInspector.GetDatabaseIndexes(ConnectionString);
        var txfers = idxes["Transfers"];

        var stateExists = txfers.Any(c => c.Name.Equals("IDX_Transfers_DirectionRemovedState", StringComparison.OrdinalIgnoreCase));
        var endedAtExists = txfers.Any(c => c.Name.Equals("IDX_Transfers_DirectionRemovedEndedAt", StringComparison.OrdinalIgnoreCase));
        var reportsExists = txfers.Any(c => c.Name.Equals("IDX_Transfers_Reports", StringComparison.OrdinalIgnoreCase));

        if (stateExists && endedAtExists && reportsExists)
        {
            return false;
        }

        return true;
    }

    public void Apply()
    {
        if (!NeedsToBeApplied())
        {
            Log.Information("> Migration {Name} is not necessary or has already been applied", nameof(Z2026_11_15_TransferViewAndReportIndexesMigration));
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

            Log.Information("> Adding missing index(es) on the Transfers table...");

            Exec("CREATE INDEX IF NOT EXISTS IDX_Transfers_DirectionRemovedState ON Transfers (Direction, Removed, State)");
            Exec("CREATE INDEX IF NOT EXISTS IDX_Transfers_DirectionRemovedEndedAt ON Transfers (Direction, Removed, EndedAt, Id, State)");
            Exec("CREATE INDEX IF NOT EXISTS IDX_Transfers_Reports ON Transfers (State, EndedAt, Direction, Username, Size, AverageSpeed, StartedAt, EnqueuedAt)");

            Log.Information("> Index(es) created");
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
