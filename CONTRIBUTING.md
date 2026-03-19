# Contributing

## Prerequisites

- .NET 10 SDK
- C# 14

No other toolchains are required. The markdown linter binary is downloaded automatically on first build.

## Build

```shell
dotnet build pamba.slnx
```

This builds all library projects, test projects, and the `Pamba.Root` project which downloads and runs the markdown
linter.

## Test

```shell
dotnet test pamba.slnx
```

## Formatting

Format all C# and Markdown files in one command:

```shell
dotnet build Pamba.Root.csproj -t:Format
```

This runs `dotnet format` (C# via `.editorconfig`) followed by `rumdl check --fix` (Markdown via `.rumdl.toml`).

Verify no formatting changes remain:

```shell
dotnet format pamba.slnx --verify-no-changes
dotnet build Pamba.Root.csproj
```

Markdown is linted by [rumdl](https://github.com/rvben/rumdl). The pinned binary is downloaded automatically to
`.tools/rumdl/` on first build. Update `RumdlVersion` in `build/markdown.targets` to upgrade - the next build downloads
the new binary.

## Project Structure

| Directory              | Project               | Purpose                                      |
| ---------------------- | --------------------- | -------------------------------------------- |
| `pamba-core/`          | `Pamba`               | Core runtime (net10.0, zero UI dependencies) |
| `pamba-winui/`         | `Pamba.WinUI`         | WinUI 3 shell (net10.0-windows)              |
| `pamba-testing/`       | `Pamba.Testing`       | Test utilities (net10.0)                     |
| `pamba-core-tests/`    | `Pamba.Tests`         | Core runtime tests                           |
| `pamba-winui-tests/`   | `Pamba.WinUI.Tests`   | WinUI shell tests                            |
| `pamba-testing-tests/` | `Pamba.Testing.Tests` | Test utility tests                           |
| `build/`               | -                     | Shared MSBuild props and targets             |

## Build Infrastructure

| File                       | Purpose                                                          |
| -------------------------- | ---------------------------------------------------------------- |
| `Directory.Build.props`    | Quality settings enforced across all projects                    |
| `Directory.Packages.props` | Centralised NuGet package version management                     |
| `build/package.props`      | Shared NuGet package metadata (imported by library csproj files) |
| `build/test.props`         | Shared test project settings (imported by test csproj files)     |
| `build/markdown.targets`   | rumdl download and lint targets                                  |
| `Pamba.Root.csproj`        | Root project - builds all projects and runs markdown linting     |
| `pamba-ci.slnf`            | CI solution filter (excludes `Pamba.Root.csproj`)                |
| `.editorconfig`            | C# style, copyright header, and analyser rules                   |
| `.rumdl.toml`              | Markdown lint configuration                                      |
| `release.ps1`              | GPG-signed release tag creation and push                         |

## Code Requirements

Key rules:

- Immutable data types (sealed records, `init` properties)
- No default parameters - use overloads
- Explicit access modifiers on all members
- Errors as values, not exceptions for control flow
- Total functions - every function returns a valid result for all inputs
- UK English for identifiers, comments, and documentation
- ASCII only in all source files unless specific context requires otherwise
- No `TODO`, `HACK`, or `FIXME` markers in committed code

## Analyser Settings

All projects build with:

- `TreatWarningsAsErrors=true`
- `AnalysisLevel=latest-all`
- `EnforceCodeStyleInBuild=true`
- `Nullable=enable` with nullable warnings as errors

Analyser suppressions require a documented justification in a pragma comment.

## Licence

Apache License 2.0. By contributing, you agree that your contributions will be licensed under the same licence.
