# Phase 0 Research: Identity, First-Run Setup and Authorization

**Feature**: `002-identity-setup` | **Date**: 2026-09-23 | **Issue**: #2

The stack is fixed by the constitution and the skeleton from feature 001, so this document
does not re-decide platform choices. It settles the design questions the spec leaves to
planning, and it justifies every new dependency, as Principle XI requires. There is only
one: a data file (R-15).

Decision numbers are prefixed `R-` so they can't be confused with 001's `D-` decisions.
Those decisions still apply here, D-07 and D-08 especially.

---

## R-01: Module placement: Identity owns setup, and there is no Setup module yet

**Decision**: This feature adds one module, `Identity`
(`QuickBuilder.Identity.Contracts` + `QuickBuilder.Identity`, schema `identity`). It owns
accounts, credentials, sessions, role grants, set-password links, sign-in throttling,
security events, **and** the first-run setup wizard. The constitution's `Setup` module is
not created by this feature.

**Rationale**: FR-003 requires that setup succeeds exactly once and that concurrent
submissions create exactly one account. That is only achievable if "the installation is
claimed" and "the first account exists" are written in one transaction. Constitution
Principle I forbids a transaction spanning two modules, so a separate `Setup` module that
owned the claim state would have to coordinate with Identity through events. That
reintroduces exactly the double-account race FR-003 rules out. Keeping both in Identity
makes atomicity a single `SaveChanges`.

**What `Setup` is left for**: installation-level setup steps that aren't about identity,
such as defaults for the first site or email settings, once a spec needs them. That
module would call Identity through `IIdentitySetupStatus` (contracts) to know whether the
installation is claimed. Nothing in this feature pre-builds it (Principle XI).

**Alternatives considered**:
- *A `Setup` module that owns the wizard and calls Identity to create the user*: the
  claim and the user row live in different schemas, so a crash between them leaves a
  claimed installation with no administrator. Recovery would need a compensating path
  that FR-003 says must not exist ("a half-created state is never possible").
- *Setup logic in the host*: the host contains no business logic (FR-001 of 001, and an
  architecture test).

---

## R-02: Use ASP.NET Core authentication and authorization, not ASP.NET Core Identity

**Decision**: Build on ASP.NET Core's **authentication** and **authorization**
primitives: a custom authentication handler, policy-based authorization and a custom
policy provider. Do **not** adopt ASP.NET Core Identity (`UserManager`, `SignInManager`,
`IdentityDbContext`, or the Identity cookie scheme).

**Rationale**: ASP.NET Core Identity is a good default for a typical application, but
several of its behaviours contradict decisions in this spec:

| Spec decision | ASP.NET Core Identity behaviour |
|---|---|
| No hard account lockout (clarification Q4, FR-015a) | `LockoutEnabled` implements exactly the hard lockout the clarification rejected |
| Revocation on the *very next* request (FR-017, SC-004) | Cookie sessions are re-validated against the security stamp on an interval (30 minutes by default); setting the interval to zero means a stamp lookup per request anyway, with none of the other benefits |
| Roles scoped per site (FR-020) | Roles are global; per-site roles need custom claims and a custom store regardless |
| Email is unverified and admin-managed (FR-010a) | Email confirmation, change-email tokens and `NormalizedEmail` flows exist and would have to be disabled one by one |
| Throttling across instances, per client and per account (FR-015) | Nothing built in; lockout counters live on the user row, keyed by account only |

Adopting it would mean overriding most of it and still writing the parts that matter. It
also ships an opinionated table set (`AspNetUsers`, `AspNetRoles`, claims, logins,
tokens) that this feature wouldn't use. The authentication and authorization *middleware*
is a different matter: it is the framework's policy mechanism, which the constitution's
"policy-based checks" (Principle IX) points to directly, and it costs nothing to adopt.

**Alternatives considered**:
- *ASP.NET Core Identity with most features disabled*: see the table. The remaining value
  (a password hasher and a user table) is small, and both are replaced below for their
  own reasons.
- *An OpenID Connect server library (OpenIddict, Duende)*: this solves SSO and machine
  clients, both out of scope. Duende's licence isn't free for redistribution in
  commercial products, and Principle XI rules out speculative infrastructure.

---

## R-03: Password hashing (FR-008, FR-009, SC-013)

**Decision**: PBKDF2-HMAC-SHA512 through the BCL (`Rfc2898DeriveBytes.Pbkdf2`), with:

