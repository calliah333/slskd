# Transfer filters and breakdown

Answers "who is downloading this album from me?" and "what did this person take?" without loading every transfer.

## Web UI (Uploads / Downloads)

- **Search box** in the header: matches anywhere in the remote path or username, ignoring ASCII case. `%` and `_` are
  literal. Debounced 350ms.
- **User chip**: click a username on a transfer card (filter icon on hover) to show only that user's transfers. Exact,
  case-sensitive match (Soulseek usernames are case-sensitive).
- **Breakdown** button: a table of the largest groups of the transfers currently selected (state tab + search + user),
  grouped by directory, file, or user, sorted by distinct users, transfers, bytes, or recency. Clicking a row drills the
  list down to it (a directory becomes a search for `dir\`, a user becomes the user chip). Refreshes every 15s.
- State lives in the URL (`?q=…&user=…&view=breakdown`), so views can be bookmarked and the back button undoes a
  drill-down.
- Bulk actions (Retry/Cancel/Remove All) are hidden while narrowed: they act on every transfer, and the narrowed counts
  would misrepresent what they touch.

## API

`GET /api/v0/transfers/{uploads|downloads}/page` gains two optional parameters:

| Parameter | Meaning |
| --- | --- |
| `username` | exact username |
| `search` | substring of filename or username, ASCII case-insensitive |

`counts` in the response are narrowed by these too (not by `filter`), so the state tabs show counts within the user or
search.

`GET /api/v0/transfers/{uploads|downloads}/summary`

| Parameter | Values | Default |
| --- | --- | --- |
| `by` | `user`, `directory`, `file` | `directory` |
| `sort` | `users`, `transfers`, `bytes`, `recent` (all descending) | `users` |
| `filter` | as for `/page` | `all` |
| `username`, `search` | as for `/page` | |
| `limit` | 1–1000 | 100 |

Response: `{ by, sort, filter, username, search, limit, groups, transfers, rows: [{ key, users, transfers, succeeded,
bytes, lastActivityAt }] }`. `groups`/`transfers` count everything matched, not only the returned rows.

## Implementation notes

- `TransferPaging.Summarize` is one raw SQL statement: `GROUP BY` the key, `COUNT(DISTINCT Username)`, and window
  functions (`COUNT(*) OVER ()`) for the totals, so there's no second query.
- Directory key: `rtrim(Filename, replace(Filename, '\', ''))` strips the trailing non-separator characters, i.e. the
  file name.
- Substring search can't use an index; it reads every row of the direction. ~150ms for 150k rows. Exact username uses
  `IDX_Transfers_UserUploadStatistics` once planner statistics exist (see findings: ANALYZE).
- Measured on 200k synthetic transfers (150k uploads): directory summary ~500ms, user summary ~170ms, user page ~30ms,
  search page ~170ms.
