# 3. One global catalog source, plus search auto-add

Date: 2026-10-01 · Status: Accepted

## Context
Jellyfin libraries are shared; per-user catalogs would duplicate titles. AIOMetadata already aggregates
TMDB/TVDB/MAL/Trakt/MDBList catalogs and maps ids.

## Decision
The admin selects AIOMetadata catalogs that sync into shared libraries. Opening a search result for a title
not yet in the library adds it (M3, via MVC action filters because Jellyfin 12's `ISearchProvider` only
returns existing items). Titles leaving catalogs are pruned after N consecutive successful syncs unless
watched or search-added.

## Consequences
One library for all users; library visibility uses Jellyfin permissions. Search-added titles are tagged
so they can be cleaned up separately.
