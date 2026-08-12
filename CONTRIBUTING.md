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
   dotnet build BinDiff.slnx
   dotnet test BinDiff.Tests/BinDiff.Tests.csproj
   dotnet format BinDiff.slnx --verify-no-changes
   ```

5. Explain the safety and compatibility impact in the pull request.

## Reporting bugs and proposing features

Use clear reproduction steps with synthetic bytes or temporary local files.
For a security issue, follow [SECURITY.md](SECURITY.md) rather than opening a
public issue.
