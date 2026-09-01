# Changelog

## 2.0.0-rc.1 — 2026-09-01

### Added

- Bounded ASCII and UTF-16LE extraction with encoding, first offsets,
  occurrence counts, set similarity, and shared/file-specific result lists.
- Metadata-only managed .NET comparison for assembly identity, target framework,
  references, declared types, methods, and P/Invoke targets.
- Dedicated Strings and .NET metadata views in the WPF application.
- Complete polymorphic JSON and encoded HTML details for both new modules.
- CLI module aliases plus `--string-min` and `--max-strings` controls.
- Report-format documentation and automated Windows build/release workflows.

### Compatibility

The five original analyzers, existing CLI options, and report section kinds are
preserved. Strings and .NET metadata are enabled by default. An inapplicable
.NET comparison does not affect the aggregate similarity score.

### Safety

Managed metadata is read through `PEReader`/`MetadataReader`; inspected
assemblies are not loaded or executed. Extracted strings can contain sensitive
input data and are therefore called out explicitly in the README and reports.
