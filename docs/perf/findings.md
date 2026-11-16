# Findings

Status: **done** (committed on this branch), **open** (worth doing, not done), **rejected** (looked, decided not to).

## Database (SQLite)

| Finding | Evidence | Status |
| --- | --- | --- |
| No database was ever `ANALYZE`d, so the planner picked indexes by heuristics | No `sqlite_stat1` in any DB. One user's uploads were found by scanning `IDX_Transfers_DirectionRemovedState` (every upload) instead of seeking `IDX_Transfers_UserUploadStatistics`: 290ms → 29ms on 200k rows | **done**: `DatabaseOptimizer` runs `PRAGMA optimize=0x10002` in the background after migrations and hourly. Plain `PRAGMA optimize` at startup did nothing: it only considers tables the connection already queried |
| Every transfer progress tick rewrote an `IDX_Transfers_Reports` entry | The 8-column index covers `AverageSpeed`, which each tick updates | **done**: partial index `WHERE EndedAt IS NOT NULL`; every report query already has that predicate |
| Progress persisted 4×/s per active transfer | 250ms `ExecuteUpdate` in both transfer services; the UI reads once a second | **done**: 1/s |
| GROUP BY / ORDER BY temp b-trees could spill to disk | default `temp_store` | **done**: `temp_store=MEMORY` per connection |
| Listing searches read every search's response JSON | Most list columns sit after the multi-MB `ResponsesJson` in the row | **done**: covering `IDX_Searches_Summary` (15ms → 0.05ms for 200 × 400KB searches) |
| Search lookups by id read the whole multi-MB row; huge searches re-parsed per page | All responses were one JSON column on the search row | **done**: `SearchResponses` table (one row per response, sortable/hideable columns, files as JSON). First page of a 10k-response search 590ms → 88ms; by-id lookup no longer touches responses |
| Unread message counts were O(conversations × unread) in memory | `ConversationService.ListAsync` | **done**: one `GROUP BY` on `(IsAcknowledged, Username)` |
| Events table grows forever; any page size accepted | `EventBus` persists every event | **done**: limit capped at 1000; opt-in `retention.events` |
| Shares scan: one connection + transaction per file | `InsertDirectory`/`InsertFile` per row | **done**: one writer, ~1000 rows per transaction, prepared commands |
| Shares DB uses `Cache=shared` | `ShareRepositoryFactory` | **open**: shared cache locks at table level, so searches can wait behind a scan batch commit |
| Redundant single-column transfer indexes (`Direction`, `Removed`) cost writes | `IDX_Transfers_DirectionRemovedState` leads with both; `Removed` alone is ~95% one value | **done**: dropped. The two old migrations that created them now count the composite as satisfying them. `State` stays: `IDX_Transfers_Reports` is partial, so active-state queries without a direction need it |
| Every startup re-inspected every database's schema for every migration | `Migrator` | **done**: `__slskd_migrations` history table in `transfers.db`; recorded migrations are skipped, schema checks remain the fallback (deleted DB, `--force-migrations`). Verified on existing data (drop applied once, then "up to date") and on a fresh install (no migrations) |

## Hot paths

| Finding | Evidence | Status |
| --- | --- | --- |
| Incoming search results enumerated up to 3× | lazy `SelectMany` + `Any()` + `Count()` in `SearchResponseResolver` (~30 requests/s) | **done** |
| Response file limit applied per share host | `ShareService.SearchAsync` | **done**: one global limit |
| Exclusions consumed the SQL `LIMIT` | post-filter in C# after `LIMIT` | **done**: pages past dropped rows |
| Blacklist miss: every configured CIDR parsed, managed list scanned linearly | `UserService.IsBlacklisted`, `Blacklist.Contains` | **done**: CIDRs parsed on config change; managed ranges merged and binary searched |
| Configured picture read from disk on every user-info request | `UserInfoResolver` | **done**: cached, re-read when mtime changes |
| Full application state deep-cloned via JSON on every mutation | `ManagedState.SetValue` | **done**: records + `with` are already immutable |
| Full state broadcast to every client on every mutation | `Application.State_OnChange` | **done**: coalesced to ≤ 4/s, last state always sent |
| Upload queue rebuilt per-group lists and sorted on every `Process` | `UploadQueue.Process` | **done**: one pass |
| Active searches persisted + broadcast every 250ms | `SearchService` | **done**: broadcast 500ms, persist 2s; completion serializes responses once |
| Webhooks/scripts start an unbounded task (or process) per event | `WebhookService`, `ScriptService` | **open**: bounded worker queue |
| User caches never expire within a session | `UserService` | **open**: measure first |

