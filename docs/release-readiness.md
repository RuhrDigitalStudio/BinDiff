# BinDiff Release Readiness

## Baseline recorded 2026-08-12

Commands were run from the repository root on the public-release branch.

```text
dotnet build BinDiff.slnx
```

Result: succeeded with 0 errors and 2 warnings. Both warnings are xUnit2000 in
`BinDiff.Tests/ByteDiffAnalyzerTests.cs` lines 50 and 51: `Assert.Equal`
arguments are ordered as actual, expected rather than expected, actual.

```text
dotnet test BinDiff.Tests/BinDiff.Tests.csproj
```

Result: passed: 40; failed: 0; skipped: 0. The same two xUnit2000 warnings were
emitted during the test build.

## Repository scan

Tracked source, XAML, and documentation contain German user-facing copy and
comments in the CLI, Core reporting and model files, and WPF GUI. The existing
README is German and its UTF-8 text is displayed incorrectly in the current
PowerShell output encoding.

No tracked personal filesystem paths, credential-like strings, executable
samples, or generated binary artifacts were found by the baseline scan. The
existing `.gitignore` excludes `bin/`, `obj/`, `.vs/`, `.idea/`, and generated
report files.

## Release gate recorded 2026-08-12

```text
dotnet build BinDiff.slnx --configuration Release
```

Result: succeeded with 0 warnings and 0 errors.

```text
dotnet test BinDiff.Tests/BinDiff.Tests.csproj --configuration Release
```

Result: passed: 40; failed: 0; skipped: 0.

```text
dotnet format BinDiff.slnx --verify-no-changes
```

Result: succeeded with no required formatting changes.

The final tracked-file scan found no generated build output, IDE state, private
paths, credential-like strings, or executable samples. `git status --short` and
the staged-file list were empty before this report update.

## Provenance and documentation

`git log --all --name-status` shows the tracked source was introduced by the
same project author, and a repository-wide source scan found no third-party or
license notices. The repository owner selected the MIT License for the original
project source and documentation. This technical evidence is not a substitute
for the owner's final confirmation that all included work may be relicensed.

The GUI screenshot at `docs/images/gui-overview.png` was captured from the
locally built WPF application and inspected. It shows the completed comparison
of synthetic `sample-a.bin` and `sample-b.bin` inputs only; no private path or
real filename is present.

## Remaining limitations

## Release blockers

- Explicit source-ownership and provenance confirmation is required before the
  public repository is enabled.
- Before publishing to GitHub, the owner must enable **Report a vulnerability**
  in the repository's Security tab or publish an equivalent monitored private
  reporting route.

The tool also loads complete inputs into memory, and its format and similarity
results are best-effort heuristics; these limitations are documented in the
README.

## Review remediation recorded 2026-08-12

The README's synthetic PowerShell example was run from the repository root. It
created two 16-byte temporary inputs, exited with code 0, wrote nonempty JSON
(7,109 bytes) and HTML (6,255 bytes) reports, and left both input SHA-256 hashes
unchanged. The temporary directory was removed after verification.

The GUI screenshot was recaptured from the built WPF application after selecting
the same synthetic files and running a comparison. It visibly shows only
`sample-a.bin`, `sample-b.bin`, their 16-byte sizes, and derived scores.

Following the remediation, the Release build, Release test suite, and format
verification were run again: build succeeded with 0 warnings and 0 errors;
tests passed 41/41; and `dotnet format BinDiff.slnx --verify-no-changes`
succeeded.

## GUI theme remediation recorded 2026-08-12

The result-state screenshot exposed native light tab and progress-control
templates and black overview labels on the dark theme. The root cause was that
the XAML only set basic control properties, leaving the native `TabItem` and
`ProgressBar` templates active; the overview title binding also relied on an
implicit text foreground. Explicit dark `TabItem` and `ProgressBar` templates,
data-grid row foregrounds, and an explicit overview-label foreground now ensure
the intended dark surfaces and high-contrast text.

`GuiThemeResourceTests` is a focused regression check that fails unless the
theme defines explicit tab/progress templates and the overview title binds to
the foreground resource. It was observed failing before the XAML change and
passes afterward. The refreshed application screenshot was visually inspected:
tab headers, metric labels, tracks, and fills now render with the dark palette.
