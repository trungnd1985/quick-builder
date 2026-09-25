# Contributing to QuickBuilder

How to run it, how to add a feature, how to add a module. The `Sites` module is the worked
example throughout — when in doubt, open it and copy what it does.

## Run it locally

**As an operator would:**

```bash
docker compose up -d
docker compose logs app | grep "SETUP CODE"   # the one-time setup code
open http://127.0.0.1:8080/admin
```

The first visit shows the setup wizard. Enter the code and create the first administrator.
Other ways back in are in [operations](operations.md).

**For development**, with the backend on the host and the admin app hot-reloading:

```bash
# 1. A database
docker compose up -d db

# 2. The backend
ConnectionStrings__Default="Host=localhost;Port=5432;Database=quickbuilder;Username=quickbuilder;Password=change-me-before-real-use" \
  dotnet run --project src/QuickBuilder.Host

# 3. The admin app (proxies /api to the backend)
cd src/admin && npm install && npm run dev
```

Needs .NET 10 SDK, Node 24+, and a container runtime (Testcontainers uses it).

**Before you push:**

```bash
dotnet build                                   # warnings are errors
dotnet format --verify-no-changes              # CI runs this
dotnet test                                    # unit + integration + architecture
cd src/admin && npm run type-check && npm run lint && npm run test
```

## The administrative and public surfaces

The application has two request surfaces, and which one a path belongs to decides whether
it needs a site. This is FR-011, and getting it wrong is the easiest way to break a fresh
installation.

| Surface | Paths | Site required? |
|---|---|---|
| **Administrative** | `/api`, `/admin`, `/health` | **No.** Must work on an installation with zero sites. Where an operation concerns one site, the request names it explicitly — never inferred from the Host header. |
| **Public** | everything else | **Yes.** Resolved from the Host header by middleware before any handler runs. An unresolvable host is rejected (FR-014). |

The reserved prefixes live in `src/QuickBuilder.Host/Routing/RoutePrefixes.cs` and are
matched ahead of the public catch-all. If you add a surface-level path, add it there rather
than relying on registration order.

**Consequence for handlers**: never read the Host header. Read `ISiteContext`. On the admin
surface its `SiteId` is null, and that is a normal state, not an error.

## Add a feature to an existing module

One feature is one folder. Copy
`src/Modules/Sites/QuickBuilder.Sites/Features/ListSites/` and rename.

```text
Features/YourFeature/
├── YourFeatureController.cs   # [ApiController], route /api/v1/<module>/...  — thin
├── YourFeatureRequest.cs      # + response shape
├── YourFeatureCommand.cs      # or Query
├── YourFeatureHandler.cs      # ICommandHandler<,> / IQueryHandler<,>
├── YourFeatureValidator.cs    # IValidator<T>, optional
└── (tests alongside or mirrored under tests/)
```

Then register the handler and validator in `SitesModule.AddSitesModule()`. That is the only
file outside your folder you should touch.

**Every action declares its authorization.** Put exactly one of these on each controller
action. An action without one fails the architecture tests, and would be refused at
runtime anyway:

| Declaration | Use it for |
|---|---|
| `[RequireCapability(YourCapabilities.X)]` | Almost everything. For a site-scoped capability, name the site in the route as `{siteId}`. |
| `[RequireSignedIn]` | Your own account, and lists that span sites. The handler filters them through `IAccessEvaluator`. |
| `[AllowAnonymous]` | Only the short allow-list in the contract. Adding to it needs a spec. |

