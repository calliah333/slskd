// <copyright file="Z2026_11_16_SearchResponsesTableMigration.cs" company="JP Dillingham">
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
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using Serilog;

/// <summary>
///     Moves the responses of each search out of the Searches.ResponsesJson column, a single JSON document that can be tens
///     of megabytes, into the SearchResponses table, one row per response, then drops the column.
/// </summary>
/// <remarks>
///     <para>
///         The JSON is unpacked by SQLite (json_each), one search at a time, so no search is ever loaded into the
///         application. Everything happens in one transaction, so the migration either completes or leaves the database as
///         it was; its state is told by the presence of the ResponsesJson column.
///     </para>
///     <para>
///         The column is dropped rather than left in place (and unused) so that the space it occupies can be reclaimed,
///         and so that nothing can go on reading or writing it. The table is rewritten in the process, which is cheap
///         once the responses are gone. Searches whose JSON is malformed lose their responses, which could never be read
///         anyway.
///     </para>
/// </remarks>
public class Z2026_11_16_SearchResponsesTableMigration : IMigration
{
    /// <summary>
    ///     The definition of the SearchResponses table, identical to the one created from the EF model.
    /// </summary>
    public const string CreateTableSql = """
        CREATE TABLE IF NOT EXISTS "SearchResponses" (
            "SearchId" TEXT NOT NULL,
            "Ordinal" INTEGER NOT NULL,
            "Username" TEXT NULL,
            "Token" INTEGER NOT NULL,
            "HasFreeUploadSlot" INTEGER NOT NULL,
            "UploadSpeed" INTEGER NOT NULL,
            "QueueLength" INTEGER NOT NULL,
            "FileCount" INTEGER NOT NULL,
            "LockedFileCount" INTEGER NOT NULL,
            "FilesJson" TEXT NULL,
            "LockedFilesJson" TEXT NULL,
            CONSTRAINT "PK_SearchResponses" PRIMARY KEY ("SearchId", "Ordinal"),
            CONSTRAINT "FK_SearchResponses_Searches_SearchId" FOREIGN KEY ("SearchId") REFERENCES "Searches" ("Id") ON DELETE CASCADE
        );
        """;

    // empty lists are stored as NULL (see ResponseRecord), which json_extract also yields for missing or null lists
    private const string MoveSql = """
        INSERT OR REPLACE INTO SearchResponses
            (SearchId, Ordinal, Username, Token, HasFreeUploadSlot, UploadSpeed, QueueLength, FileCount, LockedFileCount, FilesJson, LockedFilesJson)
        SELECT
            s.Id,
            r.key,
            json_extract(r.value, '$.Username'),
            COALESCE(json_extract(r.value, '$.Token'), 0),
            COALESCE(json_extract(r.value, '$.HasFreeUploadSlot'), 0),
            COALESCE(json_extract(r.value, '$.UploadSpeed'), 0),
            COALESCE(json_extract(r.value, '$.QueueLength'), 0),
            COALESCE(json_extract(r.value, '$.FileCount'), 0),
            COALESCE(json_extract(r.value, '$.LockedFileCount'), 0),
            NULLIF(json_extract(r.value, '$.Files'), '[]'),
            NULLIF(json_extract(r.value, '$.LockedFiles'), '[]')
        FROM Searches s, json_each(s.ResponsesJson) r
        WHERE s.rowid = $rowid
            AND json_valid(s.ResponsesJson)
            AND json_type(s.ResponsesJson) = 'array'
            AND r.type = 'object';
        """;

    public Z2026_11_16_SearchResponsesTableMigration(ConnectionStringDictionary connectionStrings)
    {
        ConnectionString = connectionStrings[Database.Search];
    }

    private ILogger Log { get; } = Serilog.Log.ForContext<Z2026_11_16_SearchResponsesTableMigration>();
    private string ConnectionString { get; }

    public bool NeedsToBeApplied()
    {
        var schema = SchemaInspector.GetDatabaseSchema(ConnectionString);

        if (!schema.TryGetValue("Searches", out var columns))
        {
            // the tables are created from the EF model; nothing to migrate
            return false;
        }

        return columns.Any(c => c.Name.Equals("ResponsesJson", StringComparison.OrdinalIgnoreCase));
    }

    public void Apply()
    {
        if (!NeedsToBeApplied())
        {
            Log.Information("> Migration {Name} is not necessary or has already been applied", nameof(Z2026_11_16_SearchResponsesTableMigration));
            return;
        }

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();

        using (var transaction = connection.BeginTransaction())
        {
            try
            {
                void Exec(string sql)
                {
                    using var command = new SqliteCommand(sql, connection, transaction);
                    command.ExecuteNonQuery();
                }

                Log.Information("> Creating the SearchResponses table...");
                Exec(CreateTableSql);

                var rowids = new List<long>();

                using (var select = new SqliteCommand("SELECT rowid FROM Searches WHERE ResponsesJson IS NOT NULL AND ResponsesJson <> '[]';", connection, transaction))
                using (var reader = select.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        rowids.Add(reader.GetInt64(0));
                    }
                }

                Log.Information("> Moving the responses of {Count} searches...", rowids.Count);

                using var move = new SqliteCommand(MoveSql, connection, transaction);
                var rowid = move.Parameters.Add("$rowid", SqliteType.Integer);

                long moved = 0;

                for (var i = 0; i < rowids.Count; i++)
                {
                    rowid.Value = rowids[i];
                    moved += move.ExecuteNonQuery();

                    if ((i + 1) % 100 == 0)
                    {
                        Log.Information("> Moved {Responses} responses of {Done}/{Total} searches", moved, i + 1, rowids.Count);
                    }
                }

                Log.Information("> Moved {Responses} responses; dropping the ResponsesJson column...", moved);
                Exec("ALTER TABLE Searches DROP COLUMN ResponsesJson;");

                transaction.Commit();
            }
            catch (Exception)
            {
                transaction.Rollback();
                throw;
            }
        }

        // the migration is complete; reclaiming the space the column occupied is nice to have, and can't be done in a
        // transaction. it needs as much free disk space as the database occupies, which may not be available
        try
        {
            Log.Information("> Reclaiming free space...");

            using var vacuum = new SqliteCommand("VACUUM;", connection);
            vacuum.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "> Failed to reclaim free space in the search database: {Message}", ex.Message);
        }

        Log.Information("> Done!");
    }
}
