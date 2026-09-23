# Feature Specification: Project Foundation and Skeleton

**Feature Branch**: `001-foundation-skeleton`

**Created**: 2026-09-22

**Status**: Draft

**Issue**: #1

**Input**: User description: "Project foundation and skeleton. Problem/goal: The team needs a working, deployable skeleton before any business feature, so every later spec follows the same structure and conventions. An operator can run docker compose up, see the app start, pass health checks and reach an empty admin shell. A developer can add a new module or feature by following an existing example. In scope: solution structure (Host, Shared Kernel, Shared Infrastructure, one sample module), command/query handler abstractions, site context, database outbox with background dispatcher, per-module schema and migrations, architecture tests, health checks, structured logging, Dockerfile, reference docker-compose, CI build and test pipeline, Vue 3 admin shell served by the app. Out: authentication and setup wizard, real business modules, editor, rendering."

## Clarifications

### Session 2026-09-22

- Q: Is the worked-example module a throwaway `Sample` module or the first minimal
  real module? → **A: the first minimal real module — `Sites`.** It owns site records
  and their host names, exposes create and list operations, and later specs extend it
  rather than replace it. Nothing in this feature is written to be thrown away.
- Q: On a brand-new installation with no sites configured, and the setup wizard out of
  scope, how does the admin shell stay reachable? → **A: the admin path is
  site-independent.** Admin requests never require a resolved site; only public paths
  do. The skeleton therefore defines the admin/public path split, and a fresh database
  legitimately holds zero sites.
- Q: How should the skeleton stop an operator from accidentally exposing an admin
  interface that has no login on it yet? → **A: the reference deployment publishes the
  application on the loopback interface only.** No application code guards it and no
  configuration flag is introduced; reaching the installation from any other machine
  requires a deliberate edit to the reference deployment file.
- Q: Does this feature have to prove multi-instance behaviour, or only claim it? → **A:
  prove it at the database level.** The test suite runs several migrators and several
  dispatchers concurrently against one real database, which exercises the actual
  contention. The pipeline does not orchestrate multiple application containers.
- Q: Should module endpoint addresses carry a version number from the first one? → **A:
  yes, from the start.** Every module endpoint is addressed as `/api/v1/<module>/...`,
  documented once as a convention that every later module follows, so no coordinated
  rename is ever needed.
- Q: Should request tracing and metrics be wired in now but switched off, or left out
  entirely? → **A: wired in, exporting nothing.** Instrumentation is present, no
  exporter is configured and no outbound traffic occurs by default; enabling export
  later is a configuration change. The log correlation identifier is the trace
  identifier, so no separate correlation scheme is invented.
- Q: Does the admin shell establish a translation and accessibility baseline now? →
  **A: yes, both, minimally.** Every piece of visible text is resolved through a lookup
  rather than written into the screen, one language ships and no translation process is
  set up; and the shell meets a stated accessibility baseline (keyboard reachable,
  visible focus, sufficient contrast) that later screens inherit.
- Q: Should the constitution be renamed to match the product name? → **A: yes, amended
  now.** The product is **QuickBuilder** and its assemblies follow the `QuickBuilder.*`
  format. The constitution was amended on this branch (2.3.0 → 2.3.1, editorial PATCH)
  and the plan artefacts were renamed with it. This spec names no assemblies, so the
  canonical product name is recorded here for terminology only.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Operator brings the product up from nothing (Priority: P1)

An operator who has never seen this product takes a fresh copy of the repository,
runs the single documented start command, and within minutes has a running
application: the database is created and up to date, the health endpoints report
healthy, and the admin interface loads in a browser. Nothing else is installed by
hand — no database client, no build toolchain, no manual schema scripts.

**Why this priority**: This is the whole point of the skeleton. Until something
deployable exists and reports itself healthy, no other work can be demonstrated,
tested end to end, or handed to anyone. It is the smallest slice that delivers real
value on its own.

