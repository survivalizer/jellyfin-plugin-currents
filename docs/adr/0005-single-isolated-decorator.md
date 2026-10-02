# 5. One decorator, isolated in Integration/

Date: 2026-10-01 · Status: Accepted (implemented in M2)

## Context
The native Version dropdown requires media sources from `GetStaticMediaSources`, which the supported
`IMediaSourceProvider` API never feeds, and it receives no user.

## Decision
Decorate only `IMediaSourceManager` (plus MVC filters for search in M3). All such code lives in
`Integration/`, is covered by reflection-based contract tests, and is disabled by a version guard on
untested Jellyfin majors.

## Consequences
Upgrades touch one folder; if the decorator is disabled, playback still works through `.strm` files.
