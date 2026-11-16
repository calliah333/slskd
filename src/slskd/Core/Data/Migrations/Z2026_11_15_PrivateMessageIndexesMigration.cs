// <copyright file="Z2026_11_15_PrivateMessageIndexesMigration.cs" company="JP Dillingham">
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
///     Adds indexes to the PrivateMessages table backing unacknowledged message counts (conversation listing) and
///     recent message history, and drops the single-column Username index they (and the primary key) supersede.
/// </summary>
public class Z2026_11_15_PrivateMessageIndexesMigration : IMigration
{
    public Z2026_11_15_PrivateMessageIndexesMigration(ConnectionStringDictionary connectionStrings)
    {
        ConnectionString = connectionStrings[Database.Messaging];
    }

    private ILogger Log { get; } = Serilog.Log.ForContext<Z2026_11_15_PrivateMessageIndexesMigration>();
    private string ConnectionString { get; }

    public bool NeedsToBeApplied()
    {
        var idxes = SchemaInspector.GetDatabaseIndexes(ConnectionString);
        var pms = idxes["PrivateMessages"];

        bool Exists(string name) => pms.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        // apply if either new index is missing, or the superseded one is still present
        return !Exists("IDX_PrivateMessages_IsAcknowledgedUsername")
            || !Exists("IDX_PrivateMessages_UsernameTimestamp")
            || Exists("IX_PrivateMessages_Username");
    }

    public void Apply()
    {
        if (!NeedsToBeApplied())
        {
            Log.Information("> Migration {Name} is not necessary or has already been applied", nameof(Z2026_11_15_PrivateMessageIndexesMigration));
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

            Log.Information("> Updating index(es) on the PrivateMessages table...");

            Exec("CREATE INDEX IF NOT EXISTS IDX_PrivateMessages_IsAcknowledgedUsername ON PrivateMessages (IsAcknowledged, Username)");
            Exec("CREATE INDEX IF NOT EXISTS IDX_PrivateMessages_UsernameTimestamp ON PrivateMessages (Username, Timestamp)");
            Exec("DROP INDEX IF EXISTS IX_PrivateMessages_Username");

            Log.Information("> Index(es) updated");
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