**Independent Test**: On a clean machine with only a container runtime installed, run
the documented start command against the reference orchestration file and confirm:
containers reach a running state, the liveness and readiness endpoints report
healthy, and the admin interface renders in a browser. No other user story needs to
exist for this to be valuable.

**Acceptance Scenarios**:

1. **Given** a clean machine with a container runtime and an empty data volume,
   **When** the operator runs the documented start command,
   **Then** the application and its database start, the schema is created
   automatically, and the readiness endpoint reports healthy without any manual step.
2. **Given** the application is running against a database holding no sites yet,
   **When** the operator opens the admin address in a browser,
   **Then** the admin shell loads — it does not require a resolved site — shows its
   navigation frame, and displays an empty list of sites fetched live from the running
   application.
3. **Given** the operator creates a site in the admin shell and gives it a host name,
   **When** that host name is requested on a public path,
   **Then** the request resolves to that site.
4. **Given** the application is running,
   **When** the operator stops the containers and starts them again against the same
   data volume,
   **Then** the application starts successfully, no data is lost, and no manual
   migration step is required.
5. **Given** a required configuration value is missing or invalid,
   **When** the application starts,
   **Then** startup fails immediately with a message naming the offending
   configuration key, instead of starting in a broken state.
6. **Given** the database is unreachable,
   **When** an orchestrator probes the application,
   **Then** liveness reports healthy (the process is alive) while readiness reports
   unhealthy and identifies the failing dependency.

---

### User Story 2 - Developer adds a feature by following the worked example (Priority: P2)

A developer joining the project is asked to add a new capability. They open the
`Sites` module, find one complete write operation and one complete read operation —
each with its entry point, request and response shapes, validation, handler and tests
living together in one folder — and they add their own by copying that shape. They do
not have to touch the host, shared code, or any other module.

**Why this priority**: The second reason the skeleton exists is convention. Without a
worked example, every later feature invents its own structure and every later spec
re-decides the same questions.

**Independent Test**: Give a developer unfamiliar with the codebase the repository and
the contributor document, and ask them to add one new read operation and one new write
operation to the `Sites` module. Success is a working, tested pair of operations added
without editing files outside that module's folder.

**Acceptance Scenarios**:

1. **Given** the `Sites` module exists,
   **When** a developer inspects one feature folder,
   **Then** they find the HTTP entry point, request and response shapes, validation,
   the command or query, its handler and its tests together in that folder.
2. **Given** a developer adds a new feature folder following the example,
   **When** the application starts,
   **Then** the new operation is reachable over HTTP with no edits to the host or to
   shared code.
3. **Given** an HTTP entry point in the `Sites` module,
   **When** it is reviewed,
   **Then** it contains no business rules and no data access — it only turns a request
   into a handler call and the handler's result into a response.
4. **Given** a request that fails validation,
   **When** it is submitted,
   **Then** the response names the invalid inputs, is clearly distinguishable from an
   unexpected server failure, and the handler never runs.
5. **Given** a developer wants to add a whole new module,
   **When** they follow the documented steps,
   **Then** the module registers itself through a single entry point, owns its own
   database schema and migration history, and requires no changes to existing modules.

---

### User Story 3 - Work in one module triggers work in another without coupling them (Priority: P3)

Creating a site in the `Sites` module records that a site was created. A separate
background process picks that record up and delivers it to whoever is interested,
reliably, after the original operation has already returned to the caller. If the
original operation is rolled back, nothing is delivered. If delivery is attempted
twice, the effect still happens only once.

**Why this priority**: Every later module — publishing, cache invalidation, media
processing, scheduled content — depends on this mechanism. Getting it wrong later
means reworking those modules; establishing it now means they inherit it.

**Independent Test**: Create a site, confirm the "site created" event is recorded in
the same atomic unit as the site record, confirm the background dispatcher delivers it
to a test handler, then force a redelivery of the same event and confirm the downstream
effect happened exactly once.

