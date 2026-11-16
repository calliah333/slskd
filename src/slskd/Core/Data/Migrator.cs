// <copyright file="Migrator.cs" company="JP Dillingham">
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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Serilog;
using slskd.Migrations;

/// <summary>
///     Applies database migrations.
/// </summary>
public class Migrator
{
    /// <summary>
    ///     The table, in the transfers database, recording migrations known to have been applied.
    /// </summary>
    public const string HistoryTable = "__slskd_migrations";

    /// <summary>
    ///     Initializes a new instance of the <see cref="Migrator"/> class.
    /// </summary>
    /// <param name="databases">A list of known databases.</param>
    public Migrator(ConnectionStringDictionary databases)
    {
        Databases = databases;

        Migrations = new()
        {
            { nameof(Z2025_04_01_TransferStateMigration), new Z2025_04_01_TransferStateMigration(connectionStrings: Databases) },
            { nameof(Z2025_07_06_TransferIndexesMigration), new Z2025_07_06_TransferIndexesMigration(connectionStrings: Databases) },
            { nameof(Z2025_07_26_PrivateMessageWasReplayedMigration), new Z2025_07_26_PrivateMessageWasReplayedMigration(connectionStrings: Databases) },
            { nameof(Z2025_12_28_AdditionalTransferIndexesMigration), new Z2025_12_28_AdditionalTransferIndexesMigration(connectionStrings: Databases) },
            { nameof(Z2026_02_17_TransferAttemptsAndBatchIdMigration), new Z2026_02_17_TransferAttemptsAndBatchIdMigration(connectionStrings: Databases) },
            { nameof(Z2026_04_30_DropTransferStartOffsetMigration), new Z2026_04_30_DropTransferStartOffsetMigration(connectionStrings: Databases) },
            { nameof(Z2026_05_06_AddBatchesTableMigration), new Z2026_05_06_AddBatchesTableMigration(connectionStrings: Databases) },
            { nameof(Z2026_11_15_TransferViewAndReportIndexesMigration), new Z2026_11_15_TransferViewAndReportIndexesMigration(connectionStrings: Databases) },
            { nameof(Z2026_11_15_PrivateMessageIndexesMigration), new Z2026_11_15_PrivateMessageIndexesMigration(connectionStrings: Databases) },
            { nameof(Z2026_11_15_SearchSummaryIndexMigration), new Z2026_11_15_SearchSummaryIndexMigration(connectionStrings: Databases) },
            { nameof(Z2026_11_16_PartialReportsIndexMigration), new Z2026_11_16_PartialReportsIndexMigration(connectionStrings: Databases) },
            { nameof(Z2026_11_16_DropRedundantTransferIndexesMigration), new Z2026_11_16_DropRedundantTransferIndexesMigration(connectionStrings: Databases) },
        };
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="Migrator"/> class with the specified <paramref name="migrations"/>.
    /// </summary>
    /// <param name="databases">A list of known databases.</param>
    /// <param name="migrations">The migrations, in the order they are to be applied.</param>
    public Migrator(ConnectionStringDictionary databases, Dictionary<string, IMigration> migrations)
    {
        Databases = databases;
        Migrations = migrations;
    }

    private ConnectionStringDictionary Databases { get; }
    private ILogger Log { get; } = Serilog.Log.ForContext<Migrator>();

    /// <summary>
    ///     A map of all migrations, to be applied in the order they are specified (descending).
    /// </summary>
    /// <remarks>
    ///     This could be done with reflection (e.g. find all instances of IMigration) but it's useful during development
    ///     to selectively add/comment out lines. Slight risk that something is missed, so this should be append only.
    /// </remarks>
    private Dictionary<string, IMigration> Migrations { get; }

    /// <summary>
    ///     Applies database migrations.
    /// </summary>
    /// <param name="force">Apply all migrations, regardless of whether there's evidence they have been applied already.</param>
    public void Migrate(bool force = false)
    {
        List<string> migrationsToApply = [];
        List<string> migrationsToSkip = [];

        /*
            determine which migrations need to be applied

            migrations recorded in the history table are skipped without touching the database they target; the rest
            decide for themselves by inspecting the schema (every migration is idempotent), and the ones found already
            satisfied are recorded so they aren't inspected again. the schema check remains the source of truth whenever
            history is missing, e.g. after the transfers database is deleted, or when forced.
        */
        HashSet<string> history;

        try
        {
            history = force ? [] : ReadHistory();

            foreach (var migration in Migrations.Keys)
            {
                if (history.Contains(migration))
                {
                    continue;
                }

                // warning: performs I/O
                if (!Migrations[migration].NeedsToBeApplied())
                {
                    Log.Debug("Migration {Name} does not need to be applied", migration);
                    migrationsToSkip.Add(migration);
                }
                else
                {
                    Log.Debug("Migration {Name} needs to be applied", migration);
                    migrationsToApply.Add(migration);
                }
            }

            // recorded before the backups below are taken, so a failed migration that restores them keeps these
            Record(migrationsToSkip);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to disposition migrations: {Message}", ex.Message);
            throw;
        }

        Log.Debug("Migrations: {@Migrations}", new { migrationsToApply, migrationsToSkip });

        if (migrationsToApply.Count == 0)
        {
            Log.Information("Databases are up to date!");
            return;
        }

        /*
            MIGRATION TIME!
            ---------------------------------------------------------------------------------------------------------------------------------------------------
        */
        Log.Warning("{Count} outstanding database migration(s) to apply. This operation must be completed before the application can start.", migrationsToApply.Count());

        var migrationId = DateTime.UtcNow.ToString("MMddyy_HHmmss");

        Log.Warning("--> The ID for this migration is {MigrationId}. Use it to locate pre-migration database backups, should manual cleanup be needed. <--", migrationId);

        // take some simple backups before we do anything. this gives users a way to get back to a working configuration
        // if the migration is interrupted, fails, or if the user wants to revert to the previous version. these files
        // must be left in place after the migration completes, and they'll be overwritten the next time a migration is applied
        try
        {
            Log.Information("Backing up existing databases...");

            foreach (var database in Database.List)
            {
                var src = MakeSourceDatabasePath(database.Name);
                var dest = MakeBackupDatabasePath(database.Name, migrationId);

                File.Copy(src, dest, overwrite: true);

                Log.Information("Backed database {Original} up to {Backup}", src, dest);
            }

            Log.Information("Databases backed up successfully");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to back up one or more database(s) prior to migration, operation cannot continue: {Message}", ex.Message);
            Log.Fatal("Try again, revert the application to the previous version, or delete existing databases (warning: history will be lost!) to continue");
            throw;
        }

        // we know which migrations to apply, and we've backed all of the databases up. let's go!
        try
        {
            Log.Warning("Beginning migration(s)");
            Log.Warning("This may take some time. Avoid stopping the application before the process is complete. If the process is interrupted it may be necessary to manually revert to backups.");

            var overallSw = new Stopwatch();
            overallSw.Start();

            var current = 0;
            var total = migrationsToApply.Count();

            var currentSw = new Stopwatch();

            foreach (var migration in migrationsToApply)
            {
                current++;
                currentSw.Restart();

                Log.Warning("Applying migration {Name} ({Current} of {Total})", migration, current, total);

                try
                {
                    Migrations[migration].Apply();
                    Record([migration]);

                    Log.Information("Migration {Name} was applied successfully (elapsed: {Duration}ms)", migration, currentSw.ElapsedMilliseconds);
                }
                catch (Exception ex)
                {
                    Log.Fatal(ex, "Failed to apply migration {Name}: {Message}", migration, ex.Message);
                    throw;
                }
            }

            Log.Information("{Count} migration(s) applied successfully (elapsed: {Duration}ms)", total, overallSw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to apply one or more migration(s): {Message}", ex.Message);

            try
            {
                Log.Information("Attempting to restore databases from backups...");

                foreach (var database in Database.List)
                {
                    var name = Path.Combine(Program.DataDirectory, database.Name);
                    var src = MakeBackupDatabasePath(database.Name, migrationId);
                    var dest = MakeSourceDatabasePath(database.Name);

                    // note: leave the backup in place just in case; automatic cleanup will delete them later
                    File.Copy(src, dest, overwrite: true);

                    Log.Information("Restored {Original} from {Backup}", dest, src);
                }

                Log.Information("Databases restored successfully");
            }
            catch (Exception restoreEx)
            {
                Log.Fatal(restoreEx, "Failed to restore one or more databases from backup: {Message}", restoreEx.Message);
                Log.Fatal($"Restore manually by renaming '<database>.pre-migration.{migrationId}.db' to '<database>.db' prior to starting the application again.");
            }

            throw new SlskdException("Failed to apply one or more database migrations. See inner Exception for details.", ex);
        }

        Log.Information("Migration(s) complete!");
    }

    private string MakeSourceDatabasePath(string database) => Path.Combine(Program.DataDirectory, $"{database}.db");
    private string MakeBackupDatabasePath(string database, string timestamp) => Path.Combine(Program.DataBackupDirectory, $"{database}.pre-migration-backup.{timestamp}.db");

    private HashSet<string> ReadHistory()
    {
        using var connection = new SqliteConnection(Databases[Database.Transfers]);
        connection.Open();

        using var create = new SqliteCommand($"CREATE TABLE IF NOT EXISTS {HistoryTable} (Name TEXT PRIMARY KEY, AppliedAt TEXT NOT NULL)", connection);
        create.ExecuteNonQuery();

        using var select = new SqliteCommand($"SELECT Name FROM {HistoryTable}", connection);
        using var reader = select.ExecuteReader();

        var names = new HashSet<string>(StringComparer.Ordinal);

        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private void Record(IEnumerable<string> migrations)
    {
        var names = migrations.ToList();

        if (names.Count == 0)
        {
            return;
        }

        using var connection = new SqliteConnection(Databases[Database.Transfers]);
        connection.Open();

        using var transaction = connection.BeginTransaction();

        // created here too, for forced runs that skip ReadHistory()
        using (var create = new SqliteCommand($"CREATE TABLE IF NOT EXISTS {HistoryTable} (Name TEXT PRIMARY KEY, AppliedAt TEXT NOT NULL)", connection, transaction))
        {
            create.ExecuteNonQuery();
        }

        using var insert = new SqliteCommand($"INSERT OR REPLACE INTO {HistoryTable} (Name, AppliedAt) VALUES ($name, $at)", connection, transaction);
        var name = insert.Parameters.Add("$name", SqliteType.Text);
        insert.Parameters.AddWithValue("$at", DateTime.UtcNow.ToString("O"));

        foreach (var migration in names)
        {
            name.Value = migration;
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }
}