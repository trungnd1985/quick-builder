---

description: "Task list for 002-identity-setup"
---

# Tasks: Identity, First-Run Setup and Authorization

**Input**: Design documents from `/specs/002-identity-setup/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included. The spec measures behaviour through automated checks (SC-002 to SC-006, SC-009 to SC-013), FR-025 requires a failing build for undeclared endpoints, and constitution Principle X makes tests mandatory. Within each story, test tasks come first and must fail before implementation starts.

**Organization**: Tasks are grouped by user story (US1–US5 from spec.md) so each story can be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: The user story the task belongs to (US1–US5)
- Every task names an exact file path

## Path Conventions

The layout is the modular monolith from [plan.md](./plan.md#project-structure):
- **Backend**: `src/QuickBuilder.Host/`, `src/Shared/`, and `src/Modules/Identity/`
  (new) alongside `src/Modules/Sites/`.
- **Frontend**: `src/admin/src/`.
- **Tests and docs**: `tests/` and `docs/`.

Shorthand used below:
- `IDENTITY` = `src/Modules/Identity/QuickBuilder.Identity`
- `ITESTS` = `tests/QuickBuilder.IntegrationTests`

Feature folders follow 001's one-folder-per-slice rule. Each `Features/<Name>/<Name>.cs`
holds the controller action, request/response, command or query, validator and handler.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: the branch, the new module's projects, and the bundled data file. Nothing here has behaviour.

- [X] T001 Create and switch to branch `002-identity-setup` at the repository root, then commit `specs/002-identity-setup/` to it as the first commit (plan.md "Development Workflow")
- [X] T002 [P] Create `src/Modules/Identity/QuickBuilder.Identity.Contracts/QuickBuilder.Identity.Contracts.csproj`: a class library with no EF Core, ASP.NET Core or Npgsql references, mirroring `QuickBuilder.Sites.Contracts.csproj`
- [X] T003 [P] Create `src/Modules/Identity/QuickBuilder.Identity/QuickBuilder.Identity.csproj`, mirroring `QuickBuilder.Sites.csproj`. It needs:
  - `FrameworkReference` to `Microsoft.AspNetCore.App` and `<Using Include="Microsoft.AspNetCore.Http" />`;
  - project references to Shared.Kernel, Shared.Infrastructure, Identity.Contracts and Sites.Contracts;
  - the EF Core, Npgsql and EF Design packages;
  - `InternalsVisibleTo` for the three test projects;
  - an `EmbeddedResource` for `Infrastructure/Security/Resources/common-passwords-12plus.txt`.
- [X] T004 Wire the projects into `QuickBuilder.slnx`, the host and the test projects:
  - add both new projects to `QuickBuilder.slnx`;
  - reference `QuickBuilder.Identity` from `src/QuickBuilder.Host/QuickBuilder.Host.csproj`;
  - reference both projects from `tests/QuickBuilder.ArchitectureTests`, `tests/QuickBuilder.UnitTests` and `tests/QuickBuilder.IntegrationTests` (their `.csproj` files).
- [X] T005 [P] Add `IDENTITY/Infrastructure/Security/Resources/common-passwords-12plus.txt` and `IDENTITY/Infrastructure/Security/Resources/README.md`:
  - **The list**: the SecLists common-credentials list (MIT), filtered to entries of ≥ 12 characters, lower-cased and de-duplicated (research R-15).
  - **The README**: records the upstream files, the commit hash, the MIT licence text and the exact filter command used to regenerate the list.
- [X] T006 [P] Add the `Identity` section to `src/QuickBuilder.Host/appsettings.json`, with every default from the research.md "Configuration keys introduced" table, plus `Hosting:PublicUrl: ""`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the kernel declarations, the host command mechanism, the complete `identity` schema, the security primitives, session authentication, authorization enforcement, and the test harness. Every story depends on these.

**⚠️ CRITICAL**: No user-story work can start until this phase is complete. Once T037 lands, 001's anonymous tests stop passing. T039 restores them, so T037 and T039 belong in the same pull-request commit series.

### Shared kernel and shared infrastructure

- [X] T007 [P] Create `src/Shared/QuickBuilder.Shared.Kernel/Authorization/Capabilities.cs` with the `SystemRole` enum (`Administrator`, `Designer`, `Editor`), the `CapabilityScope` enum (`Installation`, `Site`) and `CapabilityDefinition(string Name, CapabilityScope Scope, IReadOnlyList<SystemRole> GrantedTo)`. The constructor rejects `Administrator` in `GrantedTo` and names that don't match `^[a-z]+(\.[a-z_]+)+$` (data-model.md "Shared-kernel additions").
- [X] T008 [P] Create `src/Shared/QuickBuilder.Shared.Kernel/Authorization/Attributes.cs` with three plain attributes, none of them referencing ASP.NET Core:
  - `RequireCapabilityAttribute(string name)`, for methods and classes;
  - `RequireSignedInAttribute`, for methods and classes;
  - `InstallationScopedAttribute(string reason)`, for classes, rejecting an empty reason.
- [X] T009 [P] Create `src/Shared/QuickBuilder.Shared.Kernel/Authorization/IAccessEvaluator.cs` with `IAccessEvaluator` and `SiteVisibility(bool All, IReadOnlySet<Guid> SiteIds)`, in the exact shape given in data-model.md
- [X] T010 [P] Create `src/Shared/QuickBuilder.Shared.Infrastructure/Commands/IHostCommand.cs` (the interface from contracts/operator-commands.md) and `src/Shared/QuickBuilder.Shared.Infrastructure/Commands/HostCommandRunner.cs`:
  - it matches `args[0]` case-insensitively against the registered commands and returns the command's exit code;
  - an unknown name prints the known names and returns 64;
  - `TryRunAsync` returns null when `args` is empty so the web host starts.
- [X] T011 Convert 001's `MigrateCommand` in `src/QuickBuilder.Host/Startup/StartupTasks.cs` into an `IHostCommand` named `migrate`, and replace the `args.Contains("migrate")` special case in `src/QuickBuilder.Host/Program.cs` with `HostCommandRunner.TryRunAsync`. `docker compose run --rm app dotnet QuickBuilder.Host.dll migrate` must behave exactly as before.

### Identity module: options, domain, data

- [X] T012 [P] Create `IDENTITY/IdentityOptions.cs`:
  - option classes `SessionOptions`, `SignInOptions`, `PasswordOptions`, `SetPasswordLinkOptions`, `SecurityEventOptions` and `AllowInsecureHttp`, bound from the `Identity` section;
  - DataAnnotations ranges from the research.md key table, plus an `IValidateOptions` enforcing `AbsoluteLifetime ≥ IdleTimeout` and `Pbkdf2Iterations ≥ 210000`;
  - add a validated `PublicUrl` (absolute http/https URL or empty) to `HostingOptions` in `src/QuickBuilder.Host/Configuration/HostingOptions.cs`, and have Identity bind the same key through its own `PublicUrlOptions` class in `IdentityOptions.cs`.
- [X] T013 [P] Create `IDENTITY/Domain/Installation.cs`, the single-row setup state from data-model.md, with `Claim(now)` throwing if the installation is already claimed
- [X] T014 [P] Create `IDENTITY/Domain/User.cs`:
  - email normalisation (trim only, research R-03) and validation;
  - display-name rules;
  - the state transitions from data-model.md: `SetPasswordHash`, `ChangeEmail`, `Rename`, `Deactivate` and `Reactivate`, each returning `Result`;
  - `CanSignIn` = active and has a password.
- [X] T015 [P] Create `IDENTITY/Domain/RoleGrant.cs`, marked `[InstallationScoped("Grants are installation data managed from the admin surface, which has no resolved site; the site is a reference, not an owner.")]`. It has factory methods `Administrator(userId)` and `ForSite(userId, siteId, role)` that enforce the role/scope rule.
- [X] T016 [P] Create `IDENTITY/Domain/Session.cs` with `IsValid(now, idleTimeout)`, `Touch(now)` (writes only when more than 60 s have passed since the last write) and `Revoke(now, reason)`, using the revocation reasons from data-model.md
- [X] T017 [P] Create `IDENTITY/Domain/SetPasswordLink.cs` with `IsUsable(now)`, `Consume(now)` and `Invalidate(now, reason)`
- [X] T018 [P] Create `IDENTITY/Domain/SecurityEvent.cs`:
  - no public setters and a static factory per event type;
  - `SecurityEventTypes` constants for every row of the data-model.md event catalogue;
  - `details` serialised as `jsonb` and never accepting a password, token, setup code or session id (typed detail records only).
- [X] T019 [P] Create `IDENTITY/Domain/SignInThrottle.cs`, the row shape for `sign_in_throttles` with the composite key `(kind, key)`, and `IDENTITY/Domain/KnownDevice.cs` (the `known_devices` row) with `IsValid(now, lifetime)`, `RecordFailure(threshold)` (revokes itself at the threshold), `RecordSuccess(now)` and `Revoke(now)`, per data-model.md
- [X] T020 Create `IDENTITY/Infrastructure/IdentityDbContext.cs` and `IDENTITY/Infrastructure/IdentityDbContextFactory.cs` (design-time). It must:
  - use schema `identity` with `citext`;
  - map every table, column, `CHECK` constraint, unique index and partial unique index listed in data-model.md;
  - run 001's `UseSnakeCaseColumns()`;
  - register a `SaveChanges` interceptor that throws when a `SecurityEvent` entry is `Modified` or `Deleted` (research R-14).
- [X] T021 Generate the `InitialIdentity` migration into `IDENTITY/Infrastructure/Migrations/`. Before committing, check the generated SQL contains every `CHECK` constraint and partial index from data-model.md, including `installation.id = 1` and the claimed ⇔ code invariant.
- [X] T022 Create `IDENTITY/IdentityModule.cs`:
  - `AddIdentityModule(IServiceCollection, IConfiguration)`: registers the DbContext with its history table in the `identity` schema, an `IdentityDatabaseProvider : IModuleDatabaseProvider`, the options from T012, and `CapabilityDefinition("identity.users.manage", Installation, [])`;
  - `UseIdentityModule(IApplicationBuilder)`: an empty stub for now;
  - call both from `src/QuickBuilder.Host/Program.cs`, and add `.AddApplicationPart(typeof(IdentityModule).Assembly)`.

### Security primitives

- [X] T023 [P] Create `IDENTITY/Infrastructure/Security/Pbkdf2PasswordHasher.cs` implementing research R-03:
  - the `$pbkdf2-sha512$i=…$salt$hash` format and a 128-code-point cap, with no Unicode normalisation (research R-03);
  - `Verify` via `CryptographicOperations.FixedTimeEquals`, plus `NeedsRehash`;
  - `VerifyAgainstDummy()` for unknown accounts (R-08);
  - a one-off timing check at startup that logs a Warning above 400 ms.
- [X] T024 [P] Create `IDENTITY/Infrastructure/Security/PasswordPolicy.cs`. It checks length 12–128 code points (counted as typed), rejects entries in the embedded common-password list (loaded once into a case-insensitive `HashSet`), and rejects a password equal to the email or display name. Each rule returns a `ValidationFailure` naming the `password` field.
- [X] T025 [P] Create `IDENTITY/Infrastructure/Security/Tokens.cs` with:
  - `NewSessionToken()` and `NewLinkToken()` (32 random bytes, base64url);
  - `Hash(token)` (SHA-256);
  - `NewSetupCode()` (12 Crockford base32 characters);
  - `NormaliseSetupCode(input)` (upper-case, strip dashes and spaces, map O→0, I/L→1);
  - `FormatSetupCode` (`XXXX-XXXX-XXXX`).
- [X] T026 [P] Create `IDENTITY/Infrastructure/Security/TransportSecurity.cs`:
  - `ClientKey(IPAddress)`: IPv4 exact, IPv6 as its /64;
  - `IsLoopback(HttpContext)`;
  - `RequireSecureTransport(HttpContext)`: refuses plain HTTP from non-loopback clients unless `AllowInsecureHttp` is set, returning `Error("identity.https_required")`;
  - a startup Warning while `AllowInsecureHttp=true` (research R-04).
- [X] T027 [P] Create `IDENTITY/Infrastructure/Security/SecurityEventRecorder.cs`. It adds a `SecurityEvent` to the caller's `IdentityDbContext`, so the event commits in the caller's transaction, taking the client address from the current `HttpContext` (null outside a request).
- [X] T028 Create `IDENTITY/Infrastructure/Security/SignInThrottleService.cs` implementing research R-07:
  - `CheckClientAsync`, `CheckAccountAsync(email)`, `RecordFailureAsync(client, email?)` and `RecordSuccessAsync(email)`;
  - one parameterised `INSERT … ON CONFLICT (kind, key) DO UPDATE … RETURNING` through `Database.SqlQuery`, with a code comment noting that no table here is site-owned (plan Technology Constraints);
  - the account key is the SHA-256 of the normalised email whether or not the account exists;
  - a failure on reaching the client threshold records `client_blocked`;
  - results carry a retry-after value for the `Retry-After` header.

  Also create `IDENTITY/Infrastructure/Security/KnownDeviceService.cs` (research R-07,
  FR-015a):
  - `FindValidAsync(httpContext, userId)` reads the `qb_device` cookie (up to 5 tokens)
    and matches token hashes against `known_devices`;
  - `RememberAsync(httpContext, userId)` issues or refreshes a token and rewrites the
    cookie (`HttpOnly`, `SameSite=Strict`, `Path` = base path, `Secure` when HTTPS,
    `Max-Age` = `KnownDeviceLifetime`), keeping the 5 most recently used tokens;
  - `RecordFailureAsync(device)`, which records `known_device_revoked` at the threshold;
  - `RevokeAllAsync(userId, reason)`, which records `known_device_revoked`.

### Sessions and authentication

- [X] T029 Create `IDENTITY/Infrastructure/Authentication/SessionService.cs`, implementing research R-04:
  - `IssueAsync(user, httpContext)` creates the row and appends the `qb_session` cookie: `HttpOnly`, `SameSite=Strict`, `Path` = base path, `Secure` when HTTPS;
  - `ValidateAsync(token)` does one indexed lookup joined to `users` and checks revocation, idle and absolute expiry, and the user's status;
  - `RevokeAsync(sessionId, reason)`, `RevokeAllAsync(userId, reason, exceptSessionId?)` and `ClearCookie`.
- [X] T030 Create `IDENTITY/Infrastructure/Authentication/SessionAuthenticationHandler.cs`, an `AuthenticationHandler` for scheme `Session`. It reads the cookie, calls `SessionService.ValidateAsync`, and builds a principal with the `sub` (user id) and `sid` (session id) claims. It makes no DB write except the throttled `Touch`, and no grants go into claims. Register the scheme in `IdentityModule.AddIdentityModule`.

### Authorization enforcement (research R-05)

- [X] T031 [P] Create `IDENTITY/Infrastructure/Authorization/CapabilityCatalog.cs`, a singleton built from every registered `CapabilityDefinition`. Duplicate names throw at startup with both registrations named. `Find(name)` returns the definition.
- [X] T032 [P] Create `IDENTITY/Infrastructure/Authorization/AccessEvaluator.cs`, the scoped `IAccessEvaluator`:
  - loads the current user's grants once per request (memoised);
  - Administrator ⇒ every capability and `SiteVisibility(All: true)`;
  - site roles ⇒ capabilities whose `GrantedTo` contains the role at that site;
  - `GetVisibleSitesAsync` returns the granted site ids.
  Register it in `IdentityModule`.
- [X] T033 Create `IDENTITY/Infrastructure/Authorization/CapabilityConvention.cs`, `CapabilityPolicyProvider.cs` and `CapabilityRequirementHandler.cs`:
  - the convention adds `AuthorizeAttribute("capability:<name>")` or `AuthorizeAttribute("signed-in")` to each selector's `EndpointMetadata` wherever the kernel attributes appear;
  - the provider builds `capability:*` policies;
  - the handler reads the `siteId` route value for site-scoped capabilities and records whether the failure was "cannot see the site" or "lacks the capability", for T034.
- [X] T034 Create `IDENTITY/Infrastructure/Authorization/AuthorizationResultHandler.cs`, an `IAuthorizationMiddlewareResultHandler` producing:
  - 401 `identity.unauthenticated`;
  - 403 `identity.forbidden`;
  - 404 with the exact problem document that `src/Modules/Sites/QuickBuilder.Sites/ErrorResults.cs` emits for `sites.not_found` (same `type`, `title`, `detail`, `errorCode`), per contracts/authorization.md §3.
- [X] T035 Register authorization in `IdentityModule.AddIdentityModule`:
  - `AddAuthorization` with `FallbackPolicy` = authenticated user and the `signed-in` policy;
  - the policy provider, requirement handler and result handler;
  - `MvcOptions.Conventions.Add(new CapabilityConvention())`.

### Host wiring

- [X] T036 [P] Add `[AllowAnonymous]` to the public and health endpoints in `src/QuickBuilder.Host/Health/HealthController.cs` and `ResolveCurrentSite.cs`:
  - `HealthController` in `src/QuickBuilder.Host/Health/HealthController.cs`;
  - the public-surface controller in `src/Modules/Sites/QuickBuilder.Sites/Features/ResolveCurrentSite/ResolveCurrentSite.cs`.
- [X] T037 Update the pipeline in `src/QuickBuilder.Host/Program.cs`:
  - `UseAuthentication()` and `UseAuthorization()` after `SiteResolutionMiddleware`, with `UseIdentityModule()` between them;
  - `.AllowAnonymous()` on the `MapFallbackToFile` admin SPA fallback;
  - static files stay before authentication;
  - extend the pipeline-order comment accordingly.

### Test harness and 001 test migration

- [X] T038 Create `ITESTS/Infrastructure/IdentitySeed.cs` and extend `ITESTS/Infrastructure/QuickBuilderApp.cs`:
  - clients use `ClientOptions.BaseAddress = https://localhost` (research R-18);
  - `ClaimInstallationAsync()` marks the installation claimed directly in the DB;
  - `SeedUserAsync(email, grants, password?)` adds a user;
  - `CreateSignedInClientAsync(user)` issues a session through `SessionService` and returns an `HttpClient` carrying the cookie and `X-QuickBuilder-Request: 1`;
  - `AdminClientAsync()` is a convenience wrapper.