**Acceptance Scenarios**:

1. **Given** a write operation that records an integration event,
   **When** the operation succeeds,
   **Then** both the state change and the event record are persisted together.
2. **Given** the same write operation,
   **When** the operation fails partway through,
   **Then** neither the state change nor the event record is persisted.
3. **Given** an undelivered event exists and the application restarts,
   **When** the application comes back up,
   **Then** the event is still delivered.
4. **Given** an event that has already been processed,
   **When** it is delivered again,
   **Then** the downstream effect occurs only once and the redelivery is recorded as a
   no-op.
5. **Given** an event whose handler keeps failing,
   **When** the configured retry attempts are exhausted,
   **Then** the event is set aside as failed with its error recorded, and other pending
   events continue to be delivered.
6. **Given** two application instances running against one database,
   **When** both dispatchers run,
   **Then** each pending event is claimed and dispatched by only one instance.

---

### User Story 4 - Boundary violations are caught automatically before merge (Priority: P4)

A developer opens a pull request that — deliberately or by accident — reaches across a
module boundary, puts business logic in the host, or exposes module internals. The
automated pipeline fails the pull request and names the specific rule that was broken.
Nobody has to catch it by eye in review.

**Why this priority**: Rules that are only written down decay. The value of the
structure established in stories 1 to 3 depends on it being mechanically enforced from
the first commit, before there is anything to untangle.

**Independent Test**: Introduce a set of deliberate violations, one per rule, on a
throwaway branch; run the pipeline; confirm every one fails the build with a message
identifying the rule.

**Acceptance Scenarios**:

1. **Given** a change where one module references another module's implementation,
   **When** the pipeline runs,
   **Then** the build fails and the message names the offending reference.
2. **Given** a change that puts business logic or data access in the host,
   **When** the pipeline runs,
   **Then** the build fails.
3. **Given** a change that makes a module's internal type visible outside its
   contracts surface,
   **When** the pipeline runs,
   **Then** the build fails.
4. **Given** a change that introduces a compiler or analyzer warning, or that is not
   formatted to the project's style,
   **When** the pipeline runs,
   **Then** the build fails.
5. **Given** a pull request with no violations,
   **When** the pipeline runs,
   **Then** build, unit tests, integration tests against a real database and
   architecture checks all pass, and the result is reported on the pull request.

---

### User Story 5 - Operator diagnoses a running instance (Priority: P5)

Something looks wrong in a running installation. The operator reads the application's
log output, follows a single request from arrival to completion using one identifier,
sees which site and which module it concerned, and raises the detail level for one
noisy component without rebuilding anything.

**Why this priority**: Valuable, but an installation that starts and serves requests is
already demonstrable without it. It is last because every earlier story can be verified
without it.

**Independent Test**: Issue a request that fails, then find every log entry for that
request by its correlation identifier alone, and confirm no credentials, personal data
or content bodies appear in the output.

**Acceptance Scenarios**:

1. **Given** the application is running,
   **When** any request is handled,
   **Then** its log entries share one correlation identifier and are emitted as
   structured records carrying timestamp, level, message and — where applicable — site
   and module.
2. **Given** an unhandled failure occurs,
   **When** the operator reads the log,
   **Then** the failure is logged with its correlation identifier and enough context to
   locate the operation, while the caller receives an error response revealing no
   internal detail.
3. **Given** the operator changes the configured log level for one component,
   **When** the application restarts,
   **Then** only that component's verbosity changes, with no rebuild.
4. **Given** a request containing a secret, a personal detail or a content body,
   **When** its log entries are inspected,
   **Then** none of those values appear in the output.

---

### Edge Cases

- **Database not ready at startup**: the application retries for a bounded period
  rather than crash-looping instantly, and reports the reason through readiness while
  it waits.
- **Migration fails partway**: the application refuses to serve traffic (readiness
  stays unhealthy), the failure is logged with the failing migration named, and the
  database is left in a state that can be retried.
