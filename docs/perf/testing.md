# Measuring

Numbers in these notes come from a local instance with synthetic data, not production.

## Running locally

```sh
dotnet build slskd.sln
cd src/web && npm run build && cd -
ln -sfn "$PWD/src/web/build" src/slskd/bin/Debug/net10.0/wwwroot
DOTNET_ROLL_FORWARD=Major dotnet run --no-build --project src/slskd --no-launch-profile -- \
  --app-dir /tmp/slskd-dev --no-connect --no-auth --http-port 5030 --no-https
```

`DOTNET_ROLL_FORWARD=Major` is only needed when the machine has a newer runtime than the project targets.

## Datasets

- **Transfers**: 200k rows inserted straight into `transfers.db` (150k uploads, 50k downloads), 3,000 users, 2,000
  albums with Pareto-distributed popularity, realistic state mix (80% succeeded, 7% errored, …), 5% removed.
- **Search**: one completed search with 10,000 responses / 105k files (26MB of JSON), inserted as the old
  `Searches.ResponsesJson` column and then moved into `SearchResponses` by the migration. Note: EF stores `Guid` ids as
  upper-case text; rows inserted by hand must match or lookups 404.
- **Files**: 3,000 empty files in the downloads directory.

## Checking query plans

```sh
sqlite3 /tmp/slskd-dev/data/transfers.db "EXPLAIN QUERY PLAN SELECT ..."
sqlite3 /tmp/slskd-dev/data/transfers.db "SELECT * FROM sqlite_stat1"   # empty = never analyzed
```

## Results

| Request | Before | After |
| --- | --- | --- |
| Uploads page for one user (`?username=`) | 290ms | 29ms (planner statistics) |
| Uploads page, 500 rows, transfer size | 295KB | 70KB (brotli) |
| `main.js` transfer size | 418KB | 166KB (brotli), then cached for a year |
| Search detail, 10k responses | 26MB JSON (4.4MB brotli), filtered in the browser | 590ms first page (parse + cache), ~25ms after, ~11KB per page |
| Breakdown by directory, 150k uploads | n/a | ~500ms |
| Files explorer, 3,002 entries | 3,002 rows + a modal per row mounted | 502 rows |
| Search migration (`ResponsesJson` → `SearchResponses`), 26MB | n/a | 645ms |
| Search by id with its 26MB of responses stored | whole row read | 282B, responses untouched |