- [X] T039 Move 001's integration tests to signed-in clients so they pass again: `ITESTS/Sites/SiteLifecycleTests.cs`, `ITESTS/Sites/SiteScopingTests.cs`, `ITESTS/Modules/FeatureSliceTests.cs`, `ITESTS/Modules/SecondModuleTests.cs` (plus `WidgetsModule.cs` actions gain declarations), `ITESTS/Startup/StartupTests.cs`, `ITESTS/Observability/LoggingTests.cs` and `ITESTS/Observability/NoEgressTests.cs`. Assertions stay the same; only the client and setup change.

### Foundational tests

- [X] T040 [P] Unit tests in `tests/QuickBuilder.UnitTests/Identity/Pbkdf2PasswordHasherTests.cs`:
  - round-trip and wrong-password rejection;
  - the password is hashed exactly as typed: a differently composed form of the same text does not verify (documents the research R-03 decision);
  - a 128-code-point password verifies and a 129-code-point one is rejected, not truncated;
  - the format parses and `NeedsRehash` fires below the configured iterations;
  - the dummy verification returns false.
- [X] T041 [P] Unit tests in `tests/QuickBuilder.UnitTests/Identity/PasswordPolicyTests.cs`:
  - 11 code points are rejected and 12 accepted;
  - list entries are rejected case-insensitively;
  - a password equal to the email or name is rejected;
  - no composition rule is applied.
