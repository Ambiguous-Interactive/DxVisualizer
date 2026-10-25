# Refresh type relevance on 25 October 2026

The browser now checks the changed asset's canonical namespace group instead of scanning every managed type. It reads the live catalog directly; no cache or invalidation owner is
added. Exact type equality and the settings full-refresh path remain unchanged. Refs #308.

## Paired component observations

- [Baseline samples](refresh-relevance-baseline-2026-10-25.csv) and [final samples](refresh-relevance-final-2026-10-25.csv).
- [Unchanged evaluation driver](refresh-relevance-driver-2026-10-25.txt), executed through Unity MCP eval with a 60-second timeout.
- Unity 6000.5.2f1, Windows 11 10.0.26200, one warm editor process. Synthetic CLR types; no asset creation, classification, import or row refresh is measured.
- Each of 100/1,000/2,000 types has ten types per namespace. Every size/condition has five warm-ups and 30 samples, each timing 200 direct calls through a bound production
  delegate.
- Every call asserts its result. The driver checks the production compiler/source checksum before and after each run. CSV headers retain source SHA-256 and loaded module
  identities. Baseline source is 53c9b471b5859e1f2218d56ba16b044bb6e5019cd8756bc6f13ebf96aff65374; final source is b7708eeb9eceea58139efcf9160353ff3643e6cfb7e7eaeabad20c0e4e98290a.
- Samples preserve emitted numeric strings. Transport line endings are normalized from CRLF to repository LF.

Nearest-rank p95 in microseconds per call:

| Types | Tracked last type, baseline/final | Absent type, baseline/final | Settings, baseline/final |
| ----- | --------------------------------- | --------------------------- | ------------------------ |
| 100   | 13.576 / 10.104                   | 10.835 / 9.768              | 0.130 / 0.233            |
| 1,000 | 90.285 / 9.614                    | 88.829 / 7.949              | 0.130 / 0.139            |
| 2,000 | 202.606 / 8.992                   | 299.886 / 11.464            | 0.333 / 0.095            |

These sequential observations isolate type relevance. They do not measure callback or whole-window latency, allocations, native memory, real 100k assets, domain reload,
hardware/platform compatibility or usability. A namespace with many types still has a linear exact-identity scan inside that group. Small timings, especially settings, remain
sensitive to timer/process noise.

## Behavior coverage

The focused regression uses the production window and a 2,000-type synthetic catalog. It checks tracked and absent types, equal short names in different namespaces, immediate
removal/re-addition and the settings special case. Existing refresh tests retain move/delete/label, selection, loading-generation and Play-suspension coverage. This change does not
complete all acceptance criteria of #308.
