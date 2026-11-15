// <copyright file="TransferPaging.cs" company="JP Dillingham">
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

namespace slskd.Transfers;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Soulseek;

/// <summary>
///     Filters for the paged view of transfers.
/// </summary>
public enum TransferFilter
{
    /// <summary>
    ///     Every transfer.
    /// </summary>
    All,

    /// <summary>
    ///     Transfers whose state does not include the <see cref="TransferStates.Completed"/> flag.
    /// </summary>
    Active,

    /// <summary>
    ///     Transfers whose state includes the <see cref="TransferStates.Completed"/> flag.
    /// </summary>
    Completed,

    /// <summary>
    ///     Transfers that completed successfully.
    /// </summary>
    Succeeded,

    /// <summary>
    ///     Transfers that timed out, errored, were rejected, or were aborted.
    /// </summary>
    Errored,

    /// <summary>
    ///     Transfers that were cancelled.
    /// </summary>
    Cancelled,
}

/// <summary>
///     Filters for bulk cancellation of active transfers.
/// </summary>
public enum TransferCancelFilter
{
    /// <summary>
    ///     Every active transfer.
    /// </summary>
    All,

    /// <summary>
    ///     Requested and queued (locally or remotely) transfers.
    /// </summary>
    Queued,

    /// <summary>
    ///     Initializing and in progress transfers.
    /// </summary>
    InProgress,
}

/// <summary>
///     A page of transfers.
/// </summary>
public record TransferPage
{
    /// <summary>
    ///     Gets the number of transfers matching the filter.
    /// </summary>
    public int Total { get; init; }

    /// <summary>
    ///     Gets the number of transfers in each state, regardless of the filter.
    /// </summary>
    public IReadOnlyDictionary<TransferStates, int> Counts { get; init; }

    /// <summary>
    ///     Gets the transfers on the page; active transfers first, oldest request first, then completed transfers, most recently ended first.
    /// </summary>
    public IReadOnlyList<Transfer> Transfers { get; init; }
}

/// <summary>
///     Paged, filtered, and aggregate queries over the Transfers table, shared by uploads and downloads.
/// </summary>
/// <remarks>
///     <para>
///         Everything here is designed to be answered by the IDX_Transfers_DirectionRemovedState and
///         IDX_Transfers_DirectionRemovedEndedAt indexes without reading or sorting the whole table; databases with
///         hundreds of thousands of transfers are common. Check with EXPLAIN QUERY PLAN when changing a query.
///     </para>
///     <para>
///         Removed is compared to a parameter rather than negated; EF translates !t.Removed to NOT ("t"."Removed"),
///         which SQLite can't match to an index column, and the planner then falls back to a full sort.
///     </para>
/// </remarks>
public static class TransferPaging
{
    /// <summary>
    ///     The default page size.
    /// </summary>
    public const int DefaultLimit = 500;

    /// <summary>
    ///     The maximum page size.
    /// </summary>
    public const int MaxLimit = 5000;

    private static readonly HashSet<TransferStates> ErroredStates = [
        TransferStates.Completed | TransferStates.TimedOut,
        TransferStates.Completed | TransferStates.Errored,
        TransferStates.Completed | TransferStates.Rejected,
        TransferStates.Completed | TransferStates.Aborted,
    ];

    private static readonly HashSet<TransferStates> CancelledStates = [
        TransferStates.Completed | TransferStates.Cancelled,
    ];

    private static readonly Dictionary<string, TransferFilter> Filters = Enum.GetValues<TransferFilter>()
        .ToDictionary(f => f.ToString(), StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, TransferCancelFilter> CancelFilters = Enum.GetValues<TransferCancelFilter>()
        .ToDictionary(f => f.ToString(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Parses the specified <paramref name="value"/> into a <see cref="TransferFilter"/>, ignoring case.
    /// </summary>
    /// <remarks>Null or whitespace parses to <see cref="TransferFilter.All"/>.</remarks>
    /// <param name="value">The value to parse.</param>
    /// <param name="filter">The parsed filter.</param>
    /// <returns>A value indicating whether the value was parsed.</returns>
    public static bool TryParseFilter(string value, out TransferFilter filter)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            filter = TransferFilter.All;
            return true;
        }

        return Filters.TryGetValue(value.Trim(), out filter);
    }

    /// <summary>
    ///     Parses the specified <paramref name="value"/> into a <see cref="TransferCancelFilter"/>, ignoring case.
    /// </summary>
    /// <remarks>Null or whitespace parses to <see cref="TransferCancelFilter.All"/>.</remarks>
    /// <param name="value">The value to parse.</param>
    /// <param name="filter">The parsed filter.</param>
    /// <returns>A value indicating whether the value was parsed.</returns>
    public static bool TryParseCancelFilter(string value, out TransferCancelFilter filter)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            filter = TransferCancelFilter.All;
            return true;
        }