## Correctness bugs found along the way

| Bug | Status |
| --- | --- |
| Rejoining a room after a reconnect kept the stale user list; a message arriving before the join finished made the join's users/owner/operators get dropped (`TryAdd`) | **done**: join response wins, messages kept |
| A share scan hung forever if every worker failed while the directory channel was full | **done**: a failing worker closes the channel; the scan ends faulted |
| `POST /rooms/joined` returns 200 without joining if a placeholder room exists (e.g. a message delivered just after leaving) | **open**: rare; needs a "joined" flag on tracked rooms |
| Sending a private message while slskd is disconnected from the server fails and the message isn't kept | **open**: an outbox that sends on reconnect |

## Web UI and HTTP

| Finding | Evidence | Status |
| --- | --- | --- |
| Lazy route chunks failed to load on nested routes | `/system/logs` requested `/system/static/js/699.*.chunk.js`; reproduced on the live instance | **done** (bug): runtime `__webpack_public_path__` from `urlBase` |
| No response compression | 500-row transfers page polled every second: 295KB | **done**: brotli/gzip at fastest level; 70KB. main.js 418KB → 166KB |
| Hashed assets revalidated on every load | no `Cache-Control` on `/static` | **done**: `public, max-age=1y, immutable` |
| First render waited for both SignalR hubs to negotiate | `App.init()` | **done**: render after the session check; hubs connect in the background. Relay agent mode is injected into the page (`window.relayAgent`) so routes don't need the first hub message to know it |
| Idle Transfers page still ran 2 queries and shipped ~295KB every second | `GET /transfers/{dir}/page` polled at 1s | **done**: `RevisionInterceptor` tracks writes per database; page and summary return 304 with no query when unchanged. Verified: an idle page polls 304s; a removal invalidates |
| Dashboard waited for all nine reports | `Promise.all` | **done**: each section fills as its report arrives |
| Search detail downloaded every response and filtered/sorted in the browser | 10k responses = 26MB JSON (4.4MB brotli) | **done**: server-side filter/sort/page with a parsed-response cache; first page 590ms, then ~25ms |
| Rooms polled joined rooms every 500ms, users + messages every 1s | `Rooms.jsx` | **done**: ETag/304, users every 10s, rooms every 5s |
| Live logs re-rendered 500 rows per incoming line | `Live.jsx` | **done**: 250ms batches, memoized rows |
| Files explorer mounted every entry and a modal per row | `System/Files/Explorer.jsx` | **done**: 500-row pages, one shared modal |
| Share contents built one giant string for CodeMirror | `ContentsModal.jsx` | **done**: virtualized list over 20k lines |
| Events page blanked to a loader on every page change | `System/Events` | **done** |
| Semantic UI CSS (566KB) includes every component | `semantic.less` | **open**: now compressed and cached immutably, so the remaining cost is first-visit parse time |
| No way to see who transfers what | | **done**: see [transfer-breakdown.md](transfer-breakdown.md) |

## Known trade-offs introduced

- Transfer progress in the UI can lag up to ~2s (1s persist + 1s poll).
- Room user lists refresh every 10s.
- Share contents over 20k lines render as a plain virtualized list, so browser find only searches visible lines.
- A database error during a share scan now faults the scan instead of skipping the rest of one directory.