- **Work factor**: 210,000 iterations by default. This is the current OWASP Password
  Storage Cheat Sheet figure for PBKDF2-HMAC-SHA512. It is configurable upward through
  `Identity:Passwords:Pbkdf2Iterations`, and startup validation refuses anything lower.
- **Salt and output**: a 16-byte random salt and a 64-byte derived key.
- **Storage format**: a self-describing string, `$pbkdf2-sha512$i=<n>$<salt-b64>$<hash-b64>`,
  so the algorithm and cost travel with each hash.
- **No Unicode normalisation (revised during implementation)**: the plan was to
  NFKC-normalise passwords, and NFC-normalise email addresses, before use. The
  solution builds with `InvariantGlobalization=true`, and in that mode `string.Normalize`
  is a **silent no-op**. A throwaway check confirmed that neither the `ﬁ` ligature nor
  `e` + a combining accent was changed. Code that called it would read as normalising
  while doing nothing.
  - **Why not enable ICU**: real normalisation needs ICU, which means about 30 MB more in
    the Alpine image, and culture-dependent string behaviour that 001 deliberately
    avoided.
  - **Why the loss is small**: mainstream keyboards emit precomposed (NFC) text, so a
    user rarely produces two different code-point sequences for one password, and email
    addresses are almost always ASCII.
  - **Consequence**: passwords are hashed exactly as typed, and emails are only trimmed
    (case-insensitivity still comes from `citext`).
  - **Length rule**: length is counted in Unicode code points (`Rune`s), which works
    without ICU, and the maximum is 128 (FR-008).
  - **Revisit** if invariant globalization is ever turned off.
- **Transparent upgrade (FR-009)**: after a successful sign-in, if the stored iteration
  count is below the configured one, the password is re-hashed in the same request.
- **Comparison**: constant-time, with `CryptographicOperations.FixedTimeEquals`.

**Performance budget (SC-013)**: a hash has to leave room for 10 simultaneous sign-ins
to complete within 1 s at p95 on 2 vCPUs. That works out to roughly five hashes per core
per second, or about 150–200 ms of CPU per hash, which is where 210,000 SHA-512
iterations sit on current x64 and arm64 cores. Two safeguards back this up:
1. At startup, the Identity module times one hash and logs a warning if it exceeds
   400 ms, so an operator on slow hardware learns why sign-in feels slow.
2. The SC-013 burst test runs in the smoke stage against the reference compose file,
   with the app container limited to 2 CPUs and 4 GB (R-19).

**Alternatives considered**:
- *Argon2id*: OWASP's first preference. It isn't in the BCL, and the maintained .NET
  packages are managed implementations that are markedly slower per unit of security
  than native ones. At OWASP's minimum of 19 MiB it also uses about 190 MiB for a burst
  of 10 sign-ins. Revisit if the BCL gains an implementation. The self-describing format
  lets a later release migrate transparently at sign-in.
- *`Microsoft.Extensions.Identity.Core`'s `PasswordHasher<T>`*: also PBKDF2, but its
  format hard-codes the header layout, it doesn't normalise input, and it would pull an
  Identity package in just to use one class.
- *bcrypt*: a third-party dependency, and it truncates input at 72 bytes, which conflicts
  with FR-008's "never silently truncated".

---

## R-04: Sessions are server-side and checked on every request (FR-016, FR-017, FR-018)

**Decision**: Each session is a database row. The browser holds only an opaque 256-bit
random token in a cookie, and the database stores the token's SHA-256 hash, never the
token itself. A custom `AuthenticationHandler` validates every admin request in a single
indexed query. The query joins the session to its user, rejects the session if it is
revoked, past its idle or absolute expiry, or if the user is deactivated, and yields a
principal carrying the user and session identifiers.

- **Revocation**: this covers sign-out, "sign out everywhere", deactivation, password
  change, email change, and a set-password link being used. Each sets `revoked_at` on the
  affected rows in the same transaction as the change that caused it. The next request
  fails validation on any instance, because no instance caches sessions. This is FR-017
  and SC-004 by construction.
- **Permission changes**: grants are **not** copied into the cookie or into claims. They
  are read per request, lazily, by the access evaluator (R-05). A permission change is
  therefore visible to the next request without touching sessions at all.
- **Idle tracking**: `last_seen_at_utc` is written at most once a minute per session, to
  avoid a write on every request. Idle expiry therefore has one-minute granularity, which
  is recorded as acceptable against a two-hour default.
