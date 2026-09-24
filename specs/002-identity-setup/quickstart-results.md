# Quickstart Validation Results

**Feature**: `002-identity-setup` | **Run**: 2026-09-24 | **Task**: T125

Every section of [quickstart.md](./quickstart.md) was run against the reference
deployment (`docker compose`, image built from this branch), or through the automated
check the section names.

**Environment**: Windows 11, Docker 28.5.1, .NET 10.0.401, Node 24.19. This is a developer
workstation. Only §11 ran under the reference limits (2 CPUs, 4 GB, via
`docker-compose.perf.yml`). The other timings show headroom; they don't certify the
budgets on reference hardware.

## Results

| § | Scenario | Criteria | Result |
|---|---|---|---|
| 1 | Claim a fresh installation | SC-001, US1 | ✅ ready in **8.5 s**; one `SETUP CODE` event in the logs, shaped `XXXX-XXXX-XXXX`; setup 201 and signed in as administrator **8.9 s after `compose up`** (budget 5 min); a second setup is `409 identity.setup_already_completed`; no setup code logged after `compose restart app` |
| 2 | Nothing administrative is open | SC-002, US2 | ✅ anonymous `GET /api/v1/sites` is `401 identity.unauthenticated`; `/health/ready` is 200; the public surface answers without credentials; `EndpointAuthorizationCoverageTests` 1/1; architecture tests 18/18 |
| 3 | Per-site visibility | SC-003, US4 | ✅ an editor on A lists only A; B is 404 and byte-identical to a random id apart from `traceId`; adding a host name on A is `403 identity.forbidden`; the users list is `403`; `SiteVisibilityTests` 6/6 |
| 4 | Revocation on the next request | SC-004 | ✅ `RevocationTests` 7/7 (two instances, one database) |
| 5 | Throttling without lockout | SC-005, SC-006 | ✅ after the guide was corrected (see Deviations): five failures across addresses give `401` ×5, then `429` with `Retry-After: 900`, and the right password is also refused; `ThrottlingTests` 8/8; `CredentialOracleTests` 1/1 |
| 6 | Operator recovery | SC-007, US5 | ✅ `identity recovery-link` printed the path and the "Prepend the address" instruction; the link set a new password (204), which then signed in; events recorded `operator_recovery_used`, `set_password_link_issued` (issued by `operator`) and `set_password_link_used`; **1.7 s** from command to new password (budget 5 min) |
| 7 | Upgrading from 001 | SC-008 | ✅ **real in-place upgrade**: 001 (`75d8b8b`) built and run, two sites created, this branch built and started on the same volume. Both host names resolved before, during and after; admin API `428` until setup; both sites listed after setup. `UpgradeFrom001Tests` 1/1 |
| 8 | Logs and retention | SC-009, SC-012 | ✅ `LoggingTests` 4/4 (including the new no-personal-data sweep over setup, sign-in, user creation, email change and link use); `SetupCodeLoggingTests` 3/3; `IdentityRetentionTests` 2/2 |
| 9 | Admin shell baselines | SC-010 | ✅ lint clean (no raw text); 29/29 tests across 7 files, including axe and keyboard checks |
| 10 | Setup-code and link integrity | SC-011 | ✅ `SetupTests` 9/9; `SetPasswordLinkTests` 5/5 |
| 11 | Sign-in under load | SC-013 | ✅ after a fix (see Deviations): sign-in **p95 827 ms** (max 969 ms, target ≤ 1 s); `GET /api/v1/sites` **p95 4 ms idle and 4 ms during the burst** (limit 2×); no slow-hashing warning |

**11 of 11 pass.**

## Deviations found by this run

**§11 failed first, and was a real defect.** The first performance run met the sign-in
target (p95 587 ms) but failed the second half of SC-013. Other admin requests went from
p95 4 ms idle to **267 ms** during the burst. Ten concurrent PBKDF2 hashes used up the
container's 2-CPU quota. Request-path hashing now goes through a process-wide gate
allowing `ProcessorCount - 1` hashes at once, which always leaves a core free. The trade
is visible in the numbers: other requests are unaffected, and sign-in p95 rose to 827 ms,
still inside the target but with less margin. Recorded in research R-19.

**§5 as first written could not be reproduced, and the guide was wrong.** Its loop sent six
wrong passwords for *one* address and promised `401` ×5 then a 15-minute block. The run
returned `401, 401, 401, 429, 429, 429` with `Retry-After: 1`. That is the per-account
delay (clarification B), which starts at the third failure. A delayed attempt checks no
password, so it never counts toward the client block. The behaviour matches the spec. The
guide now demonstrates the client block with failures spread across addresses, and
describes the account delay separately.

**§8 named the wrong test class.** `SecurityEventRetentionTests` is `IdentityRetentionTests`
(T116), which also covers throttles, sessions, links and known devices. Corrected.

## What this run does not establish

- **The reference-hardware budgets for §1 and §6.** 8.9 s and 1.7 s leave a large margin
  against 5 minutes, but they were measured on a workstation.
- **The §1 browser redirects** (`/admin/` → `/admin/setup`, and back to Sites after
  setup). These are client-side routing and are covered by the admin router and screen
  tests, not by an HTTP probe. The run exercised the same API calls the wizard makes.
- **The §4 browser walk-through.** It is covered by `RevocationTests` across two instances,
  not by hand in two browsers.