- [X] T042 [P] Unit tests in `tests/QuickBuilder.UnitTests/Identity/TokensAndTransportTests.cs`:
  - setup codes are 12 characters with no I/L/O/U;
  - normalisation accepts lower case, dashes and spaces;
  - token hashes are 32 bytes;
  - `ClientKey` maps two addresses in one IPv6 /64 to the same key and different /64s apart.
- [X] T043 [P] Unit tests in `tests/QuickBuilder.UnitTests/Identity/DomainTests.cs` covering the `User`, `Session`, `SetPasswordLink`, `RoleGrant` and `Installation` transitions and invariants from data-model.md
- [X] T044 Extend `tests/QuickBuilder.ArchitectureTests/ModuleBoundaryTests.cs` to include the Identity assemblies:
  - contracts don't depend on infrastructure;
  - module internals aren't public (Identity adds `Module` and `Controller` public types only);
  - no module references another's implementation, which is no longer vacuous with two modules;
  - shared code references neither module;
  - `Entities_with_a_site_id_are_site_owned` scans both modules and accepts `[InstallationScoped]` only with a non-empty reason.
  Add a violating fixture for the new exemption rule in `tests/QuickBuilder.ArchitectureTests/Fixtures/ViolatingFixtures.cs`, and assert it fires in `RulesFireWhenBrokenTests.cs`.
- [X] T045 Add `ITESTS/Identity/SecurityEventAppendOnlyTests.cs`: modifying or deleting a tracked `SecurityEvent` through `IdentityDbContext` throws (research R-14)

**Checkpoint**: the build, the architecture tests and all of 001's tests are green again on signed-in clients, and the identity schema migrates. User-story work can begin.

---

## Phase 3: User Story 1 - Operator claims a fresh installation through the setup wizard (Priority: P1) 🎯 MVP

**Goal**: on a fresh installation, the admin address shows a setup wizard. The setup code from the console output plus name, email and password creates the first Administrator and signs them in. After that the wizard is gone for good, and 001 installations upgrade into setup mode without losing sites.

**Independent Test**: start a fresh installation, open `/admin/`, complete the wizard with the logged code, and confirm you're signed in as Administrator. Then confirm `POST /api/v1/identity/setup` returns 409 and no other account exists (quickstart.md §1).

### Tests for User Story 1 ⚠️ write first, confirm they fail

- [ ] T046 [P] [US1] Create `ITESTS/Identity/SetupTests.cs` covering US1 scenarios 2–5, FR-005 and SC-011:
  - a freshly migrated installation has zero rows in `identity.users` and `identity.role_grants`, so no account or credential is seeded (FR-005);
  - a correct code signs the caller in (session cookie and `Me` with `isAdministrator`) and records `setup_completed`;
  - after that, setup returns 409 `identity.setup_already_completed` with the user count still 1;
  - missing or wrong codes get 403 `identity.setup_code_invalid` with no account, `setup_code_rejected` recorded, and a 429 after the client threshold;
  - 10 parallel submissions produce exactly one account;
  - two app instances on one DB read the same code;
  - plain HTTP from a non-loopback address gets 403 `identity.https_required`.