- **Clock changes**: `absolute_expires_at_utc` is fixed when the session is created. A
  backwards clock jump can extend a session by at most the size of the jump, never
  indefinitely (spec edge case).
- **Cookie**: named `qb_session`, `HttpOnly`, `SameSite=Strict`, `Path` = the configured
  base path, and no `Domain` attribute. `Secure` is set whenever the request arrived over
  HTTPS (after forwarded-header processing).

**HTTPS rule** (FR-018, Principle IX "HTTPS assumed"): sign-in, setup and set-password
requests that arrive over plain HTTP from a **non-loopback** client are refused with
`identity.https_required`. Plain HTTP is allowed from loopback, which covers the reference
deployment on `127.0.0.1:8080`, where traffic never leaves the machine and browsers
treat the origin as potentially trustworthy. An operator who puts a TLS proxy in front
but forgets to list it in `Hosting:KnownProxies` sees this error instead of silently
sending credentials. The same misconfiguration would also break per-client throttling
(R-07), so failing loudly here fixes two problems at once. `Identity:AllowInsecureHttp`
exists for lab setups, and every startup with it enabled logs a warning.

**Why this fits multi-instance use (constitution IV, 001 E-4)**: all session state is in
PostgreSQL, the one shared dependency. No part of identity depends on the in-memory
cache that 001 recorded as single-instance only (E-4), so this feature adds nothing to
that exception.

**Cost**: one indexed lookup per authenticated admin request, and at most one write per
minute per session. Admin traffic is low, and this is the price of FR-017's
next-request guarantee. A short in-memory cache would break that guarantee across
instances.

**Alternatives considered**:
- *Self-contained cookie (encrypted claims) plus a security-stamp check*: a stamp check
  on every request costs the same database hit, and one on an interval violates FR-017.
- *JWT bearer tokens*: need client-side storage that scripts can read (contradicting
  FR-018) or a cookie anyway, and still need a revocation list to meet FR-017.

---

## R-05: Declaring and enforcing capabilities (FR-020 to FR-026)

**Decision**: permissions are split into a **declaration** half, which every module can
see, and an **enforcement** half, which only Identity implements.

**Declaration**, in `QuickBuilder.Shared.Kernel/Authorization`, is plain C# with no
ASP.NET Core dependency:
- `SystemRole`: `Administrator`, `Designer`, `Editor`. The constitution names these
  roles, so they are product-wide rather than specific to one module.
- `CapabilityDefinition(Name, Scope, GrantedTo)`: `Scope` is `Installation` or `Site`,
  and `GrantedTo` lists the site roles that hold the capability. `Administrator` holds
  every capability everywhere (FR-021), so it is never listed.
- `[RequireCapability("sites.host_names.manage")]`, placed on a controller action.
- `[RequireSignedIn]`, for self-service actions that any signed-in user may perform,
  such as changing their own password.
- `IAccessEvaluator`, for handlers that need access information beyond a yes/no answer,
  such as filtering a list: `IsAdministrator`,
  `HasCapabilityAsync(name, siteId?)` and `GetVisibleSiteIdsAsync()`.

**Registration**: each module registers its `CapabilityDefinition`s in DI from its
`Add<Module>Module()`. A new module adds its capabilities and maps them to existing roles
without editing Identity (FR-026).

**Enforcement**, inside `QuickBuilder.Identity`:
1. An MVC `IApplicationModelConvention` finds `[RequireCapability]` and `[RequireSignedIn]`
   on actions and adds the matching `AuthorizeAttribute(policy)` to the endpoint metadata.
   Enforcement therefore goes through the framework's authorization middleware, which
   satisfies the constitution's "policy-based" requirement, and module code never
   references ASP.NET Core authorization types.
2. A custom `IAuthorizationPolicyProvider` builds `capability:<name>` policies on demand,
   each carrying a `CapabilityRequirement`.
3. The `CapabilityRequirement` handler resolves the site for site-scoped capabilities
   from the `siteId` **route value**. Every site-scoped endpoint already names the site
   in its path, as 001 FR-011b requires. An endpoint that declares a site-scoped
   capability but has no `{siteId}` route parameter is a configuration error, caught by
   the declaration test in R-06.
4. A **fallback policy** of "authenticated user" applies to every endpoint that declares
   nothing. Forgetting a declaration therefore fails closed at runtime, and fails the
   build through R-06.
