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
// </copyright>

using Microsoft.Extensions.Options;

namespace slskd.Search
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.SignalR;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Caching.Memory;
    using Serilog;
    using slskd.Search.API;
    using Soulseek;
    using SearchOptions = Soulseek.SearchOptions;
    using SearchQuery = Soulseek.SearchQuery;
    using SearchScope = Soulseek.SearchScope;
    using SearchStates = Soulseek.SearchStates;

    /// <summary>
    ///     Handles the lifecycle and persistence of searches.
    /// </summary>
    public interface ISearchService
    {
        /// <summary>
        ///     Deletes the specified search.
        /// </summary>
        /// <param name="search">The search to delete.</param>
        /// <returns>The operation context.</returns>
        Task DeleteAsync(Search search);

        /// <summary>
        ///     Finds a single search matching the specified <paramref name="expression"/>.
        /// </summary>
        /// <param name="expression">The expression to use to match searches.</param>
        /// <param name="includeResponses">A value indicating whether to include search responses in the result.</param>
        /// <returns>The found search, or default if not found.</returns>
        /// <exception cref="ArgumentException">Thrown when an expression is not supplied.</exception>
        Task<Search> FindAsync(Expression<Func<Search, bool>> expression, bool includeResponses = false);

        /// <summary>
        ///     Returns a list of all completed and in-progress searches, with responses omitted, matching the optional <paramref name="expression"/>.
        /// </summary>
        /// <param name="expression">An optional expression used to match searches.</param>
        /// <returns>The list of searches matching the specified expression, or all searches if no expression is specified.</returns>
        Task<List<Search>> ListAsync(Expression<Func<Search, bool>> expression = null);

        /// <summary>
        ///     Updates the specified <paramref name="search"/>.
        /// </summary>
        /// <remark>
        ///     Round-trips the database; use accordingly.
        /// </remark>
        /// <param name="search">The search to update.</param>
        void Update(Search search);

        /// <summary>
        ///     Performs a search for the specified <paramref name="query"/> and <paramref name="scope"/>.
        /// </summary>
        /// <param name="id">A unique identifier for the search.</param>
        /// <param name="query">The search query.</param>
        /// <param name="scope">The search scope.</param>
        /// <param name="options">Search options.</param>
        /// <returns>The completed search.</returns>
        Task<Search> StartAsync(Guid id, SearchQuery query, SearchScope scope, SearchOptions options = null);

        /// <summary>
        ///     Cancels the search matching the specified <paramref name="id"/>, if it is in progress.
        /// </summary>
        /// <param name="id">The unique identifier for the search.</param>
        /// <returns>A value indicating whether the search was successfully cancelled.</returns>
        bool TryCancel(Guid id);

        /// <summary>
        ///     Removes <see cref="SearchStates.Completed"/> searches older than the specified <paramref name="age"/>.
        /// </summary>
        /// <param name="age">The age after which records are eligible for pruning, in minutes.</param>
        /// <returns>The number of pruned records.</returns>
        Task<int> PruneAsync(int age);

        /// <summary>
        ///     Returns the responses of the search matching the specified <paramref name="id"/>.
        /// </summary>
        /// <remarks>
        ///     The returned list may be shared and must not be modified.
        /// </remarks>
        /// <param name="id">The unique identifier of the search.</param>
        /// <returns>The responses, or null if no such search exists.</returns>
        Task<IReadOnlyList<Response>> GetResponsesAsync(Guid id);
    }

    /// <summary>
    ///     Handles the lifecycle and persistence of searches.
    /// </summary>
    public class SearchService : ISearchService
    {
        /// <summary>
        ///     The minimum interval between broadcasts of the progress of an active search.
        /// </summary>
        private const int ProgressBroadcastIntervalMilliseconds = 500;

        /// <summary>
        ///     The minimum interval between writes of the progress of an active search to the database. The final state is
        ///     always persisted, and reads of active searches see live progress (see <see cref="ActiveSearches"/>).
        /// </summary>
        private const long ProgressPersistIntervalMilliseconds = 2000;

        /// <summary>
        ///     The capacity of the response cache, in responses plus files (roughly 300 bytes per unit).
        /// </summary>
        private const long ResponseCacheSizeLimit = 500_000;

        /// <summary>
        ///     How long deserialized responses are kept after their last use.
        /// </summary>
        private static readonly TimeSpan ResponseCacheSlidingExpiration = TimeSpan.FromMinutes(5);

        /// <summary>
        ///     Initializes a new instance of the <see cref="SearchService"/> class.
        /// </summary>
        /// <param name="searchHub"></param>
        /// <param name="optionsMonitor"></param>
        /// <param name="soulseekClient"></param>
        /// <param name="contextFactory">The database context to use.</param>
        public SearchService(
            IHubContext<SearchHub> searchHub,
            IOptionsMonitor<Options> optionsMonitor,
            ISoulseekClient soulseekClient,
            IDbContextFactory<SearchDbContext> contextFactory)
        {
            SearchHub = searchHub;
            OptionsMonitor = optionsMonitor;
            Client = soulseekClient;
            ContextFactory = contextFactory;
        }

        private ConcurrentDictionary<Guid, CancellationTokenSource> CancellationTokens { get; }
            = new ConcurrentDictionary<Guid, CancellationTokenSource>();

        /// <summary>
        ///     Gets the in-progress searches, which hold progress (counts) that may not have been persisted yet.
        /// </summary>
        private ConcurrentDictionary<Guid, Search> ActiveSearches { get; } = new ConcurrentDictionary<Guid, Search>();

        /// <summary>
        ///     Gets the cache of deserialized responses of completed searches, keyed by search id, so that paging through a
        ///     search doesn't re-read and re-parse its responses for every page.
        /// </summary>
        private MemoryCache ResponseCache { get; } = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = ResponseCacheSizeLimit,
            CompactionPercentage = 0.25,
        });

        /// <summary>
        ///     Gets the lock which ensures concurrent requests for an uncached search parse it only once.
        /// </summary>
        private SemaphoreSlim ResponseCacheLoadLock { get; } = new SemaphoreSlim(1, 1);

        private ISoulseekClient Client { get; }
        private IDbContextFactory<SearchDbContext> ContextFactory { get; }
        private ILogger Log { get; set; } = Serilog.Log.ForContext<Application>();
        private IOptionsMonitor<Options> OptionsMonitor { get; }
        private IHubContext<SearchHub> SearchHub { get; set; }

        /// <summary>
        ///     Deletes the specified search.
        /// </summary>
        /// <param name="search">The search to delete.</param>
        /// <returns>The operation context.</returns>
        public Task DeleteAsync(Search search)
        {
            if (search == default)
            {
                throw new ArgumentNullException(nameof(search));
            }

            return DoDeleteAsync(search);

            async Task DoDeleteAsync(Search search)
            {
                using var context = ContextFactory.CreateDbContext();
                await context.Searches.Where(s => s.Id == search.Id).ExecuteDeleteAsync();

                ResponseCache.Remove(search.Id);

                await SearchHub.BroadcastDeleteAsync(search);
            }
        }

        /// <summary>
        ///     Finds a single search matching the specified <paramref name="expression"/>.
        /// </summary>
        /// <param name="expression">The expression to use to match searches.</param>
        /// <param name="includeResponses">A value indicating whether to include search responses in the result.</param>
        /// <returns>The found search, or default if not found.</returns>
        /// <exception cref="ArgumentException">Thrown when an expression is not supplied.</exception>
        public async Task<Search> FindAsync(Expression<Func<Search, bool>> expression, bool includeResponses = false)
        {
            if (expression == default)
            {
                throw new ArgumentException("An expression must be supplied.", nameof(expression));
            }

            using var context = ContextFactory.CreateDbContext();

            var selector = context.Searches
                .AsNoTracking()
                .Where(expression);

            if (!includeResponses)
            {
                selector = selector.WithoutResponses();
            }

            var search = await selector.FirstOrDefaultAsync();
            return WithLiveProgress(search);
        }

        /// <summary>
        ///     Returns the responses of the search matching the specified <paramref name="id"/>.
        /// </summary>
        /// <remarks>
        ///     The responses of completed searches are cached for a while after use; the returned list is shared and must not
        ///     be modified. In-progress searches have no responses yet.
        /// </remarks>
        /// <param name="id">The unique identifier of the search.</param>
        /// <returns>The responses, or null if no such search exists.</returns>
        public async Task<IReadOnlyList<Response>> GetResponsesAsync(Guid id)
        {
            if (ResponseCache.TryGetValue(id, out IReadOnlyList<Response> cached))
            {
                return cached;
            }

            await ResponseCacheLoadLock.WaitAsync();

            try
            {
                // another caller may have loaded it while we were waiting
                if (ResponseCache.TryGetValue(id, out cached))
                {
                    return cached;
                }

                using var context = ContextFactory.CreateDbContext();

                var record = await context.Searches
                    .AsNoTracking()
                    .Where(s => s.Id == id)
                    .Select(s => new { s.State, s.ResponsesJson })
                    .FirstOrDefaultAsync();

                if (record == null)
                {
                    return null;
                }

                IReadOnlyList<Response> responses = string.IsNullOrEmpty(record.ResponsesJson)
                    ? []
                    : JsonSerializer.Deserialize<List<Response>>(record.ResponsesJson) ?? [];

                // responses are persisted together with the Completed state; anything earlier is a placeholder
                if (record.State.HasFlag(SearchStates.Completed))
                {
                    CacheResponses(id, responses, CacheItemPriority.Normal);
                }

                return responses;
            }
            finally
            {
                ResponseCacheLoadLock.Release();
            }
        }

        /// <summary>
        ///     Returns a list of all completed and in-progress searches, with responses omitted, matching the optional <paramref name="expression"/>.
        /// </summary>
        /// <param name="expression">An optional expression used to match searches.</param>
        /// <returns>The list of searches matching the specified expression, or all searches if no expression is specified.</returns>
        public async Task<List<Search>> ListAsync(Expression<Func<Search, bool>> expression = null)
        {
            expression ??= s => true;
            using var context = ContextFactory.CreateDbContext();

            var searches = await context.Searches
                .AsNoTracking()
                .Where(expression)
                .WithoutResponses()
                .ToListAsync();

            if (!ActiveSearches.IsEmpty)
            {
                foreach (var search in searches)
                {
                    WithLiveProgress(search);
                }
            }

            return searches;
        }

        /// <summary>
        ///     Updates the specified <paramref name="search"/>.
        /// </summary>
        /// <param name="search">The search to update.</param>
        public void Update(Search search)
        {
            using var context = ContextFactory.CreateDbContext();
            context.Update(search);
            context.SaveChanges();
        }

        /// <summary>
        ///     Performs a search for the specified <paramref name="query"/> and <paramref name="scope"/>.
        /// </summary>
        /// <param name="id">A unique identifier for the search.</param>
        /// <param name="query">The search query.</param>
        /// <param name="scope">The search scope.</param>
        /// <param name="options">Search options.</param>
        /// <returns>The completed search.</returns>
        public async Task<Search> StartAsync(Guid id, SearchQuery query, SearchScope scope, SearchOptions options = null)
        {
            var token = Client.GetNextToken();

            var cancellationTokenSource = new CancellationTokenSource();
            CancellationTokens.TryAdd(id, cancellationTokenSource);

            var rateLimiter = new RateLimiter(ProgressBroadcastIntervalMilliseconds);
            var progressPersistedAt = Environment.TickCount64;

            // initialize the search record, save it to the database, and broadcast the creation
            // we do this so the UI has some feedback to show to the user that we've gotten their request
            var search = new Search()
            {
                SearchText = query.SearchText,
                Token = token,
                Id = id,
                State = SearchStates.Requested,
                StartedAt = DateTime.UtcNow,
            };

            bool searchCreated = false;
            bool searchBroadcasted = false;

            try
            {
                using var context = ContextFactory.CreateDbContext();
                context.Add(search);
                context.SaveChanges();

                searchCreated = true;
                ActiveSearches[id] = search;

                await SearchHub.BroadcastCreateAsync(search);

                searchBroadcasted = true;

                // initialize the list of responses that we'll use to accumulate them
                // populated by the responseHandler we pass to SearchAsync
                // responses arrive concurrently from many connections, so this must be thread safe
                ConcurrentQueue<SearchResponse> responses = new();

                options ??= new SearchOptions();
                options = options.WithActions(
                    stateChanged: (args) =>
                    {
                        // this logic is executed by Soulseek.NET upon each transition of the searcn, including the final
                        // transition to Completed. if for some reason something goes wrong, we won't see the final Completed
                        // transition, we should instead see the task below throw, in which case it will become
                        // faulted and we'll set the Completed flag manually in the catch block
                        if (args.Search.State.HasFlag(SearchStates.Completed))
                        {
                            // the completed state is persisted and broadcast when the search is finalized below, together
                            // with the responses, so that nobody sees a completed search before its responses are available
                            Log.Debug("Search for '{Query}' state changed: {State} (id: {Id})", query, args.Search.State, id);
                            return;
                        }

                        search = search.WithSoulseekSearch(args.Search);
                        ActiveSearches[id] = search;
                        Update(search);

                        SearchHub.BroadcastUpdateAsync(search);

                        Log.Debug("Search for '{Query}' state changed: {State} (id: {Id})", query, search.State, id);
                    },
                    responseReceived: (args) => rateLimiter.Invoke(() =>
                    {
                        // note that we're not actually doing anything with the response here, that's happening in the
                        // response handler. we're only updating counts here.
                        search.ResponseCount = args.Search.ResponseCount;
                        search.FileCount = args.Search.FileCount;
                        search.LockedFileCount = args.Search.LockedFileCount;

                        // progress is broadcast at most every ProgressBroadcastIntervalMilliseconds, but written to the
                        // database less often; reads in the meantime pick up live counts from ActiveSearches
                        var now = Environment.TickCount64;

                        if (now - progressPersistedAt >= ProgressPersistIntervalMilliseconds)
                        {
                            progressPersistedAt = now;
                            UpdateProgress(search);
                        }

                        SearchHub.BroadcastUpdateAsync(search);
                    }));

                // initiate the search. this can throw at invocation if there's a problem with
                // the client state (e.g. disconnected) or a problem with the search (e.g. no terms)
                var soulseekSearchTask = Client.SearchAsync(
                    query,
                    responseHandler: (response) => responses.Enqueue(response),
                    scope,
                    token,
                    options,
                    cancellationToken: cancellationTokenSource.Token);

                // search looks ok so far; let the rest of the logic run asynchronously
                // on a background thread. this logic needs to clean up after itself and
                // update the search record to accurately reflect the final state
                _ = Task.Run(async () =>
                {
                    try
                    {
                        try
                        {
                            var soulseekSearch = await soulseekSearchTask;
                            search = search.WithSoulseekSearch(soulseekSearch);

                            Log.Debug("Search for '{Query}' ended normally (id: {Id})", query, id);
                        }
                        catch (Exception ex)
                        {
                            Log.Debug(ex, "Search for '{Query}' threw {Exception}: {Message} (id: {Id})", query, ex.GetType(), ex.Message, id);

                            // OperationCanceledException might be thrown somewhere deeper, and we don't want that to count.
                            // a search that was actually cancelled by the user will meet the criteria below.
                            if (ex is OperationCanceledException && cancellationTokenSource.Token.IsCancellationRequested)
                            {
                                Log.Information("Search for '{Query}' was cancelled", query);
                                search.State = SearchStates.Completed | SearchStates.Cancelled;
                            }
                            else
                            {
                                Log.Error(ex, "Failed to execute search for '{Query}': {Message}", query, ex.Message);
                                search.State = SearchStates.Completed | SearchStates.Errored;
                            }
                        }

                        // stop progress updates so a late one can't follow the final update
                        rateLimiter.Dispose();

                        // the StateChanged callback defers the Completed state to here, and if the search failed we won't
                        // have seen it at all; make sure the record is persisted with the Completed flag set to avoid it
                        // getting "stuck"
                        if (!search.State.HasFlag(SearchStates.Completed))
                        {
                            search.State |= SearchStates.Completed;
                        }

                        var finalResponses = responses.Select(r => Response.FromSoulseekSearchResponse(r)).ToList();

                        search.EndedAt = DateTime.UtcNow;
                        search.Responses = finalResponses;

                        Update(search);

                        // whoever opens the search next (very likely the user who started it) won't need to re-read it
                        ResponseCache.Remove(id);
                        CacheResponses(id, finalResponses, CacheItemPriority.Low);

                        ActiveSearches.TryRemove(id, out _);

                        // zero responses before broadcasting, as we don't want to blast this
                        // data out over the SignalR socket
                        await SearchHub.BroadcastUpdateAsync(search with { Responses = [] });

                        Log.Debug("Search for '{Query}' finalized (id: {Id}): {Search}", query, id, search with { Responses = [] });
                        Log.Information("Search for '{Query}' completed with {Responses} responses", query, search.ResponseCount);
                    }
                    catch (Exception ex)
                    {
                        // record may be left 'hanging' and will need to be cleaned up at the next boot; we tried to update but failed
                        Log.Error(ex, "Failed to finalize search for '{Query}': {Message}", query, ex.Message);
                    }
                    finally
                    {
                        rateLimiter.Dispose();
                        ActiveSearches.TryRemove(id, out _);
                        CancellationTokens.TryRemove(id, out _);
                    }
                });

                // broadcast and return the _newly created_ search; it will continue to be updated in the background
                await SearchHub.BroadcastUpdateAsync(search);
                return search;
            }
            catch (Exception ex)
            {
                // we'll end up here if the initial call throws for an ArgumentException, InvalidOperationException if
                // the app isn't connected, and a few other straightforward issues that arise before even requesting the search
                Log.Error(ex, "Failed to execute search {Search}: {Message}", new { query, scope, options }, ex.Message);

                ActiveSearches.TryRemove(id, out _);

                // selectively 'undo' whatever actions we were able to take successfully
                if (searchCreated)
                {
                    search.State = SearchStates.Completed | SearchStates.Errored;
                    search.EndedAt = search.StartedAt;
                    Update(search);

                    if (searchBroadcasted)
                    {
                        await SearchHub.BroadcastUpdateAsync(search with { Responses = [] });
                    }
                }

                throw;
            }
        }

        /// <summary>
        ///     Cancels the search matching the specified <paramref name="id"/>, if it is in progress.
        /// </summary>
        /// <param name="id">The unique identifier for the search.</param>
        /// <returns>A value indicating whether the search was successfully cancelled.</returns>
        public bool TryCancel(Guid id)
        {
            if (CancellationTokens.TryGetValue(id, out var cts))
            {
                cts.Cancel(swallowObjectDisposedException: true);
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Removes <see cref="SearchStates.Completed"/> searches older than the specified <paramref name="age"/>.
        /// </summary>
        /// <param name="age">The age after which searches are eligible for pruning, in minutes.</param>
        /// <returns>The number of pruned records.</returns>
        public async Task<int> PruneAsync(int age)
        {
            try
            {
                using var context = ContextFactory.CreateDbContext();

                var cutoffDateTime = DateTime.UtcNow.AddMinutes(-age);

                // unlike other pruning operations, we don't care about state, since there's a 60 minute minimum
                // and searches are guaranteed to be at least 60 minutes old by the time they can be pruned, they will
                // be completed unless someone applied some rather dumb settings
                Expression<Func<Search, bool>> expired = s => s.EndedAt.HasValue && s.EndedAt.Value < cutoffDateTime;

                // fetch the ids first so the UI can be told which searches went away
                var ids = await context.Searches
                    .Where(expired)
                    .Select(s => s.Id)
                    .ToListAsync();

                if (ids.Count == 0)
                {
                    return 0;
                }

                var pruned = await context.Searches
                    .Where(expired)
                    .ExecuteDeleteAsync();

                foreach (var id in ids)
                {
                    ResponseCache.Remove(id);
                }

                await SearchHub.BroadcastDeleteManyAsync(ids);

                return pruned;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to prune searches: {Message}", ex.Message);
                throw;
            }
        }

        private static long GetCacheSize(IReadOnlyList<Response> responses)
        {
            long size = responses.Count;

            foreach (var response in responses)
            {
                size += response.Files.Count + response.LockedFiles.Count;
            }

            return Math.Max(size, 1);
        }

        private void CacheResponses(Guid id, IReadOnlyList<Response> responses, CacheItemPriority priority)
        {
            // entries larger than the whole cache are silently rejected; such searches are parsed on every request
            ResponseCache.Set(id, responses, new MemoryCacheEntryOptions
            {
                Size = GetCacheSize(responses),
                SlidingExpiration = ResponseCacheSlidingExpiration,
                Priority = priority,
            });
        }

        private void UpdateProgress(Search search)
        {
            var (id, responseCount, fileCount, lockedFileCount) = (search.Id, search.ResponseCount, search.FileCount, search.LockedFileCount);

            try
            {
                // write only the counts; the full row (including the responses) is written when the search is finalized
                using var context = ContextFactory.CreateDbContext();
                context.Searches
                    .Where(s => s.Id == id)
                    .ExecuteUpdate(setters => setters
                        .SetProperty(s => s.ResponseCount, responseCount)
                        .SetProperty(s => s.FileCount, fileCount)
                        .SetProperty(s => s.LockedFileCount, lockedFileCount));
            }
            catch (Exception ex)
            {
                // the final update will catch up
                Log.Warning(ex, "Failed to persist progress of search {Id}: {Message}", id, ex.Message);
            }
        }

        private Search WithLiveProgress(Search search)
        {
            if (search is not null && !search.IsComplete && ActiveSearches.TryGetValue(search.Id, out var live))
            {
                search.ResponseCount = live.ResponseCount;
                search.FileCount = live.FileCount;
                search.LockedFileCount = live.LockedFileCount;
            }

            return search;
        }
    }
}