- [ ] T047 [P] [US1] Create `ITESTS/Identity/SetupGateTests.cs` (US1 scenario 1, FR-001, FR-006):
  - while unclaimed, every `/api` endpoint except `GET /status` and `POST /setup` returns 428 `identity.setup_required`;
  - `/health/live`, `/health/ready`, the public surface `GET /` with a site host, and `/admin/` return their normal responses;
  - `GET /api/v1/identity/status` reports `setupRequired` correctly before and after the claim.
- [ ] T048 [P] [US1] Create `ITESTS/Identity/UpgradeFrom001Tests.cs` (US1 scenario 6, SC-008, constitution III): apply only the `sites` migrations, seed two sites with host names, start the full app, then assert setup mode, both sites intact after setup, and public resolution working throughout
- [ ] T049 [P] [US1] Create `ITESTS/Observability/SetupCodeLoggingTests.cs` (FR-004a, research R-09):
  - while unclaimed, the code appears in captured logs exactly once per startup, only on the setup-banner template's `SetupCode` property;
  - after the claim, a restart emits no setup-code event;
  - the code never appears in any other log event.

### Implementation for User Story 1

- [X] T050 [P] [US1] Create `src/Modules/Identity/QuickBuilder.Identity.Contracts/IIdentitySetupStatus.cs` with `Task<bool> IsClaimedAsync(CancellationToken)`
- [ ] T051 [US1] Create `IDENTITY/Infrastructure/SetupStatus.cs`, implementing `IIdentitySetupStatus`. It caches `true` in a static-lifetime singleton once it has been observed, since the flag is one-way (research R-12), and queries the DB otherwise. Register it in `IdentityModule`.
- [ ] T052 [US1] Create `IDENTITY/Infrastructure/InstallationBootstrapper.cs`, a hosted service that runs once `StartupState` is ready:
  - `INSERT … ON CONFLICT DO NOTHING` the installation row with a new code if missing;
  - if unclaimed, log the boxed Warning banner, with a fixed template and the `SetupCode` property, plus the advice to finish setup before enabling log forwarding (plan Risks);
  - if migrations haven't run, log that and do nothing.
  Register it in `IdentityModule`.
- [ ] T053 [US1] Create `IDENTITY/Infrastructure/Setup/SetupGateMiddleware.cs`. For `/api` paths while `IIdentitySetupStatus` is false, it returns 428 `identity.setup_required` problem details except for `GET /api/v1/identity/status` and `POST /api/v1/identity/setup`. It never touches `/health`, `/admin` assets or the public surface. T057 registers it.
- [ ] T054 [P] [US1] Create `IDENTITY/Features/GetStatus/GetStatus.cs`: `GET /api/v1/identity/status`, `[AllowAnonymous]`, returning `{ setupRequired }`
- [ ] T055 [US1] Create `IDENTITY/Features/CompleteSetup/CompleteSetup.cs`: `POST /api/v1/identity/setup`, `[AllowAnonymous]`.
  - **Validator**: display name, email and `PasswordPolicy`.
  - **Handler, in order**: `RequireSecureTransport`, then the client throttle, then one transaction containing the conditional `UPDATE installation … WHERE claimed_at_utc IS NULL AND setup_code = @code`. That transaction then inserts the user with its hash, the Administrator grant and the `setup_completed` event.
  - **Code mismatch**: 403 plus `setup_code_rejected` plus a throttle failure.
  - **Already claimed**: 409.
  - **Success**: issue a session and return 201 with `Me`.
- [ ] T056 [US1] Create `IDENTITY/Features/GetMe/GetMe.cs`: `GET /api/v1/identity/me`, `[RequireSignedIn]`, returning `Me` (id, displayName, email, isAdministrator, siteRoles) through an `AsNoTracking` projection
- [ ] T057 [US1] Replace the `UseIdentityModule` stub in `IDENTITY/IdentityModule.cs` so it registers `SetupGateMiddleware` first. The host call already exists (T037), and later middleware (T072) goes after the gate.
- [ ] T058 [P] [US1] Create `src/admin/src/api/request.ts` by moving `request()`, `ApiError` and `ProblemDetails` out of `src/admin/src/api/sites.ts`:
  - add the header `X-QuickBuilder-Request: 1` to every non-GET request (research R-11);
  - add `credentials: 'same-origin'`;
  - add hooks `onUnauthenticated` and `onSetupRequired`, invoked on 401 and 428.
  `src/admin/src/api/sites.ts` then imports from it.
- [ ] T059 [P] [US1] Create `src/admin/src/api/identity.ts`, a typed client for `status`, `setup` and `me` with the `Me` and `SiteRole` types from contracts/identity-api.yaml
- [ ] T060 [US1] Create `src/admin/src/app/authStore.ts`, a Pinia store with `bootstrap()` (status, then me if claimed), `setupRequired`, `user` and `isAdministrator`. It wires the `onSetupRequired` hook to route to `/setup`.
- [ ] T061 [US1] Update `src/admin/src/app/router.ts`:
  - add the `/setup` route;
  - add a `beforeEach` guard awaiting `authStore.bootstrap()` once, then sending everyone to `/setup` while setup is required and away from `/setup` otherwise.
- [ ] T062 [US1] Create `src/admin/src/features/identity/SetupView.vue`:
  - fields: setup code (hint: "shown in the application's console output"), display name, email, password;
  - per-field errors from `ApiError.fieldErrors`;
  - in-progress and failed states;
  - on success, store the user and go to `/sites`.
- [ ] T063 [P] [US1] Add `identity.setup.*` strings (labels, hints, errors for each `errorCode`) to `src/admin/src/i18n/en.json`
- [ ] T064 [P] [US1] Create `src/admin/tests/setupView.spec.ts` checking:
  - an axe pass;
  - the form is completable by keyboard alone and every control has a label;
  - field errors render;
  - the 409 path redirects.
- [ ] T065 [US1] Extend `tests/QuickBuilder.SmokeTests/ReferenceDeploymentSmokeTests.cs`:
  - after readiness, parse the setup code from `docker compose logs app`;
  - `POST /api/v1/identity/setup`;
  - keep the cookie container and send `X-QuickBuilder-Request: 1` for the existing create, list and resolve steps (FR-034, E-3);
  - update the remarks to state that the smoke journey now begins with the setup wizard;
  - log the elapsed time from `docker compose up` to a signed-in administrator through `ITestOutputHelper`, as evidence for SC-001. It is recorded, not asserted, because CI runner speed varies.

**Checkpoint**: a fresh install can be claimed exactly once, and a 001 install upgrades into setup mode. This is the MVP.

---

## Phase 4: User Story 2 - Users sign in and out, and the admin surface refuses everyone else (Priority: P1)

**Goal**: sign in with email and password, and sign out. Throttling blocks guessing clients without ever locking out the account owner. Every admin operation declares its authorization and refuses anonymous callers, which closes 001's exception E-1.

**Independent Test**: call every admin endpoint anonymously and get 401 on each. Sign in, see them succeed as Administrator, sign out, and get 401 again (quickstart.md §2 and §5).

### Tests for User Story 2 ⚠️ write first, confirm they fail

- [ ] T066 [P] [US2] Create `ITESTS/Identity/SignInTests.cs`:
  - success returns `Me` and sets `qb_session` with `HttpOnly` and `SameSite=Strict`, plus `Secure` over https;
  - an unknown email, a wrong password, an account without a password and a deactivated account all return an identical 401 `identity.invalid_credentials`;
  - sign-out gives 204, and replaying the old cookie gives 401;
  - idle expiry and absolute expiry are enforced (advance a fake `TimeProvider`);
  - emails match case-insensitively and are trimmed, while passwords are not trimmed;
  - a stored lower-iteration hash is upgraded on sign-in (FR-009).