- **Two instances start simultaneously against an empty database**: only one applies
  migrations; the other waits and then starts normally. Neither corrupts the schema.
- **Public request arrives for an unknown host name**: it is rejected with a clear
  response that discloses nothing about which sites exist, and does not silently fall
  back to another site.
- **Installation has zero sites**: the admin shell still loads and shows an empty
  state; readiness still reports healthy; only public requests are rejected.
- **Two sites claim the same host name**: the second is rejected at creation with a
  message naming the conflict, so resolution can never be ambiguous.
- **Admin deep link opened directly or refreshed**: the admin route loads correctly
  rather than returning "not found".
- **Admin asset path collides with an application endpoint path**: routing resolves
  deterministically by convention, not by registration order.
- **Upgrade replaces the image while a browser holds old admin assets**: the browser
  receives the new assets rather than a stale mix of old and new.
- **Outbox grows large**: dispatch throughput does not degrade without bound, and
  delivered events are retained only for a documented period.
- **An instance dies mid-dispatch**: the event it claimed becomes eligible again and is
  eventually delivered rather than stranded.
- **Container runs as a non-root user**: writable paths (data, logs, temporary files)
  remain usable and the application does not require root to start.

## Requirements *(mandatory)*

### Functional Requirements

**Solution structure and module pattern**

- **FR-001**: The product MUST be a single deployable application composed of
  self-registering modules, where the host contains only composition, middleware and
  configuration, and no business logic or data access.
- **FR-002**: The skeleton MUST include exactly one module — `Sites` — which is a real,
  minimal module (not a throwaway) and serves as the worked example of the complete
  pattern end to end. Its scope in this feature is: create a site, list sites, and
  assign one or more host names to a site. Anything further about sites (settings,
  themes, deletion, permissions) belongs to a later specification.
- **FR-003**: Shared code MUST be separated into a kernel (result and error types,
  event abstractions) and a shared infrastructure layer (site context, outbox, storage
  and cache abstractions); neither may contain module-specific concepts.
- **FR-004**: Each module MUST expose exactly one registration entry point that the
  host invokes; adding or removing a module MUST NOT require edits to any other module.
- **FR-005**: Each module MUST publish a contracts surface containing only the
  interfaces, data shapes and integration events other modules may use; everything else
  in the module MUST be invisible outside it.

**Command and query handling**

- **FR-006**: Write operations MUST be expressed as commands and read operations as
  queries, both dispatched through a project-owned handler abstraction resolved at
  runtime from the application's service registry.
- **FR-007**: HTTP entry points MUST contain no business rules and no data access; they
  MUST translate a request into a single handler invocation and the handler's result
  into an HTTP response.
- **FR-008**: Request validation MUST run before the handler executes; a validation
  failure MUST return a structured error response naming the invalid inputs, MUST be
  distinguishable from an unexpected failure, and the handler MUST NOT run.
- **FR-009**: Handler results MUST distinguish success, expected failure (with a
  machine-readable reason) and unexpected failure, so entry points can map them to
  responses without inspecting exceptions.
- **FR-010**: Each feature MUST keep its entry point, request and response shapes,
  validation, command or query, handler and tests together in one folder.
- **FR-010a**: Every module endpoint MUST be addressed as `/api/v1/<module>/...`, with
  the version segment present from the first endpoint. The convention MUST be
  documented once and MUST be the same for every module; a later breaking change to a
  module's contract raises the version segment rather than renaming the endpoint.

**Site context and site-scoped data**

- **FR-011**: The application MUST separate its request paths into an administrative
  surface and a public surface, and the split MUST be explicit and documented rather
  than implied by routing order.
- **FR-011a**: For every request on the public surface, the application MUST resolve
  the current site from the requested host name and expose it to handlers as ambient
  context, so handlers never read request headers themselves.
