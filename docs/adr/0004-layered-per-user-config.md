# 4. Layered per-user AIOStreams configuration

Date: 2026-10-01 · Status: Accepted (implemented in M2)

## Context
Households share a server but may use different debrid accounts and stream preferences.

## Decision
Precedence: user self-service (if allowed) → admin per-user override → global default → none.
Per-user records live in `users.json`; credentials are write-only over the API.

## Consequences
M1 uses only the global default. All users share the server's outbound rate limit toward AIOStreams.