- [ ] T067 [P] [US2] Create `ITESTS/Identity/ThrottlingTests.cs` (SC-005, FR-015/015a):
  - 5 failures from one client, then 429 with `Retry-After` even with the correct password, and `client_blocked` recorded;
  - the account wait doubles from the 3rd failure, is capped at 60 s, and isn't evaluated inside the wait;
  - unknown emails produce the same throttle responses as real ones;
  - two instances share the counters;
  - while another client fails on the account at every wait expiry, the owner carrying a valid `qb_device` cookie succeeds on the **first** attempt, and the owner on a new browser succeeds within 60 s by honouring `Retry-After` (SC-005);
  - a device cookie never bypasses the **client** block;
  - after 5 failures from one device it loses known status and `known_device_revoked` is recorded;
  - a device cookie for account A gives no exemption on account B;
  - deactivation, email change, set-password link use and administrator "sign out everywhere" each revoke the user's known devices.
- [ ] T068 [P] [US2] Create `ITESTS/Identity/CredentialOracleTests.cs` (SC-006, research R-08): 1,000 attempts split between unknown accounts and wrong passwords give bodies identical apart from `traceId`, and the medians differ by less than 10%
- [ ] T069 [P] [US2] Create `ITESTS/Identity/RequestGuardTests.cs` (FR-018, US2 scenario 8, research R-04/R-11):
  - a non-GET request without `X-QuickBuilder-Request` gets 403 `identity.request_rejected`;
  - so does a foreign `Origin`, and `Sec-Fetch-Site: cross-site`;
  - GET requests are unaffected;
  - plain HTTP from a non-loopback address gets `identity.https_required` on sign-in;
  - a forwarded `https` from a listed proxy is accepted.
- [ ] T070 [P] [US2] Create `ITESTS/Identity/EndpointAuthorizationCoverageTests.cs` (SC-002): after the claim, enumerate `EndpointDataSource`, call every endpoint without a session, and assert 401 for all except the anonymous allow-list in contracts/authorization.md §4
- [ ] T071 [P] [US2] Create `tests/QuickBuilder.ArchitectureTests/AuthorizationDeclarationTests.cs` (FR-025, research R-06), with fixtures in `tests/QuickBuilder.ArchitectureTests/Fixtures/AuthorizationFixtures.cs` that prove each rule fires. It asserts:
  - every action of every controller in every module assembly and in the host carries exactly one of `RequireCapability`, `RequireSignedIn` or `AllowAnonymous`;
  - `AllowAnonymous` appears only on the allow-list;
  - every capability name is registered;
  - every site-scoped capability sits on a route containing `{siteId}`.

### Implementation for User Story 2

- [ ] T072 [US2] Create `IDENTITY/Infrastructure/Security/RequestGuardMiddleware.cs` for `/api` non-safe methods: require `X-QuickBuilder-Request: 1`, check `Origin` against the request scheme and host, and check `Sec-Fetch-Site`. Register it in `UseIdentityModule` after the setup gate.
- [ ] T073 [US2] Create `IDENTITY/Features/SignIn/SignIn.cs`: `POST /api/v1/identity/sign-in`, `[AllowAnonymous]`.
  - **Order**: `RequireSecureTransport`, then the client block, then the user lookup, then `KnownDeviceService.FindValidAsync`, then the account wait (skipped when a valid known device matches), then `Verify` (or `VerifyAgainstDummy` when the account is missing or unusable).
  - **Failure**: record the throttle failure, and the device failure when a device matched. Record the `sign_in` failed event without the attempted email, with `subject_user_id` set only when the account exists (FR-029). Return 401.
  - **Success**: reset the account throttle, rehash if needed, set `last_sign_in_at_utc`, issue a session, call `KnownDeviceService.RememberAsync`, and record the `sign_in` succeeded event (`knownDevice` set when the exemption applied).
- [ ] T074 [P] [US2] Create `IDENTITY/Features/SignOut/SignOut.cs`: `POST /api/v1/identity/sign-out`, `[RequireSignedIn]`. It revokes the current session (`sign_out`), clears the cookie and records the event.
- [ ] T075 [US2] Declare authorization on every action under `src/Modules/Sites/QuickBuilder.Sites/Features/`, per contracts/authorization.md §5:
  - `[RequireCapability("sites.create")]` in `src/Modules/Sites/QuickBuilder.Sites/Features/CreateSite/CreateSite.cs`;
  - `[RequireCapability("sites.view")]` in `.../GetSite/GetSite.cs`;
  - `[RequireCapability("sites.host_names.manage")]` in `.../AddHostName/AddHostName.cs`;
  - `[RequireSignedIn]` in `.../ListSites/ListSites.cs`;
  - register the three `CapabilityDefinition`s from data-model.md in `src/Modules/Sites/QuickBuilder.Sites/SitesModule.cs`.
- [ ] T076 [US2] Check that every Identity controller from Phase 3 (`IDENTITY/Features/GetStatus/GetStatus.cs`, `IDENTITY/Features/CompleteSetup/CompleteSetup.cs`, `IDENTITY/Features/GetMe/GetMe.cs`) carries its declaration (`GetStatus` and `CompleteSetup` → `[AllowAnonymous]`, `GetMe` → `[RequireSignedIn]`), so T071 passes
- [ ] T077 [P] [US2] Extend `src/admin/src/api/identity.ts` with `signIn` and `signOut`
- [ ] T078 [US2] Create `src/admin/src/features/identity/SignInView.vue`:
  - email and password fields;
  - the generic error;
  - a throttled message showing the `Retry-After` wait;
  - on success, return to `returnTo` only if `router.resolve(returnTo)` matches a known admin route, and `/sites` otherwise (FR-019).
- [ ] T079 [US2] Wire sign-in and sign-out into the shell (`src/admin/src/app/router.ts`, `src/admin/src/app/authStore.ts`, `src/admin/src/App.vue`):
  - `src/admin/src/app/router.ts`: add the `/sign-in` route and a guard sending signed-out users there with `returnTo`;
  - `src/admin/src/app/authStore.ts`: `onUnauthenticated` clears the store and redirects preserving `returnTo`;
  - `src/admin/src/App.vue`: a signed-in user menu with "Sign out".
- [ ] T080 [P] [US2] Add `identity.signIn.*` and `app.userMenu.*` strings to `src/admin/src/i18n/en.json`, and create `src/admin/tests/signInView.spec.ts` (axe, keyboard-only, `returnTo` rejects `//evil.example` and absolute URLs)
- [ ] T081 [US2] Extend `tests/QuickBuilder.SmokeTests/ReferenceDeploymentSmokeTests.cs`: an anonymous `GET /api/v1/sites` gets 401, then sign out, then sign in with the setup credentials, then continue the journey

**Checkpoint**: E-1 is closed. Every admin endpoint is authenticated and declared, and the build fails otherwise. US1 and US2 together are a shippable, secure installation.

---

## Phase 5: User Story 3 - Administrators manage user accounts (Priority: P2)

**Goal**: administrators create accounts and pass on one-time set-password links. They change names and email addresses, deactivate and reactivate accounts, and sign users out everywhere. Users change their own password and end their other sessions. Every change applies on the next request, and the last administrator is protected.

**Independent Test**: as Administrator, create a user and use the link in a private window to set a password and sign in. Change the user's name, then sign them out everywhere, then deactivate them, checking after each change that their next request reflects it (quickstart.md §4). Grant changes are covered in US4.

### Tests for User Story 3 ⚠️ write first, confirm they fail