- **FR-011b**: Requests on the administrative surface MUST NOT require a resolved
  site; the admin shell and its operations MUST work on an installation that has no
  sites at all. Where an admin operation concerns one specific site, that site MUST be
  named explicitly by the request, never inferred from the host name.
- **FR-012**: The application MUST behave correctly behind a reverse proxy, honouring
  forwarded host, scheme and client address, and supporting a configurable base path.
- **FR-013**: Every site-owned record MUST carry a site identifier, and reads on the
  public surface MUST be restricted to the current site automatically rather than by
  per-query code. Host names are the worked example of a site-owned record.
- **FR-013a**: Administrative operations that deliberately read across sites (listing
  all sites, for example) MUST bypass the automatic restriction explicitly, with the
  bypass annotated in the code and covered by a dedicated test proving it is
  intentional. An implicit or accidental bypass MUST fail the architecture checks.
- **FR-014**: A public request whose host name does not match any configured site MUST
  be rejected with a clear response that discloses nothing about which sites exist, and
  MUST NOT fall back to an arbitrary site.
- **FR-014a**: An installation with zero configured sites MUST still start, report
  ready, and serve the administrative surface. Public requests in that state are
  rejected under FR-014.

**Outbox and background dispatch**

- **FR-015**: When a module records an integration event, the event record MUST be
  persisted in the same atomic unit as the state change it describes, so that either
  both or neither survive.
- **FR-016**: A background dispatcher MUST deliver persisted events to their registered
  handlers at least once, and undelivered events MUST survive application restarts.
- **FR-017**: Integration event handlers MUST be idempotent: redelivering an
  already-processed event MUST NOT produce a second effect.
- **FR-018**: Delivery failures MUST be retried with increasing delay up to a
  configurable limit, after which the event is set aside as failed with its error
  recorded; failed events MUST NOT block delivery of other pending events.
- **FR-019**: With multiple application instances running against one database, each
  pending event MUST be claimed and dispatched by exactly one instance at a time, and
  an event claimed by an instance that stops MUST become eligible again.
- **FR-020**: Delivered events MUST be retained for a configurable period and then
  removed, so the store does not grow without bound.
- **FR-021**: No single transaction may span more than one module's data.

**Data ownership and migrations**

- **FR-022**: Each module MUST own a separate database schema, its own data context and
  its own migration history; no module may read or write another module's tables.
- **FR-023**: Migrations MUST run automatically at startup by default, MUST be
  forward-only and MUST be safe to re-run; an operator MUST be able to disable automatic
  migration by configuration and run migrations as a separate step instead.
- **FR-024**: When multiple instances start against the same database, migrations MUST
  be applied by one instance only, with the others waiting rather than failing.

**Automated architecture enforcement**

- **FR-025**: Automated checks MUST fail the build when a module references another
  module's implementation, when the host contains business logic or data access, when
  shared code depends on a module, or when a module's internals are visible outside its
  contracts surface.
- **FR-026**: These checks MUST run in the same pipeline stage as the test suite and
  MUST fail the build rather than emit a warning.

**Health, configuration and startup**

- **FR-027**: The application MUST expose a liveness endpoint that succeeds whenever the
  process can serve requests, and a readiness endpoint that succeeds only when
  configuration is valid, migrations have completed and the database is reachable.
- **FR-028**: Missing or invalid required configuration MUST cause startup to fail
  immediately with a message naming the offending key, rather than starting in a
  degraded state.
- **FR-029**: All configuration MUST come from a settings file with documented keys and
  safe defaults, and every key MUST be overridable by an environment variable.
- **FR-030**: If the database is not yet available at startup, the application MUST
  retry for a bounded, configurable period and report the reason through readiness while
  waiting.

**Structured logging**

- **FR-031**: All log output MUST be structured and written to standard output by
  default; each entry MUST carry a timestamp, level, message, a correlation identifier
  for the originating request or background operation, and — where applicable — the site
  and module.
