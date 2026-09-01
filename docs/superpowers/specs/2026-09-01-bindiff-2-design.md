# BinDiff 2 design

## Product goal

BinDiff should answer more than “how many bytes match?” It should help an
analyst explain how two local artifacts differ, especially when they are
managed .NET variants or contain changed configuration, commands, paths, and
other embedded text.

## New comparison dimensions

### Extracted strings

Scan bounded ASCII and UTF-16LE runs without interpreting or executing them.
The result records the number of distinct strings, overlap, and a capped list
of representative strings shared by both inputs or unique to either input.
Every displayed value includes encoding, first offset, and occurrence count.

Strings shorter than a configurable minimum are ignored. Per-string length,
total distinct strings, and report lists are bounded. Control characters and
unpaired UTF-16 data end a run.

### Managed .NET metadata

Use `PEReader` and `MetadataReader` only. Never call `Assembly.Load`, resolve a
dependency, invoke an entry point, or run a module initializer.

For each managed input, capture assembly name/version, target framework,
assembly references, declared type names, method signatures at a stable summary
level, and P/Invoke targets. Compare common and file-specific values and derive
a structural similarity score. If neither input contains managed metadata, the
module is informational and does not affect the overall score.

## Surfaces

- Core: two analyzers and serializable result sections.
- CLI: both modules enabled by default, selectable through `--modules`; string
  minimum length is configurable.
- HTML/JSON: complete machine-readable values, with HTML encoding and capped
  human-facing tables.
- GUI: module toggles and dedicated .NET and Strings tabs.

## Compatibility

Existing module names, options, report fields, and the five original analyzers
remain intact. The default aggregate gains two equally weighted structural
signals when applicable. Unknown/native files still compare normally; an
inapplicable .NET module contributes no score.

## Safety and limits

All input remains local and read-only. An analyzer failure becomes a section
error and does not abort other modules. The new analyzers use bounded loops and
collections; report strings may contain sensitive data from the input, which
the UI and documentation must state plainly.

## Non-goals

No disassembler, decompiler, symbol server, package download, unpacker, binary
rewriter, patch generator, attribution claim, or sample execution.
