# Contributing to BinDiff

Thanks for helping improve a defensive, local-only analysis tool.

## Before opening a change

- Keep the analyzer offline and read-only: do not modify input binaries or add
  uploads, telemetry, or network behavior.
- Keep public source, comments, tests, UI copy, and documentation in English.
- Use synthetic, authorized, and non-sensitive data for tests and screenshots.
- Do not commit build output, IDE state, reports containing sensitive metadata,
  executables, or sample malware.

## Development workflow

1. Create a focused branch.
2. For behavior changes, add a focused xUnit test first and observe it fail.
3. Implement the smallest change that makes the test pass.
4. Run the complete local quality gate from the repository root:

   ```powershell
   dotnet restore BinDiff.slnx
   dotnet build BinDiff.slnx -c Release --no-restore
   dotnet test BinDiff.Tests/BinDiff.Tests.csproj -c Release --no-build
   dotnet format BinDiff.slnx --verify-no-changes --no-restore
   git diff --check
   ```

5. Explain the safety and compatibility impact in the pull request.

Analyzer tests should cover identical inputs, clearly different inputs, empty
data, a close benign case, malformed structure, and every new resource bound.
Report changes need a JSON concrete-field check and an HTML-encoding check for
untrusted values. Managed tests must use synthetic or project-owned assemblies
and must not introduce executable samples into source control.

## Reporting bugs and proposing features

Use clear reproduction steps with synthetic bytes or temporary local files.
For a security issue, follow [SECURITY.md](SECURITY.md) rather than opening a
public issue.
