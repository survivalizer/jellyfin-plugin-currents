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

## Releasing
1. Set the new version in `Directory.Build.props` (`<Version>`, `<AssemblyVersion>`, `<FileVersion>`) and
   `build.yaml` (`version`), and write the release notes in `build.yaml` `changelog`. Commit to `main`.
2. Tag and push: `git tag v0.2.0 && git push origin v0.2.0` (the tag must equal `<Version>`).
3. `.github/workflows/release.yml` runs the tests, builds `currents_<version>.0.zip` with
   [jprm](https://github.com/oddstr13/jellyfin-plugin-repository-manager), creates the GitHub Release
   (`0.x` versions are marked pre-release) and adds the version to `manifest.json` on the `gh-pages` branch.
4. Jellyfin servers that added `https://survivalizer.github.io/jellyfin-plugin-currents/manifest.json` as a
   plugin repository see the update in the plugin catalog.

One-time setup (already done for this repository): after the first release created the `gh-pages` branch,
enable GitHub Pages from it (Settings -> Pages -> Deploy from a branch -> `gh-pages` / root, or
`gh api -X POST repos/survivalizer/jellyfin-plugin-currents/pages -f 'source[branch]=gh-pages' -f 'source[path]=/'`).
The repository must stay public so Jellyfin can download the manifest and release assets.

If a release run fails part-way, fix the cause and use **Re-run all jobs**: the release step re-uploads the asset
to an existing release, and the manifest step replaces that version's entry (new checksum and timestamp) instead of
adding a duplicate; if nothing changed it skips the commit.
