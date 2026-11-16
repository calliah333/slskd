# Performance work

Notes from profiling and optimizing slskd at scale (hundreds of thousands of transfers, tens of thousands of shared
files, busy search traffic). Each change lands as its own commit on `perf/webui-transfers-at-scale`.

| File | Contents |
| --- | --- |
| [findings.md](findings.md) | Every finding: what's slow, evidence, status (done / open / rejected and why) |
| [transfer-breakdown.md](transfer-breakdown.md) | Filtering transfers by user/text and the Breakdown view (API + UI) |
| [testing.md](testing.md) | How the changes were measured: synthetic datasets, timings, query plans |

## Where the time went

Most problems fell into four shapes:

1. **Unbounded responses** — endpoints returning every row (all transfers, all search responses) and the browser
   filtering/sorting them. Fixed by paging and aggregating on the server.
2. **Work per tick** — things done every 250ms–1s per item (progress writes, full-state broadcasts, polling) that only
   need to happen as often as a human looks.
3. **Planner blind spots** — SQLite was never `ANALYZE`d, so it picked indexes by heuristics.
4. **Repeated work on the hot path** — lazy sequences enumerated several times on the incoming search path.
