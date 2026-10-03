# Currents — agent guide

Jellyfin 12.1 plugin (C#, net10.0) bringing AIOMetadata catalogs and AIOStreams playback into a real Jellyfin library.

## Commands
- Build: `dotnet build -c Release` (warnings are errors)
- Unit tests: `dotnet test --filter "Category!=Integration"`
- Format check: `dotnet format --verify-no-changes`
- Dev server: `docker compose -f dev/docker-compose.yml --env-file dev/.env up -d` (Jellyfin on :8097)
- Dev setup: `dev/bootstrap-jellyfin.sh` (wizard + API key), `dev/make-samples.sh` (sample media), `dev/deploy-plugin.sh` (build and deploy after every change)

## Where things are
- Design spec: `docs/superpowers/specs/2026-10-01-currents-design.md`; plans: `docs/superpowers/plans/`
- ADRs: `docs/adr/`; spike findings: `docs/spikes/`
- Code map: `docs/architecture.md`

## Rules
- Only `src/Jellyfin.Plugin.Currents/Integration/` may touch Jellyfin internals (decorators, filters, library queries).
- Never log credentials; pass every URL through `SecretMasker.Mask`.
- File writes are atomic (tmp + move) and idempotent. Managed title folders carry a `.currents` marker. An unmarked existing folder may only be adopted when it holds nothing but plugin-owned files (`*.strm`, `movie.nfo`, `tvshow.nfo`, subtitles Jellyfin saved for one of those `.strm` files (`{strm name}.….{srt|vtt|ass|ssa|sub|idx|sup|smi}`), and `Season NN`/`Specials` folders holding only `*.strm` and such subtitles); otherwise refuse to write. Deleting never touches unmarked folders, removes only plugin files (including those subtitles), and removes the marker last, only when the folder is otherwise empty.
- Manifest URLs go in request bodies, not query strings.
- `StrmBaseUrl` must be reachable by clients and by Jellyfin itself in degraded mode, including Jellyfin's base URL path if set (see `docs/architecture.md`).
- Test first; unit tests use fakes in `tests/.../TestSupport`, never a live Jellyfin.
- Do not use the maintainer's personal Jellyfin; use the `dev/` stack.
- Conventional Commits.
