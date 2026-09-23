# Quickstart & Validation Guide

**Feature**: `002-identity-setup` | **Date**: 2026-09-23

This guide explains how to take a fresh installation through setup and how to verify
each success criterion. It covers validation only. Tables are defined in
[data-model.md](./data-model.md), endpoints in
[contracts/identity-api.yaml](./contracts/identity-api.yaml), access rules in
[contracts/authorization.md](./contracts/authorization.md), and the CLI in
[contracts/operator-commands.md](./contracts/operator-commands.md).

---

## Prerequisites

The prerequisites are the same as for 001. Operators need a container runtime.
Developers also need the .NET 10 SDK, Node 22+, and a container runtime for
Testcontainers.

---

## 1. Claim a fresh installation (SC-001, US1)

```bash
docker compose down -v && docker compose up -d
docker compose logs app | grep -A3 "SETUP CODE"      # the boxed banner (R-09)
```

1. Open `http://127.0.0.1:8080/admin/`. It redirects to `/admin/setup`.
2. Enter the setup code, a name, an email address and a password of at least 12
   characters. Submitting signs you in and opens the Sites screen.
3. Reload `/admin/setup`. It redirects to the Sites screen, and a direct
   `POST /api/v1/identity/setup` returns `409 identity.setup_already_completed`.
4. Run `docker compose restart app`. The logs no longer show a setup code.

**Timing**: the whole run, from `docker compose up` to being signed in, should take
under 5 minutes. The wizard itself should take under 2 minutes.

**Automated equivalent**: the smoke test reads the code from `docker compose logs`,
completes setup, then creates, lists and resolves a site as that administrator (FR-034).

---

## 2. Nothing administrative is open (SC-002, US2)

```bash
curl -i http://127.0.0.1:8080/api/v1/sites                    # 401 identity.unauthenticated
curl -i http://127.0.0.1:8080/health/ready                    # 200: health is always open
curl -i -H "Host: example.test" http://127.0.0.1:8080/        # public surface: never asks for credentials
```

**Automated checks**:
- The architecture test (R-06) fails the build if any controller action lacks a
  declaration, or if an `[AllowAnonymous]` sits outside the allow-list.
- `EndpointAuthorizationCoverageTests` calls **every** endpoint in `EndpointDataSource`
  without a session.

---

## 3. Per-site visibility (SC-003, US4)

Signed in as the administrator:
1. Create sites **A** and **B**.
2. Create a user with grants
   `{ "administrator": false, "siteRoles": [{ "siteId": "<A>", "role": "editor" }] }`.
   Copy the link from the dialog, open it in a private window, set a password, and sign
   in as that user.
3. As the editor, the checks below should hold:

| Call | Expected |
|---|---|
| `GET /api/v1/sites` | Only A |
| `GET /api/v1/sites/<B>` | 404, byte-identical to `GET /api/v1/sites/<random uuid>` apart from `traceId` |
| `POST /api/v1/sites/<A>/host-names` | 403 `identity.forbidden` |
| `GET /api/v1/identity/users` | 403 `identity.forbidden` |

**Automated check**: `SiteVisibilityTests` runs this matrix for every role and every
site-scoped endpoint, and compares the 404 bodies byte for byte.

---

## 4. Revocation takes effect on the next request (SC-004)

Keep the editor signed in, in one browser. As the administrator, do each of the
following in turn and refresh the editor's window after each:

| Administrator action | Editor's next request |
|---|---|
| Change the editor's role on A to designer | Succeeds, and is judged as a designer |
| Remove every site role | Sees no sites and a "you have no access yet" message |
| Choose "sign out everywhere" | 401, back to sign-in; the account can sign in again straight away |
| Change the editor's email address | 401; the old address no longer signs in |
| Deactivate the account | 401; sign-in fails with the generic message |

**Automated check**: `RevocationTests` runs this with two app instances sharing one
database. The revoking action goes to instance 1 and the next request to instance 2.

---

## 5. Throttling without lockout (SC-005, SC-006)

```bash
for i in 1 2 3 4 5 6; do
  curl -s -o /dev/null -w "%{http_code}\n" -X POST http://127.0.0.1:8080/api/v1/identity/sign-in \
    -H 'content-type: application/json' -H 'X-QuickBuilder-Request: 1' \
    -d '{"email":"admin@example.com","password":"wrong-password-123"}'
done
```

The first 5 attempts return `401`. The 6th returns `429` with `Retry-After`, and from
this client even the correct password now gets `429` for 15 minutes.

**Automated checks**:
- `ThrottlingTests` (SC-005): a blocked client stays blocked across two instances.
  While another client hammers the account, the owner signs in at once from a browser
  they have used before, and within 60 seconds from a new one (known devices, R-07).
- `CredentialOracleTests` (SC-006): 1,000 attempts split between unknown accounts and
  wrong passwords produce identical bodies, and their medians differ by less than 10%.

---

## 6. Operator recovery (SC-007, US5)

```bash
docker compose exec app dotnet QuickBuilder.Host.dll identity recovery-link --email admin@example.com
```

Open the printed path, prefixed with `http://127.0.0.1:8080`, then set a new password and
sign in. The security events table then holds `operator_recovery_used`,
`set_password_link_issued` (issued by the operator) and `set_password_link_used`.

**Target**: the whole recovery takes under 5 minutes.

---

## 7. Upgrading from 001 (SC-008)

**Manual check**:
1. Check out the 001 tag and run it with its compose file.
2. Create two sites.
3. Build this branch and run `docker compose up -d` against the same volume.

The admin shell then shows setup, `curl -H "Host: <site host>" http://127.0.0.1:8080/`
still resolves throughout, and both sites are listed after setup.

**Automated check**: `UpgradeFrom001Tests` applies only the `sites` migrations, seeds
data, starts the full application, and makes the same three checks.

---

## 8. Logs and retention (SC-009, SC-012)

- `LoggingTests` (from 001, extended) sign in, fail sign-in, create a user, change an
  email address, and use a link. It then asserts that no email address, display name,
  password, token or session identifier appears in captured log output. The setup-code
  event is the only exception, and a separate test asserts that it is not emitted after
  setup (R-09).
- `SecurityEventRetentionTests` (SC-012) seeds events on either side of the cutoff,
  advances the `TimeProvider` by 366 days, runs one sweep, and asserts that only the
  newer events remain, unchanged.

---

## 9. Admin shell baselines (SC-010)

```bash
cd src/admin && npm run lint && npm run test
```

`lint` fails on any raw string in the new screens. `test` runs axe and keyboard-only
checks on the setup, sign-in, set-password, account and users screens.

---

## 10. Setup-code and link integrity (SC-011)

`SetupTests` and `SetPasswordLinkTests` cover these cases, and each must be refused with
zero accounts created or changed:
- missing, wrong or old setup codes;
- parallel setup submissions, where exactly one succeeds;
- used, expired and replaced links;
- links for deactivated accounts;
- links issued before an email change.

---

## 11. Sign-in under load (SC-013)

```bash
dotnet test tests/QuickBuilder.SmokeTests --filter "Category=Performance"
```

This runs the reference compose file with `docker-compose.perf.yml` (app limited to 2
CPUs and 4 GB), sends 10 concurrent sign-ins × 20 rounds, and asserts:
- p95 ≤ 1 s;
- p95 of `GET /api/v1/sites` during the burst ≤ 2× the idle baseline.

If the startup warning "password hashing took … ms" appears, the host is slower than the
reference environment. The run reports this instead of failing silently.
