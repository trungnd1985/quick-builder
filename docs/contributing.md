# Contributing to QuickBuilder

How to run it, how to add a feature, how to add a module. The `Sites` module is the worked
example throughout — when in doubt, open it and copy what it does.

## Run it locally

**The short way:** `./qb.ps1` builds, starts and opens the admin, and `./qb.ps1 dev` runs
the backend and the hot-reloading admin app together (see the [README](../README.md)). The
steps it automates follow.

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
# 1. The database, published on 127.0.0.1:5432 for the host. If the app container is
#    running, stop it: the admin dev server proxies to port 8080, and so would reach it.
docker compose stop app
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d db

# 2. The backend, on the port the admin dev server proxies to
ConnectionStrings__Default="Host=localhost;Port=5432;Database=quickbuilder;Username=quickbuilder;Password=change-me-before-real-use" \
  dotnet run --project src/QuickBuilder.Host -- --urls http://127.0.0.1:8080

# 3. The admin app, hot-reloading, at http://localhost:5173/admin/ (proxies /api to 8080)
cd src/admin && npm install && npm run dev
```

The backend runs its migrations on start. Back to the container afterwards with
`docker compose up -d` (it rebuilds nothing; `docker compose up -d --build` does).

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
| **Administrative** | `/api`, `/admin`, `/health` | **No.** Must work on an installation with zero sites. Where an operation concerns one site, the request names it explicitly — never inferred from the Host header. When `Hosting:AdminHostNames` is set, `/api` and `/admin` answer only on those hosts; elsewhere they look like a missing page (009). `/health` answers everywhere. |
| **Public** | everything else | **Yes.** Resolved from the Host header by middleware before any handler runs, to **enabled** sites only. An unresolvable host, or a disabled site's, is rejected identically (FR-014, 009 S-03). |

The reserved prefixes live in `src/QuickBuilder.Host/Routing/RoutePrefixes.cs` and are
matched ahead of the public catch-all. If you add a surface-level path, add it there rather
than relying on registration order.

**Consequence for handlers**: never read the Host header. Read `ISiteContext` for the site id,
and `ICurrentSite` (Sites contracts) for its details — name, default language, and the
`BaseUrl` to build absolute addresses from. On the admin surface both are empty, and that is a
normal state, not an error; there, read a named site with `ISiteDirectory.FindByIdAsync`.
Nothing caches sites across requests, so a change is visible everywhere at the next request
([site context contract](../specs/009-sites-hostname-resolution/contracts/site-context.md)).

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
- **A new provider** implements `IFileStorage` and `IFileStorageHealthCheck`, validates its
  settings at startup, and derives a test class from `FileStorageConformanceTests`. It
  passes that suite unchanged before it ships, as Local, S3 and Azure Blob do (013). A
  provider reports a service it cannot reach as an `IOException`, never an SDK type, so
  consumers stay provider-agnostic, and it never puts a secret in a message or a log.

## Image processing

Images are measured and resized with libvips through NetVips, only in
`QuickBuilder.Media.Imaging` (an architecture test keeps it there). It decodes files people
upload, so it is security-sensitive:

- **Five loaders only** (JPEG, PNG, GIF, WebP, HEIF/AVIF); every other loader and every
  untrusted operation stays blocked. `ImagingRuntime` applies this before any image is
  opened: resolve it, or a type that takes it, before using NetVips anywhere new.
- **Headers before pixels**: dimensions are read from the header and checked against
  `Media:MaxImagePixels` before anything is decoded.
- **Keep the natives current**: `NetVips.Native.*` is pinned in `Directory.Packages.props`.
  When libvips or one of its codecs (libjpeg-turbo, libpng, libwebp, libheif, dav1d,
  libaom) publishes a security release, update all four native packages together, rebuild
  the image, and run the variant golden tests (`VariantEncodingGoldenTests`): their byte
  ceilings catch an encoder whose output changed.

## Adding a module with site-owned data

Every record that belongs to one site is isolated from every other site, and the build
checks that you did it (009 FR-024, FR-025). Four obligations, detailed in
[the site context contract](../specs/009-sites-hostname-resolution/contracts/site-context.md#6-obligations-of-a-module-that-stores-site-owned-data):

1. **Mark the record.** The entity implements `ISiteOwned` and your `DbContext` declares
   `HasQueryFilter(e => e.SiteId == CurrentSiteId)` itself — see `SiteQueryFilterGuidance`
   for why a generic helper silently filters against the wrong site.
   *Guard*: `SiteFilterCoverageTests` checks every module's model.
2. **Cross sites only through `AcrossAllSites(reason)`**, with a reason a reviewer can judge
   and a test that exercises the read. *Guard*: the banned-API analyser.
3. **Register an isolation probe.** Add a class implementing `ISiteIsolationProbe` under
   `tests/QuickBuilder.IntegrationTests/Isolation/Probes/` and list it in
   `IsolationProbes.All`. `MediaIsolationProbe` is the worked example, and
   `PagesIsolationProbe` shows endpoints that take a request body (pass it as
   `ProbeRequest.Body`, so the request fails on the site rather than on a missing body):
   - `SeedAsync` creates one of each record through your module's API;
   - `AdminRequests` returns every administrative request that takes a record id, built
     with the *other* site's ids — addressed ones must answer 404, listings must not
     contain them;
   - `CoveredEndpoints` names those endpoints as `METHOD pattern`, exactly as routing
     reports them;
   - `NeverIssuedPublicPath` and the seeded `PublicPaths` cover your public addresses.
   *Guard*: `IsolationCoverageTests` names any module with site-owned data and no probe,
   and any endpoint with `{siteId}` plus a record key (a value ending in `Id`, or a name in
   `RecordKeys`) that no probe covers.
4. **Pass the suite.** `SiteIsolationSuite` runs every probe across two sites, then with
   one of them disabled.

## Adding a block type

Page and component content follows the page schema contract,
[docs/contracts/page-schema.md](contracts/page-schema.md). A block type is defined once
(constitution Principle VI): the schema says what it is, and one folder in the admin app
says how the editor draws and edits it. A new kind of block is an **additive** change to
version 1, so no stored document needs migrating:

1. Add a `$defs` entry for it in `docs/contracts/page-schema/v1.schema.json`, with its
   props, their limits and a `default` for every optional prop. Its `properties.type` must
   be a `const`, and it must use only the keywords listed in §7; patterns must stay within
   what .NET and JavaScript read alike. *Guard*: `PageSchemaSubsetTests`.
2. Add it to the placement list it belongs in: `rootChild` (beside Sections, under the
   page) or `contentChild` (inside Sections and Containers). That is also how the editor
   learns where it may be dropped.
3. Add its row to the node-type table in `page-schema.md` §2. *Guard*:
   `PageSchemaContractTests` fails while the table and the schema disagree, or while the
   type has no valid and no invalid golden example.
4. Add golden examples under `docs/contracts/page-schema/examples/`: at least one valid
   document using it, and an invalid one for each rule its props can break, listed in
   `invalid/expected.json`. Both validators — C# and the editor's TypeScript — run them.
5. Add its editor folder, `src/admin/src/editor/blocks/<type>/`: a `definition.ts` (palette
   label and icon, the panel's groups and a control for every prop, and the values a new
   block starts with) and a `<Type>Block.vue` canvas component; register it in
   `blocks/registry.ts`; put its styles in `blocks/blocks.css` under `@layer qb-blocks`,
   using token custom properties and `@container page` queries, never media queries.
   *Guard*: `BlockRegistryCoverage.spec.ts` fails while the schema and the registry
   disagree about types or props.
6. Never give it a property that holds HTML, and never draw one with `v-html` (lint
   refuses it). Rich text is structured JSON (§3 rule 8).
7. Add its renderer (012): one class in
   `src/Modules/Rendering/QuickBuilder.Rendering/Rendering/Blocks/` implementing
   `IBlockRenderer`, registered in `RenderingModule`. Write through `HtmlWriter` only, so
   every value is encoded or checked; read props through `RenderContext`, which applies
   the schema `default`; put token-driven values in `context.Style(...)`, never in a
   `style` attribute. It must write the same markup as the canvas component
   (page-schema.md §9). *Guards*: `BlockRendererCoverageTests` fails while a type has no
   renderer; `RendererGoldenTests` renders every valid example and compares it with
   `tests/fixtures/rendering/golden/<name>.html` — run it with `QB_UPDATE_GOLDEN=1` to
   write the files, then review each one; `CanvasMarkupParity.spec.ts` compares the canvas
   with the same files.

8. **Bindable props** (016): if a prop can take an item's value, list it in the type's
   `bind` object in the schema (a `$ref` to `binding`), say which kind it is (`text`,
   `richText`, `image` or `link`) in both `BindingRules.cs` (`KindOf`) and
   `editor/bindings/bindingRules.ts` (`kindOf`), and read it in the renderer through
   `context.Bound(...)` or `context.BoundText(...)`, so a bound value is encoded exactly
   like the block's own. The panel then offers the binding control beside the prop, and
   `BindingRulesSchemaTests` fails while the two sides or the schema disagree. A prop that
   is not listed is never bound.

9. **Structured data** (017): a block that describes something search engines understand
   (a video, a list of items) adds a node to the page's JSON-LD graph from its renderer:
   `context.StructuredData.Add(new JsonObject { ["@type"] = "…", … })`, built from the
   values the block actually shows, after it has written its markup. `PageResponder`
   collects the nodes into one `<script type="application/ld+json">` with the page's
   breadcrumbs, and `JsonLdWriter` escapes every value, so never write JSON-LD yourself.
   Leave a node out rather than write an empty or guessed property. Add a case to
   `StructuredDataGoldenTests`. Content types that item pages map their fields to
   (`Article`, `Product`, `Event`) live in
   [contracts/structured-data.md](contracts/structured-data.md) and its `types.json`, not
   in a block.

Anything that would make a stored document invalid — removing or renaming a type or a prop,
making a prop required — is a **breaking** change: it needs `v2.schema.json`, a forward
migration of stored drafts and the other steps in §6, in the same pull request.

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
