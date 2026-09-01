# BinDiff report format

JSON reports contain `fileA`, `fileB`, `overallSimilarityPercent`, `sections`,
`generatedAt`, and `warnings`. Each section has a `kind` discriminator, common
fields (`module`, `title`, `similarityPercent`, `error`, `metrics`), and its
module-specific data.

Current section kinds:

| `kind` | Detail |
| --- | --- |
| `byteDiff` | Content-defined chunks, byte histogram, and file maps. |
| `fuzzyHash` | MinHash and context-triggered piecewise hashes. |
| `format` | PE/ELF summaries, sections, and imports. |
| `entropy` | Aggregate values and down-sampled profiles. |
| `patterns` | Shared and file-specific repeated byte sequences. |
| `strings` | Bounded ASCII/UTF-16LE sets, encoding, offsets, and counts. |
| `dotNet` | Managed profiles and common/file-specific metadata. |
| `error` | A module failure captured without aborting the comparison. |

## Stability

The `kind` strings and existing property names are the integration contract for
the 2.x line. Consumers should ignore unknown properties and section kinds,
handle a null similarity score, and read `error` before module-specific fields.

`generatedAt` varies per run. Hashes and analyzer content are deterministic for
the same two inputs, options, architecture, and BinDiff revision. Input paths
are included in JSON; avoid publishing a report until those paths and extracted
strings have been reviewed.

## String details

String identity is the pair of `encoding` and `value`; the same text in ASCII
and UTF-16LE is intentionally treated as two observations. Missing offsets are
`-1`. Lists are capped by `maxReportedStrings`, while the distinct counts refer
to the bounded extraction set.

## Managed metadata details

`applicable` is false when neither input exposes managed assembly metadata. A
single managed input is applicable and scores zero against a native/unknown
counterpart. `truncated` on a profile means at least one metadata collection hit
the configured bound. P/Invoke values contain the module, imported member, and
declaring managed method; they are evidence, not proof the call is reachable.

The HTML report is a human-facing rendering of the same result. It is
self-contained, uses no scripts or network resources, HTML-encodes extracted
values, and caps expanded tables for readability.