- **FR-031a**: The correlation identifier in FR-031 MUST be the identifier assigned by
  the application's request tracing, not a separate scheme invented for logging, so
  logs and traces refer to the same operation by the same value.
- **FR-031b**: Request tracing and metrics MUST be instrumented in this feature but
  MUST export nothing by default: no collector is configured, no outbound network
  request is made, and an offline installation behaves identically to a connected one.
  Enabling export later MUST be a configuration change only, requiring no code change
  and no new instrumentation.
- **FR-032**: Log output MUST NOT contain credentials, secrets, personal data or content
  bodies.
- **FR-033**: Minimum log level MUST be configurable globally and per component without
  rebuilding the application.
- **FR-034**: Unexpected failures MUST be logged with their correlation identifier,
  while the response returned to the caller reveals no internal detail.

**Packaging and reference deployment**

- **FR-035**: The build MUST produce a single container image containing the application
  and the prebuilt admin assets; the operator MUST NOT need to run a separate frontend
  process or any build toolchain.
- **FR-036**: The container MUST run as a non-root user and MUST start successfully with
  only the documented configuration supplied.
- **FR-037**: The repository MUST include a reference orchestration file that starts the
  application and its database together, with documented defaults, a persistent data
  volume, and health probes wired to the health endpoints.
- **FR-037a**: The reference orchestration file MUST publish the application on the
  loopback interface only, so a default installation is reachable from the operator's
  own machine and from nowhere else. The file MUST state, at the binding itself, that
  widening it before authentication exists exposes an unauthenticated admin interface.
- **FR-037b**: The application itself MUST NOT restrict which interface it listens on
  or gate the admin surface behind an acknowledgement flag; the loopback binding is a
  property of the reference deployment only, so that nothing has to be removed when
  authentication is added.
- **FR-038**: The image MUST contain no secrets, and the reference orchestration file
  MUST make clear which values an operator has to replace before any real use.

**Continuous integration**

- **FR-039**: A pipeline MUST run on every pull request and perform at minimum: restore,
  build, unit tests, integration tests, architecture checks and a formatting check; it
  MUST fail on any compiler or analyzer warning.
- **FR-040**: Integration tests MUST run against a real database instance provisioned by
  the pipeline; in-memory database substitutes MUST NOT be used.
- **FR-040a**: The test suite MUST include concurrency tests that run several migrators
  simultaneously and several dispatchers simultaneously against one real database,
  proving FR-019 (each event claimed once) and FR-024 (migrations applied once) under
  genuine contention rather than by assertion. Orchestrating multiple application
  containers in the pipeline is explicitly not required.
- **FR-041**: The pipeline MUST build the container image to prove it builds, and MUST
  report pass or fail status back to the pull request.

**Admin shell**

- **FR-042**: The application MUST serve an admin single-page interface at a dedicated
  path, with client-side routing, a navigation frame and at least one placeholder
  screen.
- **FR-043**: Direct navigation to, or refresh of, any admin route MUST load the
  interface correctly rather than returning "not found".
- **FR-044**: The admin shell MUST list sites and create a site through the `Sites`
  module, demonstrating the complete read and write paths from browser to handler to
  database and back, including the empty-list state on a fresh installation.
- **FR-045**: The admin shell MUST show a usable state while a call is in flight and
  when a call fails, rather than a blank screen.
- **FR-045a**: Every piece of text the admin shell displays — labels, messages, empty
  states, validation and error text — MUST be resolved through a lookup rather than
  written into the screen. Exactly one language ships in this feature and no
  translation process is established; adding a language later MUST NOT require
  reopening the screens.
- **FR-045b**: The admin shell MUST meet a stated accessibility baseline that later
  screens inherit: every interactive element reachable and operable by keyboard alone,
  a visible focus indicator on each, text and controls meeting a documented contrast
  minimum, and every form control carrying an associated label. The baseline MUST be
  written down so later specifications can be held to it.
