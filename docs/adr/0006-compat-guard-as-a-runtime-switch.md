# 6. Compat guard as a runtime switch

Date: 2026-10-03 · Status: Accepted (implemented in M5). It refines 0005; it does not supersede it.

## Context
Jellyfin has no upper ABI bound for plugins, so a plugin built for 12.x loads on 13.x whether or not it still works.
An exception in `RegisterServices` disables the whole plugin, and plugin configuration is not loaded yet when services
are registered, so a registration-time guard cannot read an admin's "force enable" setting.

## Decision
Always register the decorator and the filters. They act only while `CompatState.Active` is true: the server version
is inside the tested range `[12.0, 13.0)`, or the admin ticked "Run on this untested Jellyfin version". The force-enable
setting is read on every call. Outside the range Currents titles play through their `.strm` files (degraded mode),
a warning goes to the log and Jellyfin's activity log, and the admin page shows a banner. Search keeps its own switch.

## Consequences
- Forcing the plugin on or off needs no restart.
- A Jellyfin whose interfaces changed in a binary-incompatible way still fails to load the plugin before any guard
  runs. That is Jellyfin's behavior and the guard cannot prevent it.
- `CURRENTS_COMPAT_TEST_VERSION` overrides the detected version. It exists so the guard can be tested on the dev stack
  without a second Jellyfin version.
