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
///     Drops the single-column IDX_Transfers_Direction and IDX_Transfers_Removed indexes.
/// </summary>
/// <remarks>
///     <para>
///         IDX_Transfers_DirectionRemovedState leads with (Direction, Removed), so it answers anything the Direction index
///         could, and Removed alone is ~95% one value, which makes an index on it slower than reading the table. Both only
///         added work to every insert and state change.
///     </para>
///     <para>
///         Z2025_07_06_TransferIndexesMigration and Z2025_12_28_AdditionalTransferIndexesMigration treat the composite index
///         as satisfying their checks for these, so they don't recreate them on the next start.
///     </para>
/// </remarks>
public class Z2026_11_16_DropRedundantTransferIndexesMigration : IMigration
{
    private static readonly string[] Redundant = ["IDX_Transfers_Direction", "IDX_Transfers_Removed"];

    public Z2026_11_16_DropRedundantTransferIndexesMigration(ConnectionStringDictionary connectionStrings)
    {
        ConnectionString = connectionStrings[Database.Transfers];
    }

    private ILogger Log { get; } = Serilog.Log.ForContext<Z2026_11_16_DropRedundantTransferIndexesMigration>();
    private string ConnectionString { get; }

    public bool NeedsToBeApplied()
    {
        var indexes = SchemaInspector.GetDatabaseIndexes(ConnectionString)["Transfers"];

        // only drop once the composite that replaces them is in place
        var compositeExists = indexes.Any(i => i.Name.Equals("IDX_Transfers_DirectionRemovedState", StringComparison.OrdinalIgnoreCase));

        return compositeExists && indexes.Any(i => Redundant.Contains(i.Name, StringComparer.OrdinalIgnoreCase));
    }

    public void Apply()
    {
        if (!NeedsToBeApplied())
        {
            Log.Information("> Migration {Name} is not necessary or has already been applied", nameof(Z2026_11_16_DropRedundantTransferIndexesMigration));
            return;
        }

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using var transaction = connection.BeginTransaction();

        try
        {
            Log.Information("> Dropping redundant index(es) on the Transfers table...");

            foreach (var index in Redundant)
            {
                using var command = new SqliteCommand($"DROP INDEX IF EXISTS {index}", connection, transaction);
                command.ExecuteNonQuery();
            }

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