5. An `IAuthorizationMiddlewareResultHandler` maps each outcome to the spec's responses:

| Outcome | Status | `errorCode` |
|---|---|---|
| No valid session | 401 | `identity.unauthenticated` |
| Signed in, may see the site but lacks the capability (FR-023) | 403 | `identity.forbidden` |
| Signed in, may **not** see the site named in the route (FR-022) | 404 | `sites.not_found` |
| Installation-scoped capability missing | 403 | `identity.forbidden` |

The 404 body is the same problem-details document that `Sites` returns for a site that
really doesn't exist: same `type`, `title`, `detail` and `errorCode`, with only
`traceId` differing. An integration test compares the two responses directly (SC-003).

**Why the declarations sit in the kernel**: `Shared.Infrastructure` and every
`*.Contracts` project are barred from ASP.NET Core by 001's rules (contracts by an
architecture test; shared infrastructure has no framework reference). The declarations
are small, have no dependencies, and name only constitution-level concepts (roles), so
they fit alongside `ISiteOwned`. No module-specific name appears in the kernel:
capability names are strings supplied by each module.

**Alternatives considered**:
- *The attribute in `Identity.Contracts`*: contracts may not depend on ASP.NET Core, and
  a plain attribute there would make every module reference Identity just to declare a
  permission.
- *Role checks (`[Authorize(Roles = …)]`)*: FR-026 forbids scattering role names through
  operations, and global roles can't express per-site scope.
- *Resource-based authorization called by hand in handlers*: puts enforcement at the
  handler's discretion. The declarative, endpoint-level check is the one that can be
  verified for completeness (R-06).

---

## R-06: Proving that no admin operation is left undeclared (FR-025, SC-002)

**Decision**: two independent checks.

1. **Architecture test (build-time)**: reflects over every `ControllerBase` subclass in
   every module assembly. Each action must carry exactly one of `[RequireCapability]`,
   `[RequireSignedIn]` or `[AllowAnonymous]`. `[AllowAnonymous]` is accepted only on the
   actions in an explicit allow-list kept in the test, and that list mirrors the one in
   [contracts/authorization.md](./contracts/authorization.md). Every
   `[RequireCapability]` name must match a registered `CapabilityDefinition`, and every
   site-scoped one must sit on a route containing `{siteId}`. Like 001's rules, it comes
   with deliberately broken fixtures that prove each check fires (001 SC-004 pattern).
2. **Integration test (runtime, SC-002)**: builds the real app, enumerates
   `EndpointDataSource`, and calls **every** endpoint without a session. Everything not
   on the anonymous allow-list must answer 401, or 428 while in setup mode. Because the
   endpoints are enumerated rather than listed by hand, a new endpoint is covered without
   anyone remembering to add it.

**Anonymous allow-list**: `GET /api/v1/identity/status`, `POST /api/v1/identity/setup`,
`POST /api/v1/identity/sign-in`, `POST /api/v1/identity/set-password-links/check`,
`POST /api/v1/identity/set-password`, `GET /health/live`, `GET /health/ready`, the public
site surface (`/`, from 001) and the admin SPA shell and static assets.

---

## R-07: Sign-in throttling that works across instances (FR-015, FR-015a, SC-005)

**Decision**: throttling state lives in `identity.sign_in_throttles`, a table keyed
`(kind, key)` and updated with `INSERT … ON CONFLICT DO UPDATE … RETURNING` so every
check is atomic across instances. There are three kinds:

| Kind | Key | Rule |
|---|---|---|
| `client` | client address (IPv4 exact; IPv6 aggregated to its /64) | 5 consecutive failures, then **every** sign-in, setup-code and set-password attempt from that client is refused for 15 minutes. A block is recorded as a security event. |
| `account` | SHA-256 of the normalised email, **whether or not an account exists** | From the 3rd consecutive failure, the minimum wait before the next evaluated attempt doubles: 1 s, 2 s, 4 s … up to 60 s. Attempts inside the wait get `429` with `Retry-After` and are **not** checked against the password. A success resets the counter. |
| `setup` | client address | Wrong setup codes count toward the `client` block as well as their own event (US1 scenario 5). |

**Known devices** (FR-015a, added after `/speckit-analyze` finding U1): the account wait
alone can't meet SC-005. An attacker who times failures to each wait's expiry wins the
race for every evaluation slot, so the owner's correct password nearly always lands
inside a wait. The rule follows OWASP's device-cookie pattern:

