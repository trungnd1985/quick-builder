<!--
Sync Impact Report
Version change: 2.5.0 → 2.5.1
Modified principles:
  - I. Modular Monolith with Vertical Slices: names the migration runner and
    its database version record (schema `platform`, own EF migrations) among
    what `QuickBuilder.Shared.Infrastructure` holds. The runner has lived there
    since 001 (D-04); the record is its own bookkeeping, not module data.
  - VI. Content Schemas Are Versioned Contracts: states that the forward
    migration of stored data may be an upgrade step applied when a document is
    read and stored on its next save or publish; no bulk rewrite is required.
  Both are clarifications of existing rules, a PATCH change under the
  Governance versioning rule.
Modified sections: none.
Reason: feature 019 (release pipeline and upgrade safety, issue #19) must check
  the database version before any module migrates
  (specs/019-release-upgrade-safety/research.md RL-07) and upgrades old
  documents on read (RL-19, clarification Q4). Approved by the maintainer on
  2026-10-23 (/speckit-analyze findings C1, C2).
Affected specs/plans:
  - specs/019-release-upgrade-safety/ — plan.md records this amendment.
  - docs/contracts/page-schema.md §6 and binding-expression.md are updated to
    match in the same PR.
Templates requiring updates:
  - .specify/templates/*: none. Templates read this constitution at runtime.
Follow-up TODOs: none.
-->

# QuickBuilder Constitution

A self-hosted web builder for multi-page websites with a built-in CMS.
Customers download and run it on their own infrastructure; we do not operate
it for them. This document defines non-negotiable rules. Every spec, plan,
task and implementation MUST comply. When a plan conflicts with this document,
this document wins; changing it requires the amendment process in Governance.

## Core Principles

### I. Modular Monolith with Vertical Slices

**Modules**
- The backend is ONE deployable ASP.NET Core application composed of modules:
  `Identity`, `Sites`, `Pages`, `Editor`, `Rendering`, `Cms`, `Media`, `Forms`,
  `Setup`.
- Each module consists of exactly two projects:
  - `QuickBuilder.<Module>.Contracts`: public interfaces, DTOs and integration
    events. The only project other modules may reference.
  - `QuickBuilder.<Module>`: everything else, `internal` by default, organised as
    `Domain/`, `Features/`, `Infrastructure/` and a `<Module>Module.cs` that
    exposes `Add<Module>Module()` for registration.
- `QuickBuilder.Host` only composes modules, middleware and configuration; it
  contains no business logic. Shared code lives in `QuickBuilder.Shared.Kernel`
  (Result type, event abstractions) and `QuickBuilder.Shared.Infrastructure`
  (site context, outbox, storage/cache providers, and the migration runner with
  its database version record in a `platform` schema with its own migrations),
  and MUST stay small and free of module-specific concepts.

**Inside a module**
- Code is organised by feature (vertical slice), not by technical layer: each
  folder under `Features/` holds its controller action(s), request/response
  DTOs, command or query, handler and validator together.
- Controllers live in their module and are registered via `AddApplicationPart`.
- Commands and queries are dispatched through in-house `ICommandHandler<T>` and
  `IQueryHandler<TQuery, TResult>` interfaces registered in DI. MediatR and
  similar mediator libraries are not used.
- Handlers use the module's `DbContext` directly. Generic repository wrappers
  over EF Core are not allowed.
- A rich domain model (entities enforcing invariants, domain events) is
  REQUIRED for `Pages`, `Cms` and `Rendering`. Other modules MAY use simple
  handlers over the `DbContext` until complexity justifies more.

**Between modules**
- A module MUST NOT reference another module's implementation project, types
  or tables. Synchronous reads go through interfaces in `Contracts`.
- Side effects in other modules are triggered by integration events written to
  a database outbox in the same transaction as the change, then dispatched by
  a background worker. Handlers MUST be idempotent. No message broker is
  required.
- No database transaction spans more than one module.

**Data**
- One PostgreSQL database; each module owns its own schema (`pages`, `cms`,
  ...), its own `DbContext` and its own migrations history table.

**Enforcement**
- Architecture tests (NetArchTest or equivalent) MUST enforce the project
  references, visibility and cross-module rules above in CI.

Rationale: one process is the easiest thing for customers to deploy and
operate; module boundaries keep the codebase maintainable and splittable;
vertical slices keep each Spec Kit feature in one place with little ceremony.

### II. Easy to Install, Operate and Upgrade

- The product ships as a single Docker image plus a reference
  `docker-compose.yml` (app + PostgreSQL). A fresh install MUST go from
  `docker compose up` to a working admin in under 5 minutes, with no manual
  SQL and no code changes.
- All configuration comes from `appsettings.json` overridable by environment
  variables, with documented keys and safe defaults. Invalid configuration
  MUST fail fast at startup with a clear message.
- First run shows a setup wizard that creates the initial admin account.
  There are NO default credentials, ever.
- Database migrations run automatically on startup (with a documented option
  to disable and run them via a CLI command instead). Migrations MUST be
  forward-only and safe to re-run.
- The product MUST run fully offline: no telemetry, license checks or calls to
  our servers unless the operator explicitly enables them.
- Every deployable exposes health checks (`/health/live`, `/health/ready`).

### III. Upgrades Never Lose Data

- Releases follow Semantic Versioning. Upgrading between any two minor
  versions of the same major version MUST work by replacing the image.
- Every release that changes stored data ships an automatic migration and a
  test that upgrades a database snapshot from the previous release.
- Breaking changes (configuration keys, contracts, extension points) are
  allowed only in a major version and MUST be listed in upgrade notes.
- Built-in export/import of a site (content, schemas, media manifest) exists so
  operators can back up, migrate between servers and recover.

### IV. Pluggable Infrastructure, Minimal Required Dependencies

- The only required external dependency is PostgreSQL. Everything else has a
  built-in default that works on a single server:
  - Media storage: local file system (default); S3-compatible and Azure Blob
    as optional providers.
  - Cache: in-memory (default); Redis optional for multi-instance deployments.
  - Background jobs: built-in, database-backed; no external queue required.
  - Email: SMTP.
- Each of these is accessed only through an abstraction; providers are
  selected by configuration, not by code changes.
- The app MUST work correctly with multiple instances behind a load balancer
  once the operator configures shared cache and storage providers.

### V. Multi-Site Within One Installation

- One installation can host multiple sites. Every site-owned entity carries
  `SiteId`, filtered via EF Core global query filters, never by hand.
  Bypassing a filter requires a code comment and a dedicated test.
- The site for a public request is resolved from the `Host` header against the
  hostnames configured for each site. TLS and domain routing are the
  operator's reverse proxy's responsibility; the app MUST work correctly behind
  a proxy (forwarded headers, configurable base path).
- Permissions can be scoped per site (a user may be Editor on one site only).
- Cache keys, media paths and background jobs are namespaced by `SiteId`.

### VI. Content Schemas Are Versioned Contracts

- The page tree JSON schema (`{ id, type, props, children }`), the collection
  schema and the binding expression format are shared contracts between the
  editor and the renderer. They live in `docs/contracts/` and in a shared
  schema package; both sides MUST validate against the same definition.
- Every stored document carries a `schemaVersion`. Breaking changes require a
  new version plus a forward migration of stored data, which may be an upgrade
  step applied when a document is read and stored on its next save or publish
  (no bulk rewrite is required); the renderer MUST be able to render every
  version still present in the database.
- Collection fields are identified by a stable, immutable field ID. Display
  names and slugs may change; IDs never do. Stored item data is keyed by
  field ID.
- A block type is defined once (props schema, defaults, editor controls,
  renderer). Adding a block MUST NOT require changes to unrelated blocks.

### VII. Draft and Published Are Separate Worlds

- Pages, components and collection items keep draft and published state
  separately. Editing never changes what visitors see until publish.
- Published sites read ONLY published data. The public rendering path MUST NOT
  have write access to content tables.
- Publish is an explicit, atomic, audited operation and produces a version that
  can be rolled back.
- Scheduled publish/unpublish is handled by background jobs, idempotently.

### VIII. Rendering Is Server-Side, Cached and Correctly Invalidated

- Published pages are rendered server-side to complete HTML (SEO, performance).
  Client-side JavaScript on published sites is progressive enhancement only.
- Rendered output is cached. Every publish MUST invalidate exactly the affected
  cache entries, using a tracked dependency graph (page -> components,
  page -> collections, template -> items), and invalidation MUST work across
  instances when a shared cache is configured.
- Performance budgets, measured on the reference compose setup (2 vCPU,
  4 GB RAM): p95 server render under 200 ms on a cache miss, under 20 ms on a
  cache hit; no published page ships more than 100 KB of JS unless a block
  explicitly requires it.

### IX. Secure by Default

- Operators vary in skill, so the default configuration MUST be the secure one:
  HTTPS assumed behind the proxy, secure cookies, strict security headers on
  the admin app, rate limiting on login.
- Rich text is stored as structured JSON (ProseMirror/TipTap format), never as
  raw HTML. The renderer produces HTML from JSON with an allow-list.
- All user-provided values rendered into HTML, attributes, CSS or URLs MUST be
  encoded for their context. Custom code embeds, if ever supported, are an
  explicit, opt-in feature with its own spec.
- Uploads are validated by content type and size and never executed or served
  from an executable path.
- Authorization is enforced server-side on every endpoint through policy-based
  checks (roles: Administrator, Designer, Editor, scoped per site). The UI
  hiding a button is not authorization.
- Secrets never live in the repository or in the image. No PII or content
  bodies in logs.

### X. Test-First for Behaviour, Tests Always for Contracts

- Every endpoint has integration tests (WebApplicationFactory +
  Testcontainers PostgreSQL); in-memory database providers are not allowed.
- Domain logic has unit tests. Schema validation, binding resolution and
  rendering of every block type have snapshot or golden-file tests.
- Each optional infrastructure provider has its own integration test suite.
- A release pipeline test boots the published Docker image with the reference
  compose file and runs a smoke test (setup wizard, create page, publish, view).
- Critical paths (publish, render, site resolution, migrations, upgrades) keep
  at least 80% line coverage.
- A bug fix starts with a failing test that reproduces it.

### XI. Simplicity and Incremental Delivery

- Build the smallest thing that satisfies the current spec. No speculative
  abstractions, plugin systems or generic engines without a spec that needs them.
- Every new third-party dependency must be justified in the plan (why, license
  compatible with redistribution, maintenance status, alternative considered).
- Each feature is independently demoable and merged behind a feature flag when
  incomplete.

## Technology Constraints

These are fixed. Plans MUST use them and MUST NOT propose alternatives unless
a constitution amendment is approved first.

- Backend: .NET 10, ASP.NET Core MVC controllers (`[ApiController]`,
  attribute routing). Minimal APIs are not used. Controllers stay thin: they
  map HTTP to the feature's command/query handler and contain no business logic
  or data access.
- Data access: EF Core with PostgreSQL (Npgsql) is the ONLY data access
  technology; `jsonb` stores page trees, schemas and item data. No Dapper or
  other micro-ORMs, so site filters, conventions and migrations are applied in
  one place.
  - Read paths use `AsNoTracking()` and projections to DTOs; the hot rendering
    path uses compiled queries where profiling shows a benefit.
  - Raw SQL is allowed only through EF Core (`FromSql`/`SqlQuery`) inside the
    module that owns the tables, MUST be parameterised, and MUST apply the site
    filter explicitly with a test proving it.
  - Schema changes are made only through EF Core migrations.
- Frontend (admin + editor): Vue 3 with Composition API, TypeScript strict mode,
  Vite, Pinia. Built assets are embedded in or served by the ASP.NET Core app;
  operators never run a separate Node process.
- Editor canvas: in-house Vue 3 components rendered in the admin document (no
  iframe, no page-builder framework); native HTML5 drag and drop, with a
  keyboard equivalent for every drag action; CSS container queries for the
  responsive preview, so the same block stylesheet serves published pages;
  TipTap (ProseMirror) for rich text only. Decided in
  specs/011-editor-core (research ED-01, ED-02, ED-03, ED-09).
- Distribution: multi-arch (amd64, arm64) Docker images built by GitHub Actions
  and published to a container registry, plus release notes and upgrade notes.
- Observability: structured logging (Serilog) to console by default,
  OpenTelemetry export available but disabled by default.

## Development Workflow

- One feature = one GitHub issue = one spec folder = one branch = one pull request.
- Feature IDs are GitHub issue numbers, never auto-incremented numbers. Before
  running `/speckit.specify`, the feature MUST have an issue; the command is
  invoked with the issue number and the script is called with `--number <issue>`,
  producing `specs/<issue>-<short-name>/` and branch `<issue>-<short-name>`.
- A spec folder whose prefix does not match an open or closed issue, or that
  duplicates an existing prefix, fails CI.
- The spec, the plan and the pull request each link back to the issue; the PR
  closes the issue on merge.
- Order of work: specify -> clarify -> plan -> tasks -> analyze -> implement.
  Clarify and analyze are not optional.
- Specs describe what and why only; technical decisions belong in the plan.
- Every plan contains a "Constitution Check" section listing each principle
  and how the plan complies, or the justified exception.
- Every plan states its impact on operators: new configuration keys,
  migrations, new dependencies, upgrade notes. "None" must be stated explicitly.
- Changes to anything in `docs/contracts/` require updating the contract
  document, the shared schema package and the migration in the same PR.
- CI must pass build, tests, architecture tests, formatting and analyzers
  (warnings as errors) before merge.

## Governance

- This constitution supersedes all other conventions in the repository.
- Amendments are made via a PR that updates this file, states the reason and
  lists affected specs/plans. Versioning: MAJOR for removed or redefined
  principles, MINOR for new principles or sections, PATCH for clarifications.
- Any exception to a MUST rule is recorded in the plan's Complexity Tracking
  section with justification and a follow-up task to remove it.

**Version**: 2.5.1 | **Ratified**: 2026-09-22 | **Last Amended**: 2026-10-23