If the action needs a new capability, declare it in your module's capabilities class and
register it from `Add<Module>Module()` (see [Add a module](#add-a-module)). The rules,
including what a refused caller sees, are in
[the authorization contract](../specs/002-identity-setup/contracts/authorization.md).

**Rules worth knowing before you start** (the full set is in
[the module contract](../specs/001-foundation-skeleton/contracts/module-contract.md), and
the architecture tests enforce them):

- Controllers contain no business rules and no data access. They translate a request into
  one dispatcher call and the result into a response.
- Handlers use the module's `DbContext` directly. No repository wrapper.
- Reads use `AsNoTracking()` and project to a DTO. Never return an entity.
- Endpoints are `/api/v1/<module>/...`. The version segment is there from the first one.
- Return `Result`/`Result<T>` for expected failures with a machine-readable code. Throw only
  for genuinely unexpected failures.
- An entity with a `SiteId` implements `ISiteOwned`. Forgetting this means no query filter,
  which means it returns every site's rows.
- To read across sites, use `CrossSiteQuery.AcrossAllSites(reason)` and write a real reason.
  `IgnoreQueryFilters` is banned at compile time everywhere else — this is deliberate, and
  the ban is the only thing standing between a typo and a cross-tenant leak.

## Add a module

1. Create two projects under `src/Modules/<Module>/`:
   - `QuickBuilder.<Module>.Contracts` — interfaces, DTOs, integration events. References
     only `Shared.Kernel`. **No EF Core, no ASP.NET Core.**
   - `QuickBuilder.<Module>` — everything else, `internal` by default.
2. Add `<Module>Module.cs` with a single `Add<Module>Module()` entry point.
3. Add a `DbContext` with `HasDefaultSchema("<module>")`, its own migrations history table,
   and `modelBuilder.UseSnakeCaseColumns()`. Declare a site query filter per `ISiteOwned`
   entity referencing a `CurrentSiteId` property on the context — see
   `SiteQueryFilterGuidance` for why the obvious generic helper is wrong.
4. Register an `IModuleDatabaseProvider` so the host can migrate and health-check it
   without knowing anything else about the module.
5. **Declare what the module lets people do.** Capabilities are registered by the module
   that owns them; Identity enforces them without knowing the module exists. From
   `SitesCapabilities.cs` and `SitesModule.cs`:
   ```csharp
   internal static class SitesCapabilities
   {
       public const string Create = "sites.create";
       public const string View = "sites.view";

       public static IEnumerable<CapabilityDefinition> All =>
       [
           // Installation-wide: only Administrators hold it.
           new(Create, CapabilityScope.Installation, []),
           // Per site: these roles hold it on the sites they are granted.
           new(View, CapabilityScope.Site, [SystemRole.Designer, SystemRole.Editor]),
       ];
   }

   // in AddSitesModule()
   foreach (var capability in SitesCapabilities.All)
   {
       services.AddSingleton(capability);
   }
   ```
   Names are `<module>.<noun>.<verb>` and never change once shipped, because they are part
   of what a role means. Then give every action a declaration, as described in
   [Add a feature](#add-a-feature-to-an-existing-module).
6. Add **one line** to `src/QuickBuilder.Host/Program.cs`.
7. Generate the first migration:
   ```bash
   dotnet ef migrations add Initial<Module> \
     --project src/Modules/<Module>/QuickBuilder.<Module> \
     --output-dir Infrastructure/Migrations
   ```

Nothing on that list touches an existing module. If you find yourself editing one, stop —
that is the boundary telling you something.

## Publishing an integration event

Write it to your own module's outbox in the same `SaveChanges` as the state change. See
`CreateSiteHandler`. The atomicity is the point: a second transaction would create a window
where one landed and the other did not.

Handlers must be **idempotent** and **short** — they run inside the claim transaction,
holding a row lock for their duration. Full semantics in
[the event contract](../specs/001-foundation-skeleton/contracts/integration-events.md).

## Storing files

Modules store bytes only through `IFileStorage` (`QuickBuilder.Shared.Infrastructure.Storage`),
never through `File`, `FileStream` or `Directory` — an architecture test fails the build
otherwise. The rules are in
[the storage contract](../specs/003-media-storage/contracts/storage-provider.md); the short
version:

- **Keys are generated, never typed.** Build them from ids you control, never from a file
  name, slug or anything in a request. `StorageKey` refuses anything outside its grammar.
- **Site first.** A module's keys begin with the owning site's id, then a segment naming
  the module: `{siteId:N}/media/{fileId:N}`. That keeps every site's bytes one prefix to back
  up or remove (constitution Principle V).
- **Write before you record.** Writes are create-only and all-or-nothing. Store the bytes
  first, then insert the row that points to them, so nothing is ever listed or served
  half-written. Bytes left without a row are for a sweep to remove, as Media's orphan sweep
  does.
- **A new provider** (S3-compatible, Azure Blob) implements `IFileStorage` and
  `IFileStorageHealthCheck`, validates its settings at startup, and derives a test class
  from `FileStorageConformanceTests`. It passes that suite unchanged before it ships.

## Tests

| Project | What belongs there |
|---|---|
| `QuickBuilder.UnitTests` | Pure logic — results, validators, domain invariants |
| `QuickBuilder.IntegrationTests` | Anything touching HTTP or the database, via `QuickBuilderApp` + `DatabaseFixture` |
| `QuickBuilder.ArchitectureTests` | Boundary rules, plus a fixture proving each rule fires when broken |
| `QuickBuilder.SmokeTests` | The built image with the reference compose file |

In-memory database providers are not allowed (constitution Principle X). Integration tests
run against real PostgreSQL through Testcontainers, because the behaviours worth testing
here — `SKIP LOCKED`, advisory locks, `citext`, partial indexes — do not exist anywhere
else.

## Admin app

Every visible string goes through the lookup in `src/admin/src/i18n/`. The `no-raw-text` lint
rule fails the build otherwise — not to be tidy, but because text written into screens is
the single most expensive thing to undo once several modules have copied the pattern.

New screens meet [the accessibility baseline](./admin-accessibility-baseline.md). It is
eight items, all cheap while a screen is being written and awkward afterwards.

## Specs

One feature is one GitHub issue, one spec folder, one branch, one pull request. Feature IDs
are issue numbers. Work the Spec Kit order: specify → clarify → plan → tasks → analyze →
implement. Clarify and analyze are not optional.

The [constitution](../.specify/memory/constitution.md) wins over any plan. If your change
needs an exception to a MUST rule, record it in the plan's Complexity Tracking with a
follow-up issue that removes it.