- **Issuing**: a successful sign-in sets `qb_device`, a long-lived `HttpOnly`,
  `SameSite=Strict` cookie holding up to 5 opaque 32-byte tokens, one per account signed
  in from that browser. Only the SHA-256 of each token is stored, in `known_devices`.
- **Exemption**: an attempt carrying a valid device token for the target account skips
  the account wait, but never the client block.
- **Losing known status**: after 5 consecutive failures (configurable) from one device,
  its row is revoked, so a stolen cookie can't grind passwords.
- **Revocation**: devices are revoked on deactivation, email change, set-password link
  use and administrator "sign out everywhere". They are not revoked on the owner's own
  password change or on "sign out my other sessions".
- **Expiry**: devices expire after 90 days without use (configurable), removed by the
  hourly sweep (R-14).

**Alternatives rejected**:
- *Delay per account-and-client pair*: an attacker with many addresses then has no
  account-wide limit.
- *Exempting clients with no failures*: every fresh address counts as clean, so the same
  attacker walks straight past.

**Order of checks**: client block, then known-device lookup, then account wait (skipped
for a valid known device), then password verification. Both
throttles run *before* the password is hashed, so a throttled attempt costs almost no
CPU. That protects SC-013's budget during an attack.

**Why IPv6 is aggregated to /64**: a single IPv6 subscriber typically controls a whole
/64. Keying on the full address would let one attacker rotate through 2⁶⁴ addresses and
never be blocked.

**Unknown email addresses** are throttled under the same key derivation as real ones,
so throttling reveals nothing about which accounts exist (FR-015a).

**Stale rows** older than 24 hours are removed by the same hourly sweep that enforces
security-event retention (R-14).

**Risk recorded in the plan**: behind a reverse proxy that isn't listed in
`Hosting:KnownProxies`, every client appears to come from the proxy's address. Five
failures would then block everyone. R-04's HTTPS rule makes that misconfiguration fail
loudly first, and `docs/configuration.md` states the dependency explicitly.

**Alternatives considered**:
- *ASP.NET Core rate-limiting middleware*: in-memory, so it can't meet "across all
  instances". It also limits requests rather than failures, which is the wrong signal
  for FR-015's account rule.
- *Counters on the user row*: can't throttle unknown email addresses identically, which
  leaks whether an account exists.

---

## R-08: Equal responses for unknown accounts and wrong passwords (FR-014, SC-006)

**Decision**: when the email address matches no usable account (none exists, it has no
password yet, or it is deactivated), the handler still runs a full PBKDF2 verification.
It uses a dummy hash computed once at startup with the configured iteration count, then
returns the same `401 identity.invalid_credentials` body. Throttling (R-07) treats both
cases identically, and so does the sequence of database calls: one user lookup, one
throttle upsert.

**Verification**: SC-006's test sends 1,000 attempts split between unknown addresses
and wrong passwords. It asserts the bodies are byte-identical apart from `traceId`, and
that the medians differ by less than 10%.

---

## R-09: The setup code (FR-004, FR-004a)

**Decision**:
- **Format**: 12 characters from Crockford base32 (no I, L, O or U), shown as
  `XXXX-XXXX-XXXX`, which is 60 bits of entropy. Input is matched case-insensitively,
  with dashes and spaces ignored. Combined with the client throttle (5 failures per 15
  minutes per client), guessing is infeasible.