- **FR-046**: Admin responses MUST carry the project's default security headers, and
  admin assets MUST be versioned so an upgrade never serves a stale mix of old and new
  assets.

**Contributor documentation**

- **FR-047**: The repository MUST document, in one place, how to run the application
  locally, how to add a feature to an existing module, and how to add a new module —
  each referencing the `Sites` module as the worked example.

### Key Entities

- **Site**: A website hosted by this installation, owned by the `Sites` module.
  Identified internally by a stable identifier, carries a display name, and is matched
  to public requests by one or more host names. Every site-owned record references
  exactly one site. A fresh installation has none.
- **Host Name**: A name that maps a public request to one site. Site-owned, unique
  across the installation, and the worked example of automatic site-scoped filtering.
- **Module**: A unit of the application that owns a database schema, a set of features
  and a contracts surface, and registers itself with the host through one entry point.
- **Feature (slice)**: One operation within a module — its entry point, request and
  response shapes, validation, command or query, handler and tests, kept together.
- **Integration Event**: A record that something happened in one module which other
  modules may react to. Persisted alongside the change that produced it and delivered
  asynchronously.
- **Outbox Entry**: The stored form of an integration event, carrying its payload, type,
  creation time, attempt count, claim state, last error and completion state.
- **Migration History**: A per-module record of which schema changes have been applied,
  making startup migration safe and repeatable.
- **Administrative Surface / Public Surface**: The two request surfaces of the
  application. The administrative surface serves the admin shell and its operations and
  needs no resolved site; the public surface serves site visitors and always requires
  one.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a clean machine with only a container runtime installed, an operator
  goes from a fresh copy of the repository to a loaded admin interface — on a database
  with zero sites and no seeded data — in under 5 minutes, using one documented
  command, with no manual database work and no code changes.
- **SC-001a**: From that loaded admin interface, the operator creates the first site
  and reaches it on a public path by its host name in under 2 minutes, without
  restarting the application.
- **SC-002**: On the reference environment (two cores, four gigabytes of memory), the
  application reports ready within 60 seconds of a cold start against an empty database,
  and within 30 seconds against an already-migrated database.
- **SC-003**: A developer who has not seen the codebase before adds one working, tested
  read operation and one working, tested write operation to the `Sites` module in under
  30 minutes, changing no files outside that module.
- **SC-004**: Each boundary rule in FR-025 is proven enforced: a deliberate violation of
  every rule fails the pipeline — 100% caught — and each failure message names the rule
  broken.
- **SC-005**: Every integration event recorded by a successful operation is delivered at
  least once, and forced redelivery of already-processed events produces zero duplicate
  effects across the full test suite.
- **SC-006**: Stopping and restarting the installation against existing data loses no
  data and requires zero manual steps, verified by an automated start-stop-start test.
- **SC-007**: The full pull request pipeline completes in under 10 minutes and fails on
  any failing test, architecture violation, formatting deviation, or compiler or
  analyzer warning.
- **SC-008**: Any single request can be traced end to end from log output using one
  correlation identifier alone, and an automated check confirms no credentials, personal
  data or content bodies appear in log output.
- **SC-009**: Adding a new module requires changes to no existing module and no more
  than one line of host registration, demonstrated by adding a second throwaway module
  in a test.
- **SC-010**: The reference deployment survives a database outage: readiness turns
  unhealthy while the database is down and returns healthy automatically within 60
  seconds of the database returning, with no restart.
- **SC-011**: Every public operation is proven site-scoped and every cross-site read is
  proven deliberate: an automated test asserts that no public query returns another
  site's data, and that each explicit bypass is annotated and individually tested.
- **SC-012**: Under concurrent execution against one real database, migrations are
  applied exactly once and every queued event is delivered to exactly one claimant,
  across repeated runs of the concurrency tests with zero duplicate applications and
  zero duplicate claims.
- **SC-013**: Every admin journey in this feature is completable using the keyboard
  alone, 100% of visible admin text resolves through the lookup with zero strings
  written into screens, and an automated check confirms both.
