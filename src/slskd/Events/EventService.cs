// <copyright file="EventService.cs" company="JP Dillingham">
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

namespace slskd.Events;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;

/// <summary>
///     Manages events.
/// </summary>
public class EventService
{
    /// <summary>
    ///     The maximum number of records deleted per statement when pruning, so that the database write lock is held
    ///     only briefly even when a large backlog of expired events is pruned for the first time.
    /// </summary>
    public const int PruneBatchSize = 5000;

    private int pruning;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EventService"/> class.
    /// </summary>
    /// <param name="contextFactory">The database context factory.</param>
    /// <param name="optionsMonitor">The options monitor used to resolve the configured retention period.</param>
    public EventService(IDbContextFactory<EventsDbContext> contextFactory, IOptionsMonitor<slskd.Options> optionsMonitor)
    {
        ContextFactory = contextFactory;
        OptionsMonitor = optionsMonitor;

        Clock.EveryFiveMinutes += (_, _) => Task.Run(() => PruneExpired());
    }

    private IDbContextFactory<EventsDbContext> ContextFactory { get; }
    private IOptionsMonitor<slskd.Options> OptionsMonitor { get; }
    private ILogger Log { get; } = Serilog.Log.ForContext<EventService>();

    /// <summary>
    ///     Gets list of events, optionally applying the specified <paramref name="offset"/> and <paramref name="limit"/>.
    /// </summary>
    /// <param name="offset">The beginning offset for the page.</param>
    /// <param name="limit">The page size limit.</param>
    /// <returns>The retrieved list.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the specified <paramref name="offset"/> is less than zero.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the specified <paramref name="limit"/> is zero.</exception>
    public virtual IReadOnlyList<EventRecord> Get(int offset = 0, int limit = int.MaxValue)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be greater than or equal to zero");
        }

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero");
        }

        try
        {
            using var context = ContextFactory.CreateDbContext();

            var events = context.Events
                .OrderByDescending(e => e.Timestamp)
                .Skip(offset)
                .Take(limit)
                .ToList()
                .AsReadOnly();

            return events;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get event records with options {Options}: {Message}", new { offset, limit }, ex.Message);
            throw;
        }
    }

    /// <summary>
    ///     Gets the total number of events.
    /// </summary>
    /// <returns>The total number of events.</returns>
    public virtual int Count()
    {
        try
        {
            using var context = ContextFactory.CreateDbContext();
            var count = context.Events.Count();
            return count;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to count event records: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    ///     Adds the specified event <paramref name="eventRecord"/>.
    /// </summary>
    /// <remarks>
    ///     To ensure proper construction of the record, use <see cref="EventRecord.From"/>.
    /// </remarks>
    /// <param name="eventRecord">The record to add.</param>
    /// <exception cref="ArgumentNullException">Thrown when the specified record is null.</exception>
    public virtual void Add(EventRecord eventRecord)
    {
        if (eventRecord is null)
        {
            throw new ArgumentNullException(nameof(eventRecord));
        }

        try
        {
            using var context = ContextFactory.CreateDbContext();
            context.Add(eventRecord);
            context.SaveChanges();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to add event record: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    ///     Deletes events older than the specified <paramref name="age"/>.
    /// </summary>
    /// <param name="age">The age after which events are eligible for pruning, in minutes.</param>
    /// <returns>The number of pruned events.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the specified <paramref name="age"/> is less than zero.</exception>
    public virtual int Prune(int age)
    {
        if (age < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(age), "Age must be greater than or equal to zero");
        }

        var cutoff = DateTime.UtcNow.AddMinutes(-age);
        var pruned = 0;
        int deleted;

        try
        {
            // delete in batches (oldest first; the range scan is backed by the Timestamp index) so that inserts from
            // the event bus aren't blocked behind one long-running delete
            do
            {
                using var context = ContextFactory.CreateDbContext();

                deleted = context.Events
                    .Where(e => e.Timestamp < cutoff)
                    .OrderBy(e => e.Timestamp)
                    .Take(PruneBatchSize)
                    .ExecuteDelete();

                pruned += deleted;
            }
            while (deleted == PruneBatchSize);

            if (pruned > 0)
            {
                Log.Information("Pruned {Count} events older than {Age} minutes", pruned, age);
            }

            return pruned;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to prune events older than {Age} minutes: {Message}", age, ex.Message);
            throw;
        }
    }

    private void PruneExpired()
    {
        var age = OptionsMonitor.CurrentValue.Retention.Events;

        // a large first-time prune can outlast the clock interval; never run two at once
        if (!age.HasValue || Interlocked.CompareExchange(ref pruning, 1, 0) != 0)
        {
            return;
        }

        try
        {
            Prune(age.Value);
        }
        catch
        {
            // logged by Prune; retried on the next tick
        }
        finally
        {
            Interlocked.Exchange(ref pruning, 0);
        }
    }
}