- [ ] T082 [P] [US3] Create `ITESTS/Identity/UserManagementTests.cs` (US3 scenarios 1, 4, 5, 6, 9, 10):
  - create returns 201 with a user that has `hasPassword=false`, plus `setPasswordLink`;
  - a duplicate email in any letter case gets 409 `identity.email_taken`;
  - a name change works;
  - an email change revokes sessions, invalidates the live link and records `email_changed` with both addresses;
  - deactivation gets 401 on the user's next request and makes sign-in fail, and reactivation restores it;
  - a non-administrator gets 403 on every user endpoint, including a `PATCH` of their own email;
  - two parallel `PATCH` requests to the same user both succeed and the stored values match one of them in full (last write wins), while parallel `PUT …/grants` requests serialise under the administrator lock (spec edge case).
- [ ] T083 [P] [US3] Create `ITESTS/Identity/SetPasswordLinkTests.cs` (US3 scenarios 2–3, FR-011a, SC-011):
  - `check` then `set-password` gives 204;
  - the new password signs in, the old one doesn't, and every earlier session is revoked;
  - used, expired, replaced, deactivated-account and post-email-change links all return an identical 410 `identity.link_invalid`;
  - issuing a new link invalidates the previous one;
  - the endpoints are client-throttled.
- [ ] T084 [P] [US3] Create `ITESTS/Identity/LastAdministratorTests.cs` (US3 scenario 7, FR-013):
  - with one active Administrator, deactivating them or removing the admin grant gets 409 `identity.last_administrator`;
  - with two, parallel demotion of both leaves exactly one Administrator.
- [ ] T085 [P] [US3] Create `ITESTS/Identity/OwnAccountTests.cs` (US3 scenarios 8, 11, 12, FR-012, FR-012a):
  - changing your own password needs the correct current one (403 `identity.current_password_incorrect` otherwise, and account-throttled);
  - it revokes your other sessions and keeps the current one;
  - `revoke-others` keeps the current session;
  - an administrator's `sessions/revoke` gets 401 on all the user's sessions while the account stays active and can sign in again.
- [ ] T086 [P] [US3] Create `ITESTS/Identity/RevocationTests.cs` (SC-004): two app instances on one database. For sign-out, sign-out-everywhere, deactivation, password change, email change and a grant change, the change goes to instance 1 and the user's next request to instance 2, which reflects it.

### Implementation for User Story 3

- [ ] T087 [P] [US3] Create `IDENTITY/Infrastructure/Security/SetPasswordLinkService.cs`:
  - `IssueAsync(userId, issuedBy?)` invalidates the live link as `replaced`, stores the hash and records the event;
  - `CheckAsync(token)`;
  - `ConsumeAsync(token, newPassword)` applies the policy, sets the hash, consumes the link, revokes all sessions (`password_set`), revokes known devices (`KnownDeviceService.RevokeAllAsync`, reason `password_set`) and records the event, all in one transaction.
- [ ] T088 [P] [US3] Create `IDENTITY/Infrastructure/Security/AdministratorGuard.cs`. `EnsureAnotherActiveAdministratorAsync(excludingUserId)` takes `pg_advisory_xact_lock` via `ExecuteSql` on a key derived from `identity.administrators` inside the caller's transaction, then counts the active Administrator grants (research R-13).
- [ ] T089 [P] [US3] Create `IDENTITY/Features/ListUsers/ListUsers.cs` and `IDENTITY/Features/GetUser/GetUser.cs`, both `[RequireCapability("identity.users.manage")]`, returning the `User` shape through `AsNoTracking` projections
- [ ] T090 [US3] Create `IDENTITY/Features/CreateUser/CreateUser.cs`: `[RequireCapability("identity.users.manage")]`.
  - **Validation**: name, email and the grants shape (at most one entry per site). Each `siteId` must resolve through `ISiteDirectory.FindByIdAsync`, with a field error on `grants.siteRoles[n].siteId` otherwise.
  - **One transaction**: insert the user and grants, issue the first link, and record `account_created`.
  - **Response**: 201 with `{ user, setPasswordLink }`, or 409 on a duplicate email.
- [ ] T091 [US3] Create `IDENTITY/Features/UpdateUser/UpdateUser.cs`: `PATCH …/users/{userId}`, `[RequireCapability("identity.users.manage")]`. It applies name and/or email. An email change also revokes all sessions and known devices (`email_changed`), invalidates the live link and records both addresses (FR-010a). Concurrent edits are last-write-wins, with no concurrency token (spec edge case).
- [ ] T092 [US3] Create `IDENTITY/Features/ReplaceGrants/ReplaceGrants.cs`: `PUT …/users/{userId}/grants`, `[RequireCapability("identity.users.manage")]`.
  - **Validation**: as for `CreateUser`.
  - **Last-administrator guard**: `AdministratorGuard` runs when the Administrator grant is being removed.
  - **Grant replacement**: the full grant set is replaced in one transaction under the administrator lock whether or not the Administrator grant changes, so concurrent replacements serialise, and `grants_changed` is recorded with the before and after grant sets.
- [ ] T093 [US3] Create `IDENTITY/Features/DeactivateUser/DeactivateUser.cs` and `IDENTITY/Features/ReactivateUser/ReactivateUser.cs`, both `[RequireCapability("identity.users.manage")]`. Deactivation runs the guard, revokes sessions and known devices, invalidates the live link and records the event. Reactivation records its event.
- [ ] T094 [US3] Create `IDENTITY/Features/IssueSetPasswordLink/IssueSetPasswordLink.cs` (409 `identity.user_deactivated` for an inactive user) and `IDENTITY/Features/RevokeUserSessions/RevokeUserSessions.cs` (revokes sessions and known devices with `sign_out_everywhere`, and records `sessions_revoked` with `by: administrator`), both `[RequireCapability("identity.users.manage")]`
- [ ] T095 [US3] Create `IDENTITY/Features/CheckSetPasswordLink/CheckSetPasswordLink.cs` and `IDENTITY/Features/SetPassword/SetPassword.cs`, both `[AllowAnonymous]`, with the transport check and the client throttle, delegating to `SetPasswordLinkService`
- [ ] T096 [US3] Create `IDENTITY/Features/ChangeOwnPassword/ChangeOwnPassword.cs` (verify the current password under the account throttle, apply the policy, rehash, revoke other sessions with `password_changed`) and `IDENTITY/Features/RevokeOwnOtherSessions/RevokeOwnOtherSessions.cs`, both `[RequireSignedIn]`
- [ ] T097 [P] [US3] Create `src/Modules/Identity/QuickBuilder.Identity.Contracts/UserSummaryDto.cs` and `IUserDirectory.cs` (`FindManyAsync(ids)` returning display names, for attribution by later modules), and the implementation `IDENTITY/Infrastructure/UserDirectory.cs`, registered in `IdentityModule`
- [ ] T098 [P] [US3] Extend `src/admin/src/api/identity.ts` with users (list, get, create, update, deactivate, reactivate, issue link, revoke sessions), `checkLink`, `setPassword`, `changeOwnPassword` and `revokeOtherSessions`, plus the `User`, `Grants` and `IssuedLink` types
- [ ] T099 [US3] Create `src/admin/src/features/users/UsersView.vue` (a table of name, email, status and role summary, with a create dialog for name and email) and `src/admin/src/features/users/CopyLinkDialog.vue`:
  - the dialog builds `location.origin + import.meta.env.BASE_URL + 'set-password#token=' + token`, shows the expiry, and offers a copy button;
  - it warns that the link is shown once (research R-10).
