# Implementation Plan: Identity, First-Run Setup and Authorization

**Branch**: `002-identity-setup` | **Date**: 2026-09-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-identity-setup/spec.md`

**Issue**: #2. This feature closes exception **E-1** from the
[001 plan](../001-foundation-skeleton/plan.md#complexity-tracking).

## Summary

This feature takes the admin surface from open to owned. A new `Identity` module owns
accounts, sessions, per-site role grants, set-password links, sign-in throttling,
security events and the first-run setup wizard. Every existing and future admin endpoint
declares what it requires and is checked on the server.

The approach, detailed in [research.md](./research.md):
- **Framework usage**: ASP.NET Core's authentication and authorization *middleware*,
  but not ASP.NET Core Identity. Its hard lockout, interval-based revalidation and global
  roles contradict clarified spec decisions (R-02).
- **Sessions**: server-side session rows looked up on every request. An opaque cookie
  token is stored only as a hash, and grants are never cached. Every revocation and
  permission change applies on the next request on every instance (R-04).
- **Declaring permissions**: capabilities are declared with dependency-free attributes
  in the shared kernel, so Sites and later modules never reference Identity (R-05).
- **Checking declarations**: an architecture test plus an endpoint-enumerating
  integration test prove no admin operation is left undeclared (R-06).
- **Throttling**: throttle state lives in PostgreSQL and is updated with atomic upserts. A known-device cookie keeps
  the owner from being locked out by timed attacks (R-07).
- **Password hashing**: PBKDF2-HMAC-SHA512 at 210,000 iterations, the OWASP figure,
  through the BCL (R-03).
- **Setup**: a single-row claim with a conditional update gives setup-exactly-once
  (R-12).
- **Setup code**: stored once and printed at every startup, so restarts and extra
  instances agree (R-09).

There are no new NuGet or npm packages. The only new third-party material is an
MIT-licensed common-password data file (R-15).

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript (strict) for the admin app. Unchanged from 001.

**Primary Dependencies**: ASP.NET Core MVC and its authentication/authorization
middleware (in the framework), EF Core 10 + Npgsql, and BCL cryptography
(`Rfc2898DeriveBytes.Pbkdf2`, `RandomNumberGenerator`, `SHA256`,
`CryptographicOperations`). Vue 3, Pinia, vue-router and vue-i18n are already present.
Nothing new is added (see research "New dependencies" in R-15).

**Storage**: PostgreSQL 16+, with a new `identity` schema of 8 tables
([data-model.md](./data-model.md)). The `sites` schema is unchanged.

**Testing**: xUnit, `WebApplicationFactory` and Testcontainers PostgreSQL, as in 001,
plus:
- architecture tests for declaration coverage;
- an integration test that enumerates every endpoint;
- two-instance tests for revocation and throttling;
- a snapshot-upgrade test from the 001 schema;
- a smoke-stage performance test under a 2-CPU/4 GB limit;
- vitest + axe for the new screens.

**Target Platform**: Linux containers (amd64 in CI), unchanged.

**Project Type**: Web application: a modular-monolith backend plus the Vue admin SPA it
serves.

**Performance Goals**:
- Sign-in p95 ≤ 1 s with 10 happening at once on 2 vCPU/4 GB (SC-013).
- Other admin requests stay within 2× their idle p95 during that burst.
- Session validation adds one indexed query per admin request.

**Constraints**:
- Revocation and permission changes apply on the next request on every instance
  (FR-017).
- Zero outbound network traffic (FR-035).
- No PII in logs; the setup code is the single documented exception (FR-004a).
- Plain HTTP sign-in is accepted from loopback only (R-04).

**Scale/Scope**:
- Accounts: tens per installation, with the design comfortable to low thousands.
- Security events: a few thousand per day at most, kept 365 days.
- Code: 1 new module (2 projects).
- Admin shell: 5 new screens.
- Endpoints: 18 new ones, and 4 existing Sites endpoints gain declarations.

## Constitution Check

*GATE: passed before Phase 0 and re-checked after Phase 1 design (result at the end of
this section).*

| # | Principle | Compliance |
|---|---|---|
| I | Modular Monolith with Vertical Slices | **Complies.** The `Identity` module ships as `QuickBuilder.Identity.Contracts` + `QuickBuilder.Identity`, with slices under `Features/`, its own schema, `DbContext` and migration history, and a single `AddIdentityModule()`. Identity reads sites only through `ISiteDirectory` (Sites.Contracts). Sites doesn't reference Identity at all: it declares capabilities through kernel attributes and filters through the kernel's `IAccessEvaluator` (R-05). No transaction spans modules. Setup lives in Identity rather than the constitution's `Setup` module, because FR-003's exactly-once claim needs the claim and the first account in one transaction. The constitution lists modules but doesn't say what each must contain, so this is an interpretation, not an exception (R-01). What the shared projects gain: the kernel gets `Authorization/` (plain types, no module names); `Shared.Infrastructure` gets `IHostCommand`. Both stay generic, as the rule requires, and the existing "shared depends on no module" test is extended to cover Identity. With a second module, the "no module references another's implementation" rule stops being vacuous for the first time. |
| II | Easy to Install, Operate and Upgrade | **Complies.** The first run shows the setup wizard and there are **no default credentials** (FR-005), which 001 could only satisfy vacuously. The setup code is read from `docker compose logs`, the step operators already use to see startup, so the 5-minute install still holds (SC-001). Every new configuration key has a documented default and startup validation (research, "Configuration keys"). Migrations are additive and automatic. Nothing needs email or network access. Health endpoints are untouched by setup mode (FR-006). |
| III | Upgrades Never Lose Data | **Complies, and exercised for the first time.** The Identity migration only adds a schema. An installation running 001 upgrades by replacing the image, enters setup mode and keeps every site. `UpgradeFrom001Tests` is the constitution's "upgrade a database snapshot from the previous release" test (SC-008, R-18). |
| IV | Pluggable Infrastructure, Minimal Required Dependencies | **Complies.** PostgreSQL is still the only dependency. Sessions, throttles and the setup claim live in the database, so identity is correct across instances without any shared cache. The only in-memory state is the one-way "installation is claimed" flag, which is safe on every instance (R-12). 001's E-4, the cache-invalidation gap, is unaffected: identity takes no dependency on the cache. |
| V | Multi-Site Within One Installation | **Complies.** This is the principle's "permissions can be scoped per site", delivered through Designer and Editor site grants and site-scoped capabilities read from `{siteId}` (FR-020–FR-023). `role_grants.site_id` *refers to* a site but is not site-owned: grants are installation data read on an admin surface that has no resolved site. The entity therefore carries the kernel's `[InstallationScoped("reason")]`, and the site-owned architecture rule accepts that marker only with a non-empty reason and a dedicated test. This is not a filter bypass: there is no filter to bypass. |
| VI | Content Schemas Are Versioned Contracts | **N/A**: no content schemas. |
| VII | Draft and Published Are Separate Worlds | **N/A**: no content. |
| VIII | Rendering Is Server-Side, Cached, Invalidated | **N/A**: the public surface is untouched, and throttle and setup checks never run on it. |
| IX | Secure by Default | **Complies, and E-1 is closed.** Every endpoint is authorized on the server by policy (R-05), and completeness is proven by the build (R-06). On role scope, the constitution's "(roles: Administrator, Designer, Editor, scoped per site)" is read together with Principle V's "permissions **can** be scoped per site". Designer and Editor are site-scoped. Administrator is installation-wide, per clarification Q3. The grant model records scope explicitly (FR-020a), so a per-site administrative role can be added later without a data migration. Login is rate-limited per client and per account without hard lockout (R-07). Cookies are HttpOnly and SameSite=Strict, and Secure over HTTPS. Plain-HTTP credentials are refused from non-loopback clients (R-04), and there are CSRF defences (R-11). There is no PII in logs (FR-030). The setup code in console output is not PII: the constitution forbids PII and content bodies in logs, and the code is a one-time, pre-claim value. It is recorded as a narrow exception to 001's stricter FR-032, with tests on both sides (R-09). No secrets are in the repo or image. |
| X | Test-First for Behaviour, Tests Always for Contracts | **Complies.** Every new endpoint gets integration tests against real PostgreSQL. Hashing, throttle arithmetic, code formats and grant validation get unit tests. The release smoke test gains **setup wizard → sign-in → create site → view as signed-in administrator**, which is the first step of the constitution's specified sequence (FR-034). E-3 therefore narrows but stays open until Pages and Rendering exist. The coverage gate's critical paths gain authentication, authorization and setup. |
| XI | Simplicity and Incremental Delivery | **Complies.** No ASP.NET Core Identity, OIDC server, validation library or rate-limit package: each was weighed and rejected in research. The only new third-party item is a filtered data file (R-15). The feature ships whole in one PR, so no feature flag is needed. Speculative items stay out: no security-events browser, no per-site admin role, no email, and no `SiteDeleted` handler until site deletion exists. |

**Technology Constraints**:
- MVC controllers only.
- EF Core only. The throttle upserts and advisory locks use parameterised
  `ExecuteSql`/`SqlQuery` inside the Identity module that owns those tables. None of
  those tables is site-owned, so the rule to "apply the site filter explicitly" doesn't
  bind here, and the code comment says so.
- Read paths use `AsNoTracking` projections.
- Admin assets are built into the image, as before.

**Development Workflow**:
- Issue #2 → `specs/002-identity-setup` → branch `002-identity-setup` → one PR that
  closes #2.
- **Branch**: `002-identity-setup`, created from `main` after the admin move (#6)
  merged, so every `src/admin/` path in this plan matches the code.
- 001's `spec-ids.yml` check resolves `002` to issue #2, which must exist and must not be
  a pull request.

**Operator impact** (required by the workflow section):
- **New configuration keys**: 13, all optional with safe defaults. They are listed in
  [research.md](./research.md#configuration-keys-introduced) and are to be added to
  `docs/configuration.md`.
- **Migrations**: one, creating the `identity` schema. It is additive and applied
  automatically.
- **New dependencies**: none (packages). One bundled data file.
- **Upgrade notes**:
  1. After upgrading, the admin interface asks for setup. Read the setup code from the
     app logs, or run `identity setup-code`.
  2. Every `/api` call now needs a session. Scripts written against 001's
     unauthenticated API stop working. There is no release yet, so no semver promise is
     broken, but the release notes must say so.
  3. The warning at the port binding in `docker-compose.yml` is replaced (FR-033). The
     binding stays `127.0.0.1` by default, and widening it is now safe *after* setup.
  4. Behind a TLS proxy, list it in `Hosting:KnownProxies`. Otherwise sign-in is refused
     as insecure (R-04) and per-client throttling would treat all users as one client.

**Post-Phase-1 re-check**: the data model, the three contracts and the quickstart
introduce no violation. E-1 is closed. E-3 narrows, E-4 is unchanged, and no new
exception is needed. **Gate: PASS.**

## Project Structure

### Documentation (this feature)

```text
specs/002-identity-setup/
├── plan.md              # This file
├── spec.md              # Feature specification (clarified)
├── research.md          # Phase 0: decisions R-01 … R-19
├── data-model.md        # Phase 1: identity schema, kernel types, capabilities
├── quickstart.md        # Phase 1: validation guide, one section per success criterion
├── contracts/
│   ├── identity-api.yaml       # HTTP contract for /api/v1/identity/**
│   ├── authorization.md        # How every endpoint declares access; binding on later modules
│   └── operator-commands.md    # identity setup-code / recovery-link; IHostCommand
├── checklists/
│   └── requirements.md
└── tasks.md             # Created by /speckit-tasks, not by this command
```

### Source Code (repository root)

New items are marked `+`, changed items `~`.

```text
src/
├── QuickBuilder.Host/
│   ~ Program.cs                    # + AddIdentityModule(), UseAuthentication/UseAuthorization,
│   │                               #   UseIdentityModule() (setup gate + request guard),
│   │                               #   IHostCommand dispatch replacing the `migrate` special case,
│   │                               #   AllowAnonymous on the SPA fallback
│   ~ appsettings.json              # + Identity section, Hosting:PublicUrl
│   └ Startup/StartupTasks.cs       # ~ MigrateCommand becomes an IHostCommand
├── Shared/
│   ├── QuickBuilder.Shared.Kernel/
│   │   + Authorization/            # SystemRole, CapabilityScope, CapabilityDefinition,
│   │                               # RequireCapability, RequireSignedIn, InstallationScoped,
│   │                               # IAccessEvaluator, SiteVisibility (R-05)
│   └── QuickBuilder.Shared.Infrastructure/
│       + Commands/IHostCommand.cs  # (R-16)
└── Modules/
    ├── Sites/QuickBuilder.Sites/
    │   ~ SitesModule.cs            # + registers sites.* CapabilityDefinitions
    │   ~ Features/*/…              # + declarations; ListSites filters via IAccessEvaluator
    │   └ Features/ResolveCurrentSite  # + [AllowAnonymous]
    └── Identity/                   # + new module
        ├── QuickBuilder.Identity.Contracts/
        │   ├── UserSummaryDto.cs, IUserDirectory.cs   # attribution for later modules
        │   └── IIdentitySetupStatus.cs                # "is the installation claimed?"
        └── QuickBuilder.Identity/
            ├── IdentityModule.cs   # AddIdentityModule(), UseIdentityModule()
            ├── Domain/             # User, RoleGrant, Session, SetPasswordLink,
            │                       # Installation, SecurityEvent
            ├── Infrastructure/     # IdentityDbContext, Migrations/, retention sweep,
            │   │                   # IdentityCommands (IHostCommand), SetupStatus, bootstrapper
            │   ├── Authentication/ # SessionService, SessionAuthenticationHandler, cookie
            │   ├── Authorization/  # model convention, policy provider, capability
            │   │                   # handler, result handler (401/403/404), AccessEvaluator
            │   ├── Security/       # Pbkdf2PasswordHasher, PasswordPolicy + Resources/,
            │   │                   # SignInThrottle, KnownDevices, Tokens, TransportSecurity,
            │   │                   # RequestGuard (CSRF), SecurityEventRecorder, link service,
            │   │                   # AdministratorGuard
            │   └── Setup/          # SetupGateMiddleware
            └── Features/
                ├── GetStatus/  CompleteSetup/  SignIn/  SignOut/  GetMe/
                ├── ChangeOwnPassword/  RevokeOwnOtherSessions/
                ├── CheckSetPasswordLink/  SetPassword/
                └── ListUsers/  GetUser/  CreateUser/  UpdateUser/  ReplaceGrants/
                    DeactivateUser/  ReactivateUser/  IssueSetPasswordLink/  RevokeUserSessions/

src/admin/                          # the admin SPA (moved from the repository root)
├── src/
│   ~ api/                          # request() adds X-QuickBuilder-Request, handles 401/428;
│   │                               # + identity.ts typed client
│   ~ app/router.ts                 # + guards, setup/sign-in/set-password/account/users routes
│   + app/authStore.ts
│   + features/identity/            # SetupView, SignInView, SetPasswordView, AccountView
│   + features/users/               # UsersView, UserEditView, GrantsEditor, CopyLinkDialog
│   ~ i18n/en.json                  # + all new strings
└── tests/
    + *.spec.ts                     # axe + keyboard checks per new screen

tests/
~ QuickBuilder.ArchitectureTests/   # + declaration coverage, InstallationScoped rule,
│                                   #   security-events append-only, Identity boundaries
~ QuickBuilder.UnitTests/           # + hasher, password policy, throttle arithmetic, setup code
~ QuickBuilder.IntegrationTests/    # + signed-in client helpers; Identity/*; existing Sites
│                                   #   tests move to signed-in clients; UpgradeFrom001Tests
~ QuickBuilder.SmokeTests/          # + setup → sign-in steps; Performance category (SC-013)

docker-compose.yml                  # ~ binding note rewritten (FR-033)
+ docker-compose.perf.yml           # 2 CPU / 4 GB limit for the SC-013 test
docs/
~ configuration.md                  # + Identity keys, Hosting:PublicUrl, proxy dependency
~ contributing.md                   # + "declare authorization" step, linking contracts/authorization.md
+ operations.md                     # setup code, recovery, security-event retention
```

**Structure decision**: the module follows exactly the layout 001 established, and
`Sites` stays the worked example. `Identity` keeps exactly the constitution's
`Domain/`, `Features/` and `Infrastructure/` layout (Principle I). Its framework
integration and cryptographic primitives, which no single slice owns, sit in
sub-folders of `Infrastructure/`: `Authentication/`, `Authorization/`, `Security/` and
`Setup/`. That is where Principle I puts non-slice machinery, so no exception is needed.
Everything stays `internal`, and the "module internals are not public" test still
applies.

## Complexity Tracking

Status of exceptions carried over from 001, as required by the Governance section.

| Exception | Status after this feature |
|---|---|
| **E-1** (no authentication on the admin surface) | **Closed.** Every admin endpoint is authenticated and authorized (FR-024–FR-027), proven by R-06. The loopback note in `docker-compose.yml` is rewritten (FR-033). |
| **E-2** (no media storage abstraction) | Unchanged. Issue #3. |
| **E-3** (smoke test is not the constitution's full sequence) | **Narrowed.** The smoke test now starts with the setup wizard and signs in (FR-034). Create page, publish and view remain for Pages and Rendering. Issue #4. |
| **E-4** (in-memory cache can't invalidate across instances) | Unchanged. This feature adds no cache dependency. |

**New exceptions**: none.

## Risks

- **An unlisted reverse proxy merges every client into one.** Per-client throttling
  keys on the client address. Behind an unlisted proxy, five failures by anyone block
  everyone for 15 minutes. Mitigations:
  - the HTTPS rule refuses sign-in from the proxy's forwarded plain-HTTP connection
    first (R-04), so the misconfiguration is visible before it bites;
  - `docs/configuration.md` states the dependency at the `KnownProxies` key.
- **Log shipping before setup exposes the setup code.** Anyone reading shipped logs
  before setup completes could claim the installation. The code is useless after setup.
  `docs/operations.md` advises finishing setup before enabling log forwarding, and the
  banner says so too.
- **Slower hardware than the reference environment.** 210,000 iterations may exceed the
  SC-013 budget on older or throttled CPUs. The startup self-timing warning (R-03) makes
  this visible. The iteration count can only be raised, never lowered below guidance, so
  the answer is more CPU, not weaker hashing.
- **Churn in 001's tests.** Every existing Sites integration and smoke test calls
  anonymously and will fail until it moves to signed-in helpers. This is budgeted in
  tasks, not a regression.
- **Issue #2 must exist.** The same prerequisite as 001's `spec-ids.yml` check. The local
  token lacked `repo` scope, so its existence could not be verified from this
  environment.

## Deferred (recorded, not built)

- **Designer vs Editor**: they differ once Pages, CMS and Media register capabilities.
- **Browsing security events**: spec Out of Scope. The `occurred_at` index already
  serves a future screen.
- **Grants on deleted sites**: removing grants when a site is deleted waits for a
  `SiteDeleted` event, which waits for site deletion.
- **Argon2id**: revisit if the BCL adds it. The hash format allows a transparent
  migration at sign-in (R-03).
