# 2. Real-file titles, ephemeral per-user versions

Date: 2026-10-01 · Status: Accepted — M0 confirmed versions display; playback needs synthetic-id resolution (see docs/spikes/2026-10-m0-findings.md)

## Context
Gelato stores titles and streams as database rows and decorates ~10 core services; Jellyfin 12's
`MigrateLinkedChildren` and library scans have wiped such items, and watch state splits across versions.

## Decision
Titles are real `.strm` + `.nfo` files in plugin-managed folders. Stream versions (M2) are generated per
request and per user and are never persisted. Every `.strm` targets a signed resolve endpoint so playback
works even without the version decorator ("degraded mode").

## Consequences
Titles survive scans, migrations and upgrades; watch state attaches to one stable item. Search auto-add
must write files before an item exists. Synthetic versions depend on Jellyfin accepting non-persisted
media-source ids. The M0 spike showed they display in the Version dropdown, but the web client assumes every
media-source id is a library item id, so M2 must resolve synthetic ids to the base item, attach `MediaStreams`
to each version and set `RunTimeTicks` on the base item (fallback: persist version rows only).