- **SC-014**: A default installation makes zero outbound network requests from start-up
  through creating a site and viewing it, verified with outbound traffic blocked.

## Scope

### In Scope

- Solution structure: host, shared kernel, shared infrastructure, and the `Sites`
  module as the single worked example.
- The `Sites` module, minimally: create a site, list sites, assign host names.
- Command and query handler abstractions with validation and result handling.
- The administrative / public request surface split.
- Site context resolution on public paths and site-scoped data access.
- Database outbox with a background dispatcher, retries and idempotent handling.
- Per-module database schemas and migrations, applied automatically at startup.
- Automated architecture tests enforcing module boundaries.
- Liveness and readiness health checks, and fail-fast configuration validation.
- Structured logging with correlation, site and module context.
- Request tracing and metrics instrumentation, exporting nothing by default.
- Container image, reference orchestration file and documented configuration.
- Continuous integration pipeline: build, tests, architecture checks, formatting.
- Admin single-page shell served by the application, calling the `Sites` module.
- Admin text lookup (one language) and a written accessibility baseline for the shell.
- Contributor documentation for adding a module and adding a feature.

### Out of Scope

- Authentication, authorization policies, user accounts and the first-run setup wizard.
- Every module other than `Sites`: identity, pages, content collections, media,
  rendering, editor, setup.
- Everything about sites beyond create, list and host name assignment — site settings,
  themes, renaming, deletion, per-site permissions, host name verification.
- The page editor and the editor canvas.
- Public site rendering, render caching and cache invalidation.
- Optional infrastructure providers (object storage, distributed cache, email) beyond
  the abstractions they will later implement.
- Publishing the container image to a registry, release notes and upgrade notes.
- Export and import of sites.
- Shipping a second language, any translation or review workflow, and right-to-left
  layout. Only the lookup mechanism and one language are in scope (FR-045a).
- Exporting telemetry anywhere, configuring a collector, and any outbound network call
  whatsoever. The instrumentation itself is in scope (FR-031b); shipping data out of
  the process is not.

## Assumptions

- **Technology is already fixed**: the platform, data store, frontend framework and
  logging approach are set by the project constitution and are not decisions this
  specification makes. This document states observable behaviour only; the plan maps it
  onto those fixed choices.
- **One required dependency**: the reference deployment is the application plus one
  relational database. No message broker, cache server or object store is required for
  this feature.
- **`Sites` is real but deliberately minimal**: it is the worked example precisely
  because it is real — later specs extend it rather than delete it. This feature
  implements only create, list and host name assignment; everything else about sites
  is deferred.
- **A fresh installation has no sites**: nothing is seeded. The admin shell is designed
  around the empty state, and creating the first site is an ordinary admin action, not
  a special bootstrap path.
- **No authentication yet**: the admin shell and the `Sites` module operations are
  reachable without credentials in this feature. The reference deployment compensates
  by binding to loopback only (FR-037a), which makes the default installation a
  single-machine one. Exposing this feature's output to a network is an explicit
  operator action against written advice, and remains unsafe until authentication
  lands.
- **Continuous integration**: the pipeline runs on the repository's existing hosted CI,
  builds for the default architecture only on pull requests, and publishes no artefacts.
- **Reference environment**: startup and availability criteria are measured on the
  documented reference environment (two cores, four gigabytes of memory).
- **Operator skill**: operators can run containers and edit a configuration file, but
  are not expected to run database tools, build tooling or language runtimes by hand.
- **Outbox delivery is in-process**: integration events are delivered to handlers inside
  the same application, not to external subscribers.

## Dependencies

- The project constitution (`.specify/memory/constitution.md`), which fixes the
  architecture rules this feature has to make executable.
- A hosted continuous integration environment able to run containers, so integration
  tests can use a real database.
- GitHub issue #1, which this feature implements and which the pull request closes.
