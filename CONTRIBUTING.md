# Contributing to Currents

## Setup
1. .NET SDK 10 (`global.json` pins the feature band).
2. Docker Desktop for the dev stack (`dev/README.md`).

## Workflow
- Branch from `main`; open a PR. CI must pass (build with warnings as errors, `dotnet format`, unit tests, CodeQL).
- Commits follow [Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `docs:`, `test:`, `chore:`, `ci:`.
- Write the failing test first. Unit tests must not need a running Jellyfin; tests that do are tagged `[Trait("Category", "Integration")]`.
- Code that touches Jellyfin internals (decorators, filters, `ILibraryManager` queries) lives only in `src/Jellyfin.Plugin.Currents/Integration/`.
- Never log secrets: pass URLs through `SecretMasker.Mask`.
- Architecture changes get an ADR in `docs/adr/`.

## Running
    dotnet test --filter "Category!=Integration"
    dev/deploy-plugin.sh
