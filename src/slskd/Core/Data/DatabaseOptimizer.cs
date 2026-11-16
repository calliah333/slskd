// <copyright file="Coalescer.cs" company="JP Dillingham">
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

namespace slskd;

using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Data.Sqlite;
using Serilog;

/// <summary>
///     Keeps the SQLite query planner's statistics current.
/// </summary>
/// <remarks>
///     <para>
///         Without statistics (sqlite_stat1), SQLite picks indexes by heuristics that ignore how selective they are. With
///         hundreds of thousands of transfers it then reads every row of a direction to find one user's transfers through
///         IDX_Transfers_DirectionRemovedState instead of seeking IDX_Transfers_UserUploadStatistics. EF's EnsureCreated
///         and the migrations never analyze, and 'PRAGMA optimize' only considers tables the current connection queried,
///         which on a fresh connection is none of them.
///     </para>
///     <para>
///         'PRAGMA optimize=0x10002' checks every table and analyzes those that have never been analyzed or have changed
///         a lot since; it's close to free when nothing changed, so it's run once in the background at startup and then
///         hourly.
///     </para>
/// </remarks>
public static class DatabaseOptimizer
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(DatabaseOptimizer));
    private static readonly SemaphoreSlim SyncRoot = new(1, 1);

    /// <summary>
    ///     Analyzes the tables of each of the specified <paramref name="databases"/> that need it.
    /// </summary>
    /// <remarks>Never throws; failures are logged. Concurrent calls are skipped rather than queued.</remarks>
    /// <param name="databases">The databases to optimize.</param>
    public static void Optimize(ConnectionStringDictionary databases)
    {
        if (!SyncRoot.Wait(0))
        {
            return;
        }

        try
        {
            foreach (var database in Database.List)
            {
                try
                {
                    var sw = Stopwatch.StartNew();

                    using var connection = new SqliteConnection(databases[database]);
                    connection.Open();

                    using var command = connection.CreateCommand();
                    command.CommandText = "PRAGMA optimize=0x10002;";
                    command.ExecuteNonQuery();

                    Log.Debug("Optimized database {Database} in {Duration}ms", database.Name, sw.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to optimize database {Database}: {Message}", database.Name, ex.Message);
                }
            }
        }
        finally
        {
            SyncRoot.Release();
        }
    }
}
