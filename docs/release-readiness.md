# BinDiff 2.0 release-candidate checklist

Target tag: `v2.0.0-rc.1`

## Public boundary

The source tree contains the managed local comparison engine, CLI, WPF GUI,
documentation, and safe synthetic/project-owned tests. It contains no unknown
sample, updater, telemetry, network client, binary rewriter, disassembler,
decompiler, or project-owned native component.

Self-contained archives include Microsoft .NET runtime dependencies produced by
`dotnet publish`. Inputs remain local and read-only. Managed PE files are parsed
with metadata readers rather than loaded as assemblies.

## Required evidence

- Release build completes with no warnings or errors.
- All tests pass in Release configuration.
- Format verification and `git diff --check` pass.
- Self-contained Windows x64 CLI and GUI publishes succeed.
- CLI comparison writes concrete `strings` and `dotNet` data to valid JSON and
  encoded HTML.
- Published GUI starts and completes a harmless project-assembly comparison.
- The refreshed screenshot contains neutral artifact names and no private path.
- Tracked-file review finds no build output, executable sample, report, dump,
  credential, personal path, or unexpected large file.
- README, security policy, contribution guide, report format, changelog, CI, and
  tag-release workflow match the shipped behavior.

## Verification record

Verified locally on 2026-09-01 against the complete candidate tree:

- Release build: zero warnings and zero errors.
- Tests: 52 passed, zero failed, zero skipped.
- Format verification, YAML parse, and `git diff --check`: succeeded.
- Self-contained Windows x64 CLI and GUI publishes: succeeded.
- Published CLI smoke: seven section kinds; 945 bounded strings in A; managed
  metadata applicable; concrete JSON and 52,701-byte self-contained HTML.
- Both SHA-256 input hashes were identical before and after comparison.
- Published GUI smoke: responsive; 61 automation elements; Analyze, Strings,
  .NET metadata, and Export controls present.
- 1120 × 780 WPF screenshot inspected with neutral `variant-a.dll` and
  `variant-b.dll` project artifacts and no visible private path.

## Maintainer actions

1. Enable GitHub private vulnerability reporting.
2. Confirm source ownership, MIT licensing, and dependency provenance.
3. Merge the reviewed branch to `main` and create the target tag.
4. Verify the generated `SHA256SUMS.txt` before announcing the release.

A release candidate invites validation. Similarity remains evidence for review,
not proof of common origin, intent, or safety.