        return CancelFilters.TryGetValue(value.Trim(), out filter);
    }

    /// <summary>
    ///     Gets the set of completed states matched by the specified <paramref name="filter"/>.
    /// </summary>
    /// <remarks>
    ///     <see cref="TransferFilter.Active"/> matches no completed state. <see cref="TransferFilter.All"/> and
    ///     <see cref="TransferFilter.Completed"/> match <see cref="TransferStateCategories.Completed"/>, which is
    ///     the set used to remove completed transfers.
    /// </remarks>
    /// <param name="filter">The filter.</param>
    /// <returns>The set of matching completed states.</returns>
    public static IReadOnlySet<TransferStates> CompletedStates(TransferFilter filter) => filter switch
    {
        TransferFilter.All or TransferFilter.Completed => TransferStateCategories.Completed,
        TransferFilter.Succeeded => TransferStateCategories.Successful,
        TransferFilter.Errored => ErroredStates,
        TransferFilter.Cancelled => CancelledStates,
        _ => new HashSet<TransferStates>(),
    };

    /// <summary>
    ///     Counts the non-removed transfers in the specified <paramref name="direction"/>, grouped by state.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="direction">The transfer direction.</param>
    /// <returns>The number of transfers in each state that has at least one.</returns>
    public static Dictionary<TransferStates, int> CountByState(TransfersDbContext context, TransferDirection direction)
    {
        return NotRemoved(context, direction)
            .GroupBy(t => t.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToDictionary(g => g.State, g => g.Count);
    }

    /// <summary>
    ///     Gets a page of non-removed transfers in the specified <paramref name="direction"/> matching the specified <paramref name="filter"/>.
    /// </summary>
    /// <remarks>
    ///     The sequence being paged is active transfers ordered by RequestedAt ascending, followed by completed transfers
    ///     ordered by EndedAt descending, with ties broken by Id. Each half is a separate indexed query.
    /// </remarks>
    /// <param name="context">The database context.</param>
    /// <param name="direction">The transfer direction.</param>
    /// <param name="filter">The filter.</param>
    /// <param name="offset">The number of matching transfers to skip.</param>
    /// <param name="limit">The maximum number of transfers to return.</param>
    /// <returns>The page.</returns>
    public static TransferPage Page(TransfersDbContext context, TransferDirection direction, TransferFilter filter, int offset, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var counts = CountByState(context, direction);

        // the IN lists are built from the states actually present so that 'all' covers every row, even ones in
        // states that aren't in any of the predefined categories, and so that absent states don't bloat the query
        var activeStates = filter is TransferFilter.All or TransferFilter.Active
            ? counts.Keys.Where(s => !s.HasFlag(TransferStates.Completed)).ToList()
            : [];

        var completedStates = filter is TransferFilter.All or TransferFilter.Completed
            ? counts.Keys.Where(s => s.HasFlag(TransferStates.Completed)).ToList()
            : counts.Keys.Where(CompletedStates(filter).Contains).ToList();

        var activeTotal = activeStates.Sum(s => counts[s]);
        var completedTotal = completedStates.Sum(s => counts[s]);

        var transfers = new List<Transfer>(Math.Min(limit, Math.Max(0, activeTotal + completedTotal - offset)));

        if (activeStates.Count > 0 && offset < activeTotal)
        {
            transfers.AddRange(NotRemoved(context, direction)
                .Where(t => activeStates.Contains(t.State))
                .OrderBy(t => t.RequestedAt)
                .ThenBy(t => t.Id)
                .Skip(offset)
                .Take(limit)
                .ToList());
        }

        var remaining = limit - transfers.Count;
        var completedOffset = Math.Max(0, offset - activeTotal);

        if (completedStates.Count > 0 && remaining > 0 && completedOffset < completedTotal)
        {
            transfers.AddRange(NotRemoved(context, direction)
                .Where(t => completedStates.Contains(t.State))
                .OrderByDescending(t => t.EndedAt)
                .ThenByDescending(t => t.Id)
                .Skip(completedOffset)
                .Take(remaining)
                .ToList());
        }

        return new TransferPage
        {
            Total = activeTotal + completedTotal,
            Counts = counts,
            Transfers = transfers,
        };
    }

    /// <summary>
    ///     Lists the non-removed, active transfers in the specified <paramref name="direction"/> matching the specified <paramref name="filter"/>.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="direction">The transfer direction.</param>
    /// <param name="filter">The filter.</param>
    /// <returns>The matching transfers, ordered by RequestedAt ascending.</returns>
    public static List<Transfer> ListActive(TransfersDbContext context, TransferDirection direction, TransferCancelFilter filter)
    {
        var states = filter switch
        {
            TransferCancelFilter.Queued => TransferStateCategories.Queued.ToList(),
            TransferCancelFilter.InProgress => TransferStateCategories.InProgress.ToList(),
            _ => CountByState(context, direction).Keys.Where(s => !s.HasFlag(TransferStates.Completed)).ToList(),
        };

        if (states.Count == 0)
        {
            return [];
        }

        return NotRemoved(context, direction)
            .Where(t => states.Contains(t.State))
            .OrderBy(t => t.RequestedAt)
            .ThenBy(t => t.Id)
            .ToList();
    }

    private static IQueryable<Transfer> NotRemoved(TransfersDbContext context, TransferDirection direction)
    {
        // see remarks on the class; this must be an equality comparison for the indexes to be used
        var removed = false;

        return context.Transfers
            .AsNoTracking()
            .Where(t => t.Direction == direction)
            .Where(t => t.Removed == removed);
    }
}
