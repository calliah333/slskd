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
///     Narrows the paged view and summaries of transfers to a user and/or a search term.
/// </summary>
public record TransferQuery
{
    /// <summary>
    ///     Gets the exact username to match, or null to match any user.
    /// </summary>
    public string Username { get; init; }

    /// <summary>
    ///     Gets text to find anywhere in the filename or username, ignoring ASCII case, or null to match everything.
    /// </summary>
    public string Search { get; init; }

    /// <summary>
    ///     Creates a query from raw request values, treating null or whitespace as absent and trimming the rest.
    /// </summary>
    /// <param name="username">The exact username.</param>
    /// <param name="search">The search text.</param>
    /// <returns>The query.</returns>
    public static TransferQuery From(string username, string search) => new()
    {
        Username = string.IsNullOrWhiteSpace(username) ? null : username.Trim(),
        Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
    };
}

/// <summary>
///     How transfers are grouped in a summary.
/// </summary>
public enum TransferSummaryGrouping
{
    /// <summary>
    ///     One row per remote user.
    /// </summary>
    User,

    /// <summary>
    ///     One row per directory, i.e. the filename up to the last path separator.
    /// </summary>
    Directory,

    /// <summary>
    ///     One row per filename.
    /// </summary>
    File,
}

/// <summary>
///     The order of rows in a summary; every order is descending.
/// </summary>
public enum TransferSummarySort
{
    /// <summary>
    ///     By the number of distinct users.
    /// </summary>
    Users,

    /// <summary>
    ///     By the number of transfers.
    /// </summary>
    Transfers,

    /// <summary>
    ///     By the number of bytes transferred.
    /// </summary>
    Bytes,

    /// <summary>
    ///     By the most recent activity.
    /// </summary>
    Recent,
}

/// <summary>
///     One group of transfers in a summary.
/// </summary>
public record TransferSummaryRow
{
    /// <summary>
    ///     Gets the username, directory, or filename shared by the transfers in the group.
    /// </summary>
    public string Key { get; init; }

    /// <summary>
    ///     Gets the number of transfers in the group.
    /// </summary>
    public int Transfers { get; init; }

    /// <summary>
    ///     Gets the number of distinct users in the group.
    /// </summary>
    public int Users { get; init; }

    /// <summary>
    ///     Gets the number of transfers in the group that succeeded.
    /// </summary>
    public int Succeeded { get; init; }

    /// <summary>
    ///     Gets the total number of bytes transferred in the group.
    /// </summary>
    public long Bytes { get; init; }

    /// <summary>
    ///     Gets the time of the most recent activity in the group; the latest end time, or request time if none ended.
    /// </summary>
    public DateTime LastActivityAt { get; init; }
}

/// <summary>
///     The largest groups of a set of transfers.
/// </summary>
public record TransferSummaryResult
{
    /// <summary>
    ///     Gets the number of groups, including those not returned.
    /// </summary>
    public int Groups { get; init; }

    /// <summary>
    ///     Gets the number of transfers in all groups.
    /// </summary>
    public int Transfers { get; init; }

    /// <summary>
    ///     Gets the returned groups, in the requested order.
    /// </summary>
    public IReadOnlyList<TransferSummaryRow> Rows { get; init; }
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

    /// <summary>
    ///     The default number of groups in a summary.
    /// </summary>
    public const int DefaultSummaryLimit = 100;

    /// <summary>
    ///     The maximum number of groups in a summary.
    /// </summary>
    public const int MaxSummaryLimit = 1000;

    // escapes LIKE wildcards in user-supplied search text; '\' would work too, but every remote path is full of them
    private const string LikeEscape = "!";

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
    ///     Parses the specified <paramref name="value"/> into a member of <typeparamref name="T"/> by name, ignoring case.
    /// </summary>
    /// <remarks>Unlike <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/>, numbers and combined flags are rejected.</remarks>
    /// <typeparam name="T">The enum type.</typeparam>
    /// <param name="value">The value to parse.</param>
    /// <param name="defaultValue">The result when the value is null or whitespace.</param>
    /// <param name="result">The parsed value.</param>
    /// <returns>A value indicating whether the value was parsed.</returns>
    public static bool TryParseName<T>(string value, T defaultValue, out T result)
        where T : struct, Enum
    {
        result = defaultValue;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var name = Enum.GetNames<T>().FirstOrDefault(n => n.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));