- [ ] T100 [US3] Create `src/admin/src/features/users/UserEditView.vue`: edit name and email (with a warning that changing the email signs the user out), deactivate/reactivate, "New set-password link" (opens `CopyLinkDialog`) and "Sign out everywhere". Show a 409 `identity.last_administrator` as an inline explanation.
- [ ] T101 [P] [US3] Create `src/admin/src/features/identity/SetPasswordView.vue`. It reads `#token`, calls `checkLink` first, and removes the fragment from the address bar via `history.replaceState`. It then shows the password form with policy feedback, and on success goes to sign-in. For a 410 it shows a single "this link can't be used; ask an administrator for a new one" state.
- [ ] T102 [P] [US3] Create `src/admin/src/features/identity/AccountView.vue` with a change-password form (current and new) and a "Sign out my other sessions" action
- [ ] T103 [US3] Update `src/admin/src/app/router.ts` and `src/admin/src/App.vue`:
  - routes: `/users`, `/users/:id`, `/account` (signed-in) and `/set-password` (anonymous, reachable in any setup state);
  - nav: *Users* is shown only when `authStore.isAdministrator`, which is courtesy only (FR-024);
  - the account link goes in the user menu.
- [ ] T104 [P] [US3] Add `identity.users.*`, `identity.setPassword.*` and `identity.account.*` strings to `src/admin/src/i18n/en.json`
- [ ] T105 [P] [US3] Create `src/admin/tests/usersView.spec.ts`, `src/admin/tests/setPasswordView.spec.ts` and `src/admin/tests/accountView.spec.ts`, each checking an axe pass and keyboard-only completion. `setPasswordView.spec.ts` also checks that the fragment is cleared and the 410 state renders.

**Checkpoint**: multi-user installations work end to end without email. US1 to US3 are each testable on their own.

---

## Phase 6: User Story 4 - Users see and act only on the sites they are permitted on (Priority: P2)

**Goal**: Designer and Editor grants apply per site. A user without a grant for a site gets 404 for it, exactly as if the site didn't exist. A user who has a grant but lacks the specific capability gets 403. Administrators see everything.

**Independent Test**: with sites A and B, grant a user Editor on A only (seeded directly or via US3). As that user, the site list shows only A. `GET /api/v1/sites/<B>` returns a response byte-identical to one for a random id, and `POST …/<A>/host-names` returns 403 (quickstart.md §3).

### Tests for User Story 4 ⚠️ write first, confirm they fail

- [ ] T106 [P] [US4] Create `ITESTS/Identity/SiteVisibilityTests.cs` (SC-003, US4 scenarios 1–6, FR-022/023). Seed sites A and B, and users for Administrator, Editor on A, Designer on A plus Editor on B, and no grants. For every Sites endpoint and every role, assert:
  - the list contents;
  - 404 bodies are byte-identical (apart from `traceId`) to a nonexistent site;
  - 403 appears only where the site is visible;
  - a newly created site is visible only to Administrators.
- [ ] T107 [P] [US4] Create `ITESTS/Identity/GrantValidationTests.cs`:
  - `PUT …/grants` with an unknown `siteId` gets 400 with a field error;
  - two roles for the same site get 400;
  - the Administrator flag plus site roles is accepted, and the site roles are stored but add nothing;
  - a changed grant applies on the user's next request.

### Implementation for User Story 4

- [ ] T108 [US4] Update `src/Modules/Sites/QuickBuilder.Sites/Features/ListSites/ListSites.cs` so the handler takes `IAccessEvaluator` and applies `GetVisibleSitesAsync()`: no filter when `All`, `Where(s => visible.SiteIds.Contains(s.Id))` otherwise. Keep the existing `AcrossAllSites` reason and extend it to explain the per-user narrowing. Update the dedicated bypass test in `ITESTS/Sites/SiteScopingTests.cs` to cover both paths.
- [ ] T109 [US4] Create `src/admin/src/features/users/GrantsEditor.vue`, used in the create dialog of `UsersView.vue` and in `UserEditView.vue`:
  - an Administrator toggle;
  - a per-site role picker (none / designer / editor), with sites from `sitesApi.list()`;
  - saving calls `PUT …/grants` and shows 409 `identity.last_administrator` inline.
- [ ] T110 [US4] Add a "you have no access to any site yet" empty state to `src/admin/src/features/sites/SitesView.vue` for signed-in non-administrators with no site roles (edge case "user whose last site role is removed"), and hide the create-site form from non-administrators (courtesy only)
- [ ] T111 [P] [US4] Add `identity.grants.*` and `sites.noAccess.*` strings to `src/admin/src/i18n/en.json`, create `src/admin/tests/grantsEditor.spec.ts` (axe and keyboard), and extend `src/admin/tests/sitesView.spec.ts` for the no-access state

**Checkpoint**: per-site permissions are enforced and visible, and later modules plug their capabilities into this model.

---

## Phase 7: User Story 5 - Operator recovers when no administrator can sign in (Priority: P3)

**Goal**: with command-line access, the operator can print the setup code or issue an administrator recovery link. Nothing is exposed over the network.

**Independent Test**: lock yourself out of every administrator account, run `identity recovery-link --email …`, use the link, and sign in. Then confirm `operator_recovery_used` was recorded (quickstart.md §6).

### Tests for User Story 5 ⚠️ write first, confirm they fail

- [ ] T112 [P] [US5] Create `ITESTS/Identity/OperatorCommandTests.cs`, driving `HostCommandRunner` in-process against the test database and checking the contracts/operator-commands.md table:
  - `identity setup-code` prints the code and exits 0 when unclaimed, and exits 2 when claimed;
  - `identity recovery-link --email` exits 0 for an active administrator (printing the full URL when `Hosting:PublicUrl` is set, and the path plus instruction otherwise);
  - it exits 4 for an unknown address or a non-administrator, 2 when unclaimed, and 3 with pending migrations;
  - `operator_recovery_used` and `set_password_link_issued` (`issuedBy: operator`) are recorded;
  - the token appears on stdout and never in captured log output;
  - an unknown command exits 64.

### Implementation for User Story 5

- [ ] T113 [US5] Create `IDENTITY/Infrastructure/IdentityCommands.cs`, an `IHostCommand` named `identity`:
  - `setup-code` and `recovery-link --email <address>`, exactly per contracts/operator-commands.md;
  - output goes to `Console.Out`, never the logger;
  - it reuses `SetPasswordLinkService.IssueAsync(userId, issuedBy: null)`;
  - it checks pending migrations through `ModuleMigrationRunner`.
  Register it in `IdentityModule`.
- [ ] T114 [US5] Extend `tests/QuickBuilder.SmokeTests/ReferenceDeploymentSmokeTests.cs`: run `docker compose exec -T app dotnet QuickBuilder.Host.dll identity recovery-link --email <admin>`, then `POST /api/v1/identity/set-password` with the printed token, then sign in with the new password

**Checkpoint**: every user story is complete and independently verifiable.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: retention, log hygiene, performance proof, deployment and documentation.

- [ ] T115 [P] Create `IDENTITY/Infrastructure/IdentityRetentionService.cs`, an hourly `BackgroundService` modelled on 001's `OutboxRetentionService`. It deletes, each via batched `ExecuteDelete` of 10,000 rows driven by `TimeProvider`:
  - `security_events` older than `Identity:SecurityEvents:RetentionPeriod` (FR-029a);
  - `sign_in_throttles` untouched for 24 h;
  - expired or revoked `sessions` older than `AbsoluteLifetime`;
  - non-live `set_password_links` 30 days after expiry;
  - `known_devices` that are revoked or unused for longer than `KnownDeviceLifetime`.
  Expose `SweepAsync` for tests and register the service in `IdentityModule`.