- **Storage**: a single row in `identity.installation`, created at startup with
  `INSERT … ON CONFLICT DO NOTHING` when the installation is unclaimed. Every instance
  therefore reads and prints the **same** code (spec edge case "Restart, or several
  instances"). The column is cleared in the same transaction that claims the
  installation.
- **Printing**: once at every startup while unclaimed, as a single Warning-level log
  event with a fixed message template and a dedicated `SetupCode` property. The console
  sink renders it as a boxed banner. In that event, and nowhere else, the code is in
  plain text. The recovery command's `identity setup-code` subcommand (R-16) prints it
  on demand.
- **Stored in plain text on purpose**: the code has to be re-printable at every restart,
  which rules out storing only a hash. Anyone who can read the `identity` schema already
  controls the installation, and the code is useless once setup completes.
- **The 001 FR-032 exception (FR-004a)**: the constitution forbids PII and content
  bodies in logs, not secrets as such. 001's broader "no secrets in logs" rule gains
  exactly this one exception, enforced by a test on both sides. The log check that
  guards against leaks (SC-009) tolerates the `SetupCode` property **only** on that one
  event template, and a second test asserts the event is not emitted once the
  installation is claimed.

---

## R-10: Set-password links (FR-011, FR-011a, FR-011b, FR-028)

**Decision**:
- **Token**: 32 random bytes, base64url-encoded. The database stores only the SHA-256
  hash (`token_hash`), unique.
- **One live link per account**: a partial unique index on `user_id` where
  `used_at_utc IS NULL AND invalidated_at_utc IS NULL`. Issuing a new link first
  invalidates the old one in the same transaction.
- **Link shape**: `<admin base>/set-password#token=<token>`. The token sits in the URL
  **fragment**, which browsers never send to servers. It therefore never reaches access
  logs, proxy logs or `Referer` headers.
- **Who builds the URL**: the API returns the token and its expiry, never a full URL.
  The admin shell builds the link from its own `location.origin` plus base path, which is
  the address the administrator is actually using. The server never derives a link from
  the request's `Host` header, which satisfies FR-011b and rules out Host-header
  poisoning.
- **Recovery command (no browser)**: the CLI prints the full URL when the optional
  `Hosting:PublicUrl` is configured. Otherwise it prints the path and the token and tells
  the operator to prepend their admin address.
- **Using a link**: consumes the link, sets the password, revokes all of that user's
  sessions (FR-011a) and records the event, all in one transaction. A link whose account
  has since been deactivated, or whose email address has changed (FR-010a), is refused
  with the same `410 identity.link_invalid` as an expired one.
- **Expiry**: `Identity:SetPasswordLinks:Lifetime`, 24 hours by default.

---

## R-11: Cross-site request forgery (FR-018, US2 scenario 8)

**Decision**: three layers, none of which needs a token round-trip.
1. The `qb_session` cookie is `SameSite=Strict`, so browsers don't attach it to
   cross-site requests.
2. Every state-changing admin API request (anything other than GET, HEAD or OPTIONS)
   must carry the header `X-QuickBuilder-Request: 1`. Browsers can't add a custom header
   to a cross-origin request without a CORS preflight, and the application enables no
   CORS policy. Requests without the header get `403 identity.request_rejected`.
3. When an `Origin` header is present, it must match the request's own scheme, host and
   base path. `Sec-Fetch-Site`, when present, must be `same-origin`.

The admin shell's single `request()` helper (`src/admin/src/api/`) adds the header, so no
screen can forget it.

**Alternatives considered**: *ASP.NET Core antiforgery tokens*: effective, but they add a
token-fetch round-trip and per-form state to a single-page app that is already
same-origin, and they protect nothing that the three layers above don't.

---

## R-12: Setup mode, and claiming the installation exactly once (FR-001, FR-003, FR-006)

**Decision**: `identity.installation` is a single-row table (`id = 1`, enforced by a
check constraint) with `claimed_at_utc` and `setup_code`. Setup runs in one transaction:
`UPDATE installation SET claimed_at_utc = now(), setup_code = NULL WHERE id = 1 AND
claimed_at_utc IS NULL AND setup_code = @code`. It checks that exactly one row was
affected, then inserts the user, the administrator grant, the session and the security
event. A concurrent second submission affects zero rows and receives
`409 identity.setup_already_completed` (US1 scenario 4).

**The setup-mode gate**: middleware from the Identity module, added to the host
pipeline through its public `UseIdentityModule()` extension (composition only). While
unclaimed, it answers every `/api` request not on the setup allow-list with
`428 identity.setup_required`. Health, static assets and the public surface are
untouched (FR-006). The claimed flag is cached in memory **once it is true**. That is
safe on every instance because the transition only ever goes one way; an unclaimed
instance keeps checking the database until it sees the claim.

**The upgrade from 001 (FR-006, SC-008)**: 001 databases have no `identity` schema. The
Identity migration creates it, and startup inserts the unclaimed installation row. 001
sites are untouched because they live in another schema, and public resolution never
passes through the setup gate. A snapshot-upgrade test proves it (constitution III,
R-18).

---

## R-13: Never leaving the installation without an administrator (FR-013)

**Decision**: every operation that can reduce the number of active administrators takes
a PostgreSQL transaction-scoped advisory lock (`pg_advisory_xact_lock`) keyed
`identity.administrators` before re-counting. Those operations are deactivation,
removing the administrator grant, and replacing a user's grants. If the change would
leave zero active administrators, it returns `409 identity.last_administrator`. The lock
serialises only these rare operations, and it is released automatically when the
transaction ends, even if the process crashes (the same property 001's D-04 relies on).

**Alternatives considered**: *SERIALIZABLE isolation*: correct, but it moves the
retry-on-serialisation-failure burden onto every caller for no benefit here.

---

## R-14: Security events and retention (FR-029, FR-029a, SC-012)

**Decision**: `identity.security_events` is append-only. The module exposes no update
or delete path apart from the retention sweep. This is enforced two ways:

- `SecurityEvent` has no public setters.
- A `SaveChanges` interceptor on `IdentityDbContext` throws if a `SecurityEvent` entry
  is ever `Modified` or `Deleted`, and a test proves it does.

An architecture test can't do this job: NetArchTest sees types, not call sites (001
D-06). The retention sweep's set-based `ExecuteDelete` is the single sanctioned deletion
path, and a test covers it (SC-012). Each row
carries the time, event type, outcome, acting user, affected user, client address and a
small `jsonb` `details` object, used for example for the old and new email address
(FR-010a) or the site and role of a grant change.

The **retention sweep** is an hourly `BackgroundService` modelled on 001's
`OutboxRetentionService`. It deletes events older than
`Identity:SecurityEvents:RetentionPeriod` (365 days by default) in batches of 10,000, and
also removes stale throttle rows (R-07). A `DELETE … WHERE occurred_at_utc < cutoff` is
idempotent and safe to run on several instances at once. The sweep is driven through a
`TimeProvider`, so SC-012's test can move the clock forward.

**Personal data**: events hold email addresses and client addresses, which is personal
data, in the database and not in logs (FR-030). The retention period is the operator's
lever for data-protection duties.

---

## R-15: The common-password list (FR-008). This is the one new dependency

**Decision**: ship a list of common and breached passwords as an embedded resource in
`QuickBuilder.Identity`, derived from the **SecLists** project (MIT licence) common-
credentials lists. At build time the list is filtered to entries of **12 or more
characters**, since shorter ones are already rejected by the length rule. It is
lower-cased and de-duplicated, which leaves a file of a few hundred kilobytes. At startup
it is loaded into a case-insensitive `HashSet<string>`. The exact source files and
upstream commit are pinned in a `README` next to the resource, so updates are
deliberate.

The same check also rejects a password equal to the account's own email address or
display name. That is NIST SP 800-63B's "context-specific words" guidance, at the cost
of one line.

| Dependency | Licence | Why | Alternative rejected |
|---|---|---|---|
| SecLists common-credentials list (data file, filtered) | MIT | FR-008 requires an offline breached/common password check | *Have I Been Pwned range API*: needs network access, which violates offline operation (constitution II). *Full HIBP offline corpus*: tens of gigabytes. |

No new NuGet or npm packages are added.

---

## R-16: Operator commands (FR-028, FR-004)

**Decision**: generalise 001's `migrate` argument into a small command mechanism.
`Shared.Infrastructure` gains `IHostCommand` (`Name`, `RunAsync(args, services)`).
Modules register implementations, and the host runs the command whose name matches the
first argument. The host still contains no command logic.

Identity registers:

```text
dotnet QuickBuilder.Host.dll identity setup-code
dotnet QuickBuilder.Host.dll identity recovery-link --email <address>
```

Inside the reference deployment these run as
`docker compose exec app dotnet QuickBuilder.Host.dll identity …`, which needs
command-line access to the host and nothing on the network (FR-028). `recovery-link`
accepts only an **existing, active administrator**. It issues a set-password link under
R-10, with `issued_by_user_id = NULL` meaning "operator", and records
`operator_recovery_used`. It refuses to run until migrations have been applied, with the
same message as readiness's `MigrationsPending`.

**Alternatives considered**: *a separate CLI executable*: a second artefact to build,
ship and version, for two subcommands.

---

## R-17: The admin shell

**Decision**:
- **Auth store**: a Pinia store bootstraps from `GET /api/v1/identity/status` and
  `GET /api/v1/identity/me`. A router guard sends unclaimed installations to `/setup` and
  signed-out users to `/sign-in?returnTo=…`.
- **Return target**: `returnTo` is honoured only if `router.resolve()` maps it to a known
  admin route. External and protocol-relative targets are dropped (FR-019).
- **Central error handling**: the shared `request()` helper adds the R-11 header and
  handles `401` (clear the store, go to sign-in, keep `returnTo`) and `428` (go to setup)
  in one place, which covers the stale-shell-after-upgrade edge case.
- **New screens**: setup wizard, sign-in, set-password (reads `#token`, checks it first,
  then asks for the password), account (change password, sign out other sessions),
  users (list, create with a copy-link dialog, edit: name, email, grants, deactivate or
  reactivate, new link, sign out everywhere).
- **Existing baselines**: every string goes through `vue-i18n` (the no-raw-text lint
  rule already fails the build otherwise), and each screen gets an axe check in vitest
  (FR-032, SC-010).
- **Hiding controls is courtesy, not enforcement**: navigation hides *Users* from
  non-administrators, but the server enforces access regardless (FR-024).

---

## R-18: Test strategy additions

- **Signed-in test clients**: `QuickBuilderApp` gains helpers that complete setup
  (reading the code through the Identity module's internal service, which is visible to
  tests) and return signed-in `HttpClient`s for an administrator, or for a Designer or
  Editor on given sites. Clients use `https://localhost` as their base address, so the
  HTTPS rule (R-04) is exercised as it is in production, with no special case for test
  servers.
- **Existing 001 tests**: they call `/api/v1/sites` anonymously and will now get 428 or
  401. They move to the signed-in helpers. This is an expected, budgeted change, not a
  regression.
- **Upgrade test (constitution III)**: migrate only the `sites` schema (the 001 state),
  insert sites and host names, start the full application, and assert setup mode, intact
  sites and working public resolution (SC-008).
- **Concurrency**: parallel setup submissions (exactly one account), parallel
  demotion of the last two administrators (at least one survives), and throttling
  counters under parallel failed sign-ins spread across two app instances sharing one
  container database (SC-005).

---

## R-19: Verifying the sign-in performance target (SC-013)

**Decision**: add a smoke-stage test, run against the built image, that uses a compose
override limiting the app container to `cpus: 2` and `mem_limit: 4g` (the reference
environment). It fires 10 concurrent sign-ins, repeated 20 times, and asserts p95 ≤ 1 s.
During the burst it also measures `GET /api/v1/sites` latency and compares it with an
idle baseline (≤ 2× at p95). It lives with the smoke tests because only there is the
CPU limit real. `WebApplicationFactory` shares the test runner's CPUs.

---

## Configuration keys introduced

All keys are documented in `docs/configuration.md`, validated at startup
(`ValidateOnStart`), and overridable by environment variables.

| Key | Default | Validation |
|---|---|---|
| `Identity:Session:IdleTimeout` | `02:00:00` | 5 minutes – 24 hours |
| `Identity:Session:AbsoluteLifetime` | `12:00:00` | ≥ IdleTimeout, ≤ 30 days |
| `Identity:SignIn:ClientFailureThreshold` | `5` | 3 – 100 |
| `Identity:SignIn:ClientBlockDuration` | `00:15:00` | 1 minute – 24 hours |
| `Identity:SignIn:AccountFailureThreshold` | `3` | 1 – 20 |
| `Identity:SignIn:AccountMaxDelay` | `00:01:00` | 1 second – 10 minutes |
| `Identity:SignIn:KnownDeviceFailureThreshold` | `5` | 1 – 20 |
| `Identity:SignIn:KnownDeviceLifetime` | `90.00:00:00` | 1 – 365 days |
| `Identity:Passwords:Pbkdf2Iterations` | `210000` | ≥ 210000 |
| `Identity:SetPasswordLinks:Lifetime` | `1.00:00:00` | 10 minutes – 7 days |
| `Identity:SecurityEvents:RetentionPeriod` | `365.00:00:00` | 1 – 3650 days |
| `Identity:AllowInsecureHttp` | `false` | warns at every startup when `true` |
| `Hosting:PublicUrl` | *(empty)* | absolute `https`/`http` URL or empty; used only by the recovery command |

The minimum password length is fixed at 12 and the maximum at 128. FR-008 states them as
product rules, not as configuration.

---

## Resolved unknowns

Technical Context has no `NEEDS CLARIFICATION` entries. Every design question the spec
left to planning is settled above, and nothing blocks Phase 1.