        return name is not null && Enum.TryParse(name, out result);
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
    /// <param name="query">An optional query narrowing the transfers counted.</param>
    /// <returns>The number of transfers in each state that has at least one.</returns>
    public static Dictionary<TransferStates, int> CountByState(TransfersDbContext context, TransferDirection direction, TransferQuery query = null)
    {
        return NotRemoved(context, direction, query)
            .GroupBy(t => t.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToDictionary(g => g.State, g => g.Count);
    }

    /// <summary>
    ///     Gets a page of non-removed transfers in the specified <paramref name="direction"/> matching the specified <paramref name="filter"/>
    ///     and <paramref name="query"/>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The sequence being paged is active transfers ordered by RequestedAt ascending, followed by completed transfers
    ///         ordered by EndedAt descending, with ties broken by Id. Each half is a separate indexed query.
    ///     </para>
    ///     <para>
    ///         Counts are narrowed by the query (but not the filter), so that they describe what the filter can select.
    ///     </para>
    /// </remarks>
    /// <param name="context">The database context.</param>
    /// <param name="direction">The transfer direction.</param>
    /// <param name="filter">The filter.</param>
    /// <param name="query">An optional query narrowing the transfers to a user and/or search term.</param>
    /// <param name="offset">The number of matching transfers to skip.</param>
    /// <param name="limit">The maximum number of transfers to return.</param>
    /// <returns>The page.</returns>
    public static TransferPage Page(TransfersDbContext context, TransferDirection direction, TransferFilter filter, TransferQuery query, int offset, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        var counts = CountByState(context, direction, query);

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
            transfers.AddRange(NotRemoved(context, direction, query)
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
            transfers.AddRange(NotRemoved(context, direction, query)
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
    ///     Groups the non-removed transfers in the specified <paramref name="direction"/> matching the specified
    ///     <paramref name="filter"/> and <paramref name="query"/>, and returns the largest groups.
    /// </summary>
    /// <remarks>
    ///     Answers questions like "how many people downloaded this album" (by directory, sorted by users) and "who
    ///     downloads the most from me" (by user, sorted by transfers or bytes). One pass over the matching rows; the
    ///     group count comes from a window over the grouped rows rather than a second query.
    /// </remarks>
    /// <param name="context">The database context.</param>
    /// <param name="direction">The transfer direction.</param>
    /// <param name="filter">The filter.</param>
    /// <param name="query">An optional query narrowing the transfers to a user and/or search term.</param>
    /// <param name="grouping">How to group transfers.</param>
    /// <param name="sort">How to order groups.</param>
    /// <param name="limit">The maximum number of groups to return.</param>
    /// <returns>The summary.</returns>
    public static TransferSummaryResult Summarize(
        TransfersDbContext context,
        TransferDirection direction,
        TransferFilter filter,
        TransferQuery query,
        TransferSummaryGrouping grouping,
        TransferSummarySort sort,
        int limit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        // directory: rtrim() strips every trailing character that isn't a separator, leaving the directory and its
        // trailing separator, which is trimmed after reading. remote paths always use '\'
        var key = grouping switch
        {
            TransferSummaryGrouping.User => "Username",
            TransferSummaryGrouping.Directory => @"rtrim(Filename, replace(Filename, '\', ''))",
            TransferSummaryGrouping.File => "Filename",
            _ => throw new ArgumentOutOfRangeException(nameof(grouping)),
        };

        var order = sort switch
        {
            TransferSummarySort.Users => "Users DESC, Transfers DESC",
            TransferSummarySort.Transfers => "Transfers DESC, Users DESC",
            TransferSummarySort.Bytes => "Bytes DESC",
            TransferSummarySort.Recent => "LastActivityAt DESC",
            _ => throw new ArgumentOutOfRangeException(nameof(sort)),
        };

        var connection = context.Database.GetDbConnection();
        context.Database.OpenConnection();

        try
        {
            using var command = connection.CreateCommand();

            void AddParameter(string name, object value)
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }

            var where = new List<string> { "Direction = $direction", "Removed = 0" };
            AddParameter("$direction", direction.ToString());

            if (filter == TransferFilter.Active)
            {
                where.Add("(State & $completed) = 0");
                AddParameter("$completed", (int)TransferStates.Completed);
            }
            else if (filter != TransferFilter.All)
            {
                var states = CompletedStates(filter).ToList();
                where.Add($"State IN ({string.Join(", ", states.Select((_, i) => $"$state{i}"))})");

                for (int i = 0; i < states.Count; i++)
                {
                    AddParameter($"$state{i}", (int)states[i]);
                }
            }

            if (query?.Username is not null)
            {
                where.Add("Username = $username");
                AddParameter("$username", query.Username);
            }

            if (query?.Search is not null)
            {
                where.Add($"(Filename LIKE $search ESCAPE '{LikeEscape}' OR Username LIKE $search ESCAPE '{LikeEscape}')");
                AddParameter("$search", ContainsPattern(query.Search));
            }

            AddParameter("$succeeded", (int)(TransferStates.Completed | TransferStates.Succeeded));
            AddParameter("$limit", limit);

            command.CommandText = $"""
                SELECT {key} AS Key,
                    COUNT(*) AS Transfers,
                    COUNT(DISTINCT Username) AS Users,
                    SUM(State = $succeeded) AS Succeeded,
                    TOTAL(BytesTransferred) AS Bytes,
                    MAX(COALESCE(EndedAt, RequestedAt)) AS LastActivityAt,
                    COUNT(*) OVER () AS Groups,
                    SUM(COUNT(*)) OVER () AS AllTransfers
                FROM Transfers
                WHERE {string.Join(" AND ", where)}
                GROUP BY Key
                ORDER BY {order}, Key
                LIMIT $limit
                """;

            using var reader = command.ExecuteReader();

            var rows = new List<TransferSummaryRow>();
            int groups = 0, transfers = 0;

            while (reader.Read())
            {
                var rowKey = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);

                rows.Add(new TransferSummaryRow
                {
                    Key = grouping == TransferSummaryGrouping.Directory ? rowKey.TrimEnd('\\') : rowKey,
                    Transfers = reader.GetInt32(1),
                    Users = reader.GetInt32(2),
                    Succeeded = reader.GetInt32(3),
                    Bytes = (long)reader.GetDouble(4),
                    LastActivityAt = DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc),
                });

                groups = reader.GetInt32(6);
                transfers = reader.GetInt32(7);
            }

            return new TransferSummaryResult
            {
                Groups = groups,
                Transfers = transfers,
                Rows = rows,
            };
        }
        finally
        {
            context.Database.CloseConnection();
        }
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

    private static string ContainsPattern(string text)
    {
        var escaped = text
            .Replace(LikeEscape, LikeEscape + LikeEscape)
            .Replace("%", LikeEscape + "%")
            .Replace("_", LikeEscape + "_");

        return $"%{escaped}%";
    }

    private static IQueryable<Transfer> NotRemoved(TransfersDbContext context, TransferDirection direction, TransferQuery query = null)
    {
        // see remarks on the class; this must be an equality comparison for the indexes to be used
        var removed = false;

        var transfers = context.Transfers
            .AsNoTracking()
            .Where(t => t.Direction == direction)
            .Where(t => t.Removed == removed);

        if (query?.Username is { } username)
        {
            transfers = transfers.Where(t => t.Username == username);
        }

        if (query?.Search is { } search)
        {
            // SQLite's LIKE ignores ASCII case; there's no index for a substring match, so this reads every row in
            // the direction. acceptable for an interactive filter, and still far cheaper than shipping rows to the client
            var pattern = ContainsPattern(search);
            transfers = transfers.Where(t => EF.Functions.Like(t.Filename, pattern, LikeEscape) || EF.Functions.Like(t.Username, pattern, LikeEscape));
        }

        return transfers;
    }
}