- [ ] T116 [P] Create `ITESTS/Identity/IdentityRetentionTests.cs` (SC-012): seed events either side of the cutoff, advance the fake `TimeProvider` by 366 days and run `SweepAsync`. Only the newer events remain, byte-for-byte unchanged. Stale throttles, sessions, links and known devices are removed, and two concurrent sweeps don't fail.
- [ ] T117 Extend `ITESTS/Observability/LoggingTests.cs` (SC-009, FR-030):
  - drive setup, a failed and a successful sign-in, user creation, an email change, and issuing and using a link;
  - assert that no email address, display name, password, link token, session token or session id appears anywhere in captured logs;
  - the setup-code event is the one tolerated exception (T049).
- [ ] T118 [P] Extend `ITESTS/Observability/NoEgressTests.cs` (FR-035) so the blocked-egress run covers setup, sign-in, user creation and site creation
- [ ] T119 [P] Rewrite the binding note in `docker-compose.yml` (FR-033):
  - authentication and a setup code now protect the admin;
  - the binding stays `127.0.0.1` by default and may be widened after setup;
  - when fronting it with a TLS proxy, list the proxy in `Hosting__KnownProxies__0`;
  - add a commented `Hosting__PublicUrl` example.
- [ ] T120 [P] Create `docker-compose.perf.yml`, an override setting `cpus: 2` and `mem_limit: 4g` on `app`, and `tests/QuickBuilder.SmokeTests/SignInPerformanceTests.cs` (`[Trait("Category", "Performance")]`, SC-013, research R-19). The test sends 10 concurrent sign-ins × 20 rounds and asserts:
  - sign-in p95 ≤ 1 s;
  - `GET /api/v1/sites` p95 during the burst ≤ 2× the idle baseline.
- [ ] T121 [P] Update `docs/configuration.md`:
  - document every `Identity:*` key and `Hosting:PublicUrl`, with defaults and ranges;
  - replace the "ships no authentication" warning;
  - state at `Hosting:KnownProxies` that per-client throttling and the HTTPS rule depend on it (plan Risks).
- [ ] T122 [P] Create `docs/operations.md`: first-run setup and where the code appears, finishing setup before enabling log forwarding, the `identity setup-code` and `identity recovery-link` commands, security-event retention, and the upgrade note that anonymous API scripts from 001 stop working
- [ ] T123 [P] Update `docs/contributing.md` with an "Every action declares its authorization" step in the add-a-feature and add-a-module guides, linking `specs/002-identity-setup/contracts/authorization.md`, and a capability-registration example taken from `SitesModule.cs`
- [ ] T124 Confirm the CI coverage gate in `.github/workflows/ci.yml` includes the Identity assembly in the merged report, and that line coverage stays ≥ 80% with authentication, authorization and setup counted as critical paths (constitution X). Raise coverage by adding tests where it falls short, never by lowering the bar.
- [ ] T125 Run every section of quickstart.md against the reference deployment and record the outcomes, timings (SC-001, SC-007) and any deviations in `specs/002-identity-setup/quickstart-results.md`, as 001 did

---

## Dependencies & Execution Order

### Phase Dependencies

```text
Setup (T001–T006)
   └─▶ Foundational (T007–T045)       blocks everything below
          ├─▶ US1 Setup wizard (T046–T065)       P1 🎯 MVP
          │      └─▶ US2 Sign-in & enforcement (T066–T081)   P1, needs US1's GetMe/Status declarations and the smoke setup step
          ├─▶ US3 Account management (T082–T105) P2, needs Foundational only (tests seed users directly)
          ├─▶ US4 Per-site visibility (T106–T111) P2, backend needs Foundational only; GrantsEditor UI (T109) needs US3's user screens
          └─▶ US5 Operator recovery (T112–T114)   P3, needs US3's SetPasswordLinkService (T087)
                 └─▶ Polish (T115–T125)             after the stories it touches
```

### User Story Dependencies

- **US1**: depends only on Foundational.
- **US2**: depends on Foundational. T076 and T081 touch US1's endpoints and smoke steps, so do US2 after US1 or with US1 merged first.
- **US3**: depends only on Foundational. Its tests seed accounts through `IdentitySeed` rather than through the wizard.
- **US4**: the backend (T106–T108, T110) depends only on Foundational. T109 plugs into US3's `UsersView`/`UserEditView`.
- **US5**: depends on T087 (`SetPasswordLinkService`) from US3.

### Within Each User Story

- Tests are written first and must fail.
- Then: domain and services, then feature slices (endpoints), then admin API client, then screens, then i18n and screen tests, then the smoke step.
- A story is complete when its checkpoint holds.

### Parallel Opportunities

- **Setup**: T002, T003, T005 and T006.
- **Foundational**:
  - T007–T010 (kernel and command interface);
  - T012–T019 (options and every domain entity, one file each);
  - T023–T027 (security primitives);
  - T031 and T032;
  - T040–T043 (unit tests).
- **Every story**: all test tasks marked [P] can be written together.
- **Across stories**: once Foundational is done, US1, US3 and US4's backend can proceed in parallel on separate branches of work, because their files don't overlap. US2 follows US1, and US5 follows T087.

---

## Parallel Example: User Story 1

```text
# Tests together (all different files):
T046 ITESTS/Identity/SetupTests.cs
T047 ITESTS/Identity/SetupGateTests.cs
T048 ITESTS/Identity/UpgradeFrom001Tests.cs
T049 ITESTS/Observability/SetupCodeLoggingTests.cs

# Then backend and frontend in parallel:
T050 Identity.Contracts/IIdentitySetupStatus.cs   |  T058 src/admin/src/api/request.ts
T054 Features/GetStatus/GetStatus.cs              |  T059 src/admin/src/api/identity.ts
                                                  |  T063 src/admin/src/i18n/en.json
```

## Parallel Example: User Story 3

```text
T082 UserManagementTests.cs | T083 SetPasswordLinkTests.cs | T084 LastAdministratorTests.cs
T085 OwnAccountTests.cs     | T086 RevocationTests.cs
then
T087 SetPasswordLinkService.cs | T088 AdministratorGuard.cs | T089 ListUsers + GetUser | T097 IUserDirectory
then feature slices T090–T096 (sequential: they share IdentityModule registrations)
alongside T098, T101, T102, T104 on the admin side
```

---

## Implementation Strategy

### MVP first (User Story 1)

1. Phase 1, then Phase 2. At the checkpoint, 001's suite is green on signed-in clients.
2. Phase 3 (US1). A fresh install is claimed through the wizard, and a 001 install upgrades safely.
3. **Stop and validate** with quickstart.md §1 and §7.

The MVP still leaves Sites endpoints reachable by *any* signed-in session and doesn't
enforce per-endpoint capabilities, so it isn't releasable on its own. **US2 is the first
releasable increment**: it closes E-1.

### Incremental delivery

1. **US1 + US2**: a secure single-administrator installation. E-1 is closed and the smoke test covers setup and sign-in.
2. **+ US3**: multiple users, links, self-service, and revocation proven across instances.
3. **+ US4**: per-site Designer and Editor, with byte-identical 404s.
4. **+ US5**: operator recovery.
5. **Polish**: retention, log checks, SC-013 performance proof, docs, quickstart results.

### Parallel team strategy

After Foundational:
- **Developer A**: US1, then US2 (the critical security path).
- **Developer B**: the US3 backend (T082–T097).
- **Developer C**: the US4 backend (T106–T108) and the admin shell pieces of US3 and US4.
- **US5 and Polish**: pick these up once T087 lands.

---

## Notes

- **Declarations on every action**: every new controller action needs its declaration in the same task that creates it. T071 fails the build otherwise, which is the point.
- **Security events**: never write a password, token, setup code or session id into a `SecurityEvent.details` or a log message. T117 and T049 enforce this.
- **Raw SQL**: keep it inside the Identity module, parameterised, with the "no site-owned table" comment (plan Technology Constraints).
- **Commits**: commit after each task or logical group, on branch `002-identity-setup` (T001).
