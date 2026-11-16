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

namespace slskd;

using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

/// <summary>
///     Tracks a revision for the database behind <typeparamref name="TContext"/> that changes whenever its data might have.
/// </summary>
/// <remarks>
///     <para>
///         Lets polled read endpoints answer "nothing changed" (an ETag match, 304) without running their queries.
///     </para>
///     <para>
///         The revision moves after every command that isn't a SELECT or PRAGMA, and again when a transaction commits:
///         inside a transaction a write isn't visible to other connections until the commit, and a reader that saw the
///         revision move before then would otherwise pair the new revision with old data and keep it. Moving too often
///         only costs a refetch; never moving after a visible write is what must not happen.
///     </para>
///     <para>
///         Only writes made through EF contexts with this interceptor are seen. Raw connections that write (migrations,
///         maintenance) happen at startup or don't change rows.
///     </para>
/// </remarks>
/// <typeparam name="TContext">The context whose database is tracked.</typeparam>
public sealed class RevisionInterceptor<TContext> : DbCommandInterceptor, IDbTransactionInterceptor
    where TContext : DbContext
{
    private static long current = Revision.Next();

    /// <summary>
    ///     Gets the current revision.
    /// </summary>
    public static long Current => Interlocked.Read(ref current);

    /// <inheritdoc/>
    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Observe(command);
        return result;
    }

    /// <inheritdoc/>
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Observe(command);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc/>
    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        // EF's SQLite provider issues INSERT/UPDATE ... RETURNING as readers
        Observe(command);
        return result;
    }

    /// <inheritdoc/>
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        Observe(command);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc/>
    public override object ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object result)
    {
        Observe(command);
        return result;
    }

    /// <inheritdoc/>
    public override ValueTask<object> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object result, CancellationToken cancellationToken = default)
    {
        Observe(command);
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc/>
    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => Bump();

    /// <inheritdoc/>
    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Bump();
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Returns a value indicating whether the specified SQL could change data.
    /// </summary>
    /// <param name="commandText">The SQL.</param>
    /// <returns>A value indicating whether the SQL could change data.</returns>
    public static bool IsWrite(string commandText)
    {
        var text = commandText.AsSpan().TrimStart();

        return !text.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase);
    }

    private static void Observe(DbCommand command)
    {
        if (IsWrite(command.CommandText))
        {
            Bump();
        }
    }

    // a fresh value from the shared sequence, so a revision is never reused across databases or restarts
    private static void Bump() => Interlocked.Exchange(ref current, Revision.Next());
}
