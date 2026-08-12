# Security policy

## Supported versions

Security fixes are made against the current default branch. This repository has
not published versioned releases yet.

## Reporting a vulnerability

For an official GitHub-hosted release, submit a private report through the
repository's **Security** tab:

1. Open the repository where you obtained BinDiff on GitHub.
2. Select **Security**, then **Report a vulnerability**.
3. Create a private vulnerability report with:

- a concise description and impact assessment;
- affected commit or version;
- reproduction steps using harmless synthetic inputs where possible; and
- suggested mitigations or a proof of concept only when it is necessary to
  establish impact.

The GitHub **Report a vulnerability** action must be enabled by the repository
owner before publication. If it is unavailable, do not disclose the issue in a
public issue or attach sensitive samples. The repository is not ready for a
public release until the owner enables private vulnerability reporting or
publishes an equivalent monitored private contact route.

Do not attach sensitive, proprietary, or malicious samples. The maintainers
will acknowledge the report, assess scope, and coordinate a fix before public
disclosure.

## Scope notes

BinDiff is intended to read local files only. Reports that identify unsafe
parsing, unbounded resource use, unexpected file modification, or an unintended
network interaction are particularly useful.
