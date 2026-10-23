# Research: Release Pipeline and Upgrade Safety

**Feature**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Issue**: #19

Decisions are numbered RL-01 … RL-22 and cited from the plan, the data model and the
contracts. Each one gives the decision, why, and what was rejected.

## Starting point (what exists on `main` at 5f549ae)

- **Image**: one multi-stage [Dockerfile](../../Dockerfile) (Node builds the admin, the SDK
  publishes, Alpine `aspnet` runs). It already honours `TARGETARCH` when it prunes the libvips
  natives (013 MV-09), so it builds for arm64 unchanged. No version is passed in; every build
  reports assembly version 1.0.0.0.
- **CI** ([ci.yml](../../.github/workflows/ci.yml)): build, format, tests, coverage gate,
  `docker build`, the reference smoke journey (setup wizard → create page → publish → view
  → template) on the built image, and report-only performance. Nothing is pushed anywhere.
- **Reference compose** ([docker-compose.yml](../../docker-compose.yml)): `image:
  quickbuilder:latest` with a `build:` section, so `docker compose up` builds from source.
- **Migrations**: `ModuleMigrationRunner` (Shared.Infrastructure) waits for the database,
  then migrates each module under its own PostgreSQL advisory lock, before the host starts
  (Program.cs: `RunMigrationsAsync` runs before the pipeline and before hosted services). A
  failure leaves the process alive and readiness `Failed` (D-17). `Database:AutoMigrate=false`
  gives `MigrationsPending`; the `migrate` command applies them. No migration uses
  `suppressTransaction`, so every EF migration runs in its own transaction.
- **Upgrade tests**: ten `UpgradeFrom0NNTests` build the previous slice's database by
  migrating *down* from today's code, render, migrate up, and compare. They cannot see a
  shipped migration that was later edited, or data only an old binary wrote.
- **Schema versions**: page schema, menu, design tokens and collection schema are all at
  version 1; bindings carry `bindingVersion: 1`. Rendering lists what it renders
  (`PageRenderer.SupportedSchemaVersions`, `MenuModel.SupportedSchemaVersions`, guarded by
  `RendererSchemaVersionsTests` / `BlockRendererCoverageTests`). `ItemReads.ReadValues`
  rejects any version but the current one. `collections` and `design_token_sets` carry
  `CHECK (schema_version = 1)`. [page-schema.md](../../docs/contracts/page-schema.md) §6 says a
  breaking change needs "a forward migration that rewrites every stored document".
- **No releases**: no tags, no registry, no release notes. Operator-facing changes are
  recorded per feature in [operations.md](../../docs/operations.md) ("Upgrading from feature
  0NN") and in each plan's "Operator impact".

---

## RL-01 Version number: one source, stamped into the build

**Decision**: `Directory.Build.props` holds `<VersionPrefix>` (the *next* release, starting
at `0.1.0`) and `<VersionSuffix>dev</VersionSuffix>`. Local and pull-request builds therefore
report `0.1.0-dev`. The release pipeline builds with `-p:VersionSuffix=` (empty) and
`-p:SourceRevisionId=<sha>` passed as Docker build arguments (`QB_VERSION_SUFFIX`,
`QB_REVISION`), so the image reports exactly `0.1.0+<sha>` as its informational version. The
pipeline refuses a tag `vX.Y.Z[-pre]` that does not equal `VersionPrefix` (plus the tag's own
pre-release label). After a release, the next PR bumps `VersionPrefix`.

A second property, `<QbUpgradeFloor>`, holds the oldest release this build may upgrade from
(RL-08). Both are written into the Host assembly as `AssemblyMetadata` and read once at
startup by `ApplicationVersion` (Shared.Infrastructure).

**Why**: one number, visible in review; the image cannot claim a version its tag does not
have. `-dev` builds sort below the release they lead to (SemVer pre-release ordering), which
is what the startup check needs (RL-08).

**Rejected**: deriving the version from `git describe` (needs the full history in the Docker
context, and a PR build would report the last release's number); MinVer/Nerdbank
(new dependency for one string, Principle XI).

## RL-02 Registry and image names

**Decision**: GitHub Container Registry, public:
`ghcr.io/trungnd1985/quickbuilder`. Tags:

| Tag | When it is set | Moves? |
|---|---|---|
| `sha-<12 hex>` | on push of the candidate | never reused |
| `X.Y.Z` (and `X.Y.Z-rc.N`) | on approval | never (FR-005) |
| `X.Y`, `X`, `latest` | on approval of a non-pre-release | yes |

The image carries OCI labels `org.opencontainers.image.{source,version,revision,licenses}`;
`source` links the package to the repository. Making the package public is a one-time manual
step after the first push (quickstart §0).

**Why**: the issue and constitution ask for "a container registry"; GHCR needs no account for
pulls, no extra secret (the workflow's `GITHUB_TOKEN` with `packages: write` pushes), and sits
beside the source and releases. **Rejected**: Docker Hub (rate-limited anonymous pulls, an
extra credential); both at once (later, without changing the spec — assumption).

## RL-03 Multi-arch build: native runners, push by digest, merge

**Decision**: the release workflow builds each architecture on its own native runner
(`ubuntu-latest` for amd64, `ubuntu-24.04-arm` for arm64) with Buildx, pushes each by digest
only (no tag), then a `manifest` job joins the two digests into one multi-arch index with
`docker buildx imagetools create`, tagged only `sha-<12>`. The index digest is the identity of
the candidate from then on: both gates pull `…@sha256:<index>` and approval re-tags that same
digest (RL-06). The Dockerfile is unchanged except for the version arguments and labels
(RL-01, RL-02).

**Why**: QEMU emulation runs `npm ci`, Vite and `dotnet publish` five to ten times slower and
has flaked on .NET; native arm64 runners are free for public repositories and keep tag →
candidate under SC-011's 60 minutes. Building on both natives also proves the arm64 image
starts (FR-010, RL-11) on real hardware.

**Rejected**: one `buildx --platform linux/amd64,linux/arm64` under QEMU (slow); cross-
compiling with `--platform=$BUILDPLATFORM` and `-r linux-musl-arm64` (works for .NET, but the
libvips native selection and the Node stage would need separate handling for a gain the
native runner already gives).

## RL-04 Release workflow shape

**Decision**: a new [`.github/workflows/release.yml`](../../.github/workflows/release.yml),
triggered only by pushing a tag matching `v*.*.*` (FR-002). Jobs, in order:

1. **prepare** (amd64): checks the tag against `VersionPrefix` (RL-01); runs the release
   checks — frozen migrations (RL-12), patch rules (RL-13), notes assembly (RL-16) — with the
   previous available release found from the GitHub releases list; computes the upgrade
   sources (RL-09). Fails fast before anything is built.
2. **build** (matrix amd64/arm64, native) → **manifest** (RL-03).
3. **smoke** (amd64): the reference journey against the pulled candidate (RL-10).
4. **arm64-boot** (arm64): pull the candidate, compose up, wait for ready, check setup mode
   (RL-11).
5. **upgrade** (amd64, one matrix leg per upgrade source): restore, boot, compare, rollback
   drill; the leg from the previous release also produces the new snapshot (RL-09, RL-14,
   RL-15).
6. **candidate**: creates or updates a **draft** GitHub release for the tag with the assembled
   notes, the candidate digest, the snapshot assets and the release compose file; on any
   failure above, the draft says "Not available: <gate> failed" and links the run, whose
   artifacts keep both gates' logs (FR-009).
7. **promote** — `environment: release` with the maintainer as required reviewer (RL-06).

**Why**: one file, one tag, every gate against the published bytes. Splitting `prepare`
first means a missing note or an edited migration costs a minute, not a build.

## RL-05 Release states

**Decision**: a release is a GitHub release object for tag `vX.Y.Z`. *Candidate* = draft
release with all gates green; *failed* = draft release whose body starts with the failure
banner; *available* = published (non-draft) release. Pre-releases (`-rc.N`) are published
with GitHub's pre-release flag and never become "latest" (FR-004). The previous available
release is the highest published, non-pre-release tag below the new version.

**Why**: GitHub releases already hold notes and assets and have the draft/published states
the spec needs; no store of our own. Operators find versions where they find the source.

## RL-06 Approval: a protected environment

**Decision** (clarification Q2): the `promote` job runs in the GitHub environment `release`,
configured with the maintainer as required reviewer and restricted to `v*` tags. It depends
on every gate, so it cannot be approved while one has failed (FR-009a). It:

1. refuses if tag `X.Y.Z` already exists on a *different* digest (FR-005; re-running promote
   for the same digest is a no-op);
2. tags the candidate index digest `X.Y.Z` and, for non-pre-releases, `X.Y`, `X` and
   `latest`, with `docker buildx imagetools create` — a registry-side copy of the manifest,
   no rebuild or re-push of layers;
3. replaces the candidate digest placeholder in the notes with the final references, and
   publishes the draft release (`make_latest` only for non-pre-releases).

**Why**: GitHub's environment approval is the built-in "wait for a person" with an audit
trail, needing no bot or token. **Rejected**: a manual `workflow_dispatch` "promote" workflow
(the operator of the click must type the right digest; easy to promote the wrong one).

## RL-07 The installation version record

**Decision**: the record lives in a new schema **`platform`**, owned by the migration
infrastructure in Shared.Infrastructure, with its own `PlatformDbContext`, EF migrations and
history table `platform."__EFMigrationsHistory"` (named as the modules name theirs). One table, `platform.database_version`, one row
(data-model §1). The runner migrates `platform` first, then reads and checks the record, then
migrates the modules (RL-08). Changes to `platform` are additive only, forever, so an older
release can always read the row a newer one wrote.

**Why**: the check must run *before* any module migrates and must also write the "upgrading"
mark before the first module migration (FR-030). No module can own that without the runner
migrating it out of turn, and the record is about the database as a whole, not a module's
data. The migration runner and its advisory locks already live in Shared.Infrastructure
(001 D-04); this is the runner's own bookkeeping. Constitution Principle I lists what
Shared.Infrastructure holds and that each module owns its schema; the plan proposes a PATCH
clarification (2.5.0 → 2.5.1) naming the migration runner and its `platform` record there
(plan, Constitution Check). Schema changes still go through EF migrations only.

**Rejected**: a row in Identity's schema (Identity would have to migrate before the check
could read it, and a fresh database has no Identity schema to mark); `CREATE TABLE IF NOT
EXISTS` from code (constitution: EF migrations only); a file on the media volume (not backed
up with the database, which is what the record describes).

## RL-08 The startup compatibility check

**Decision** (clarification Q1, FR-027 – FR-031): `InstallationGate` runs inside
`ModuleMigrationRunner.RunAsync`, after the database is reachable and `platform` is migrated,
**before** the `AutoMigrate` decision, under the advisory lock `platform`. It reads the record
and the running build's version `V` and floor `F` (RL-01) and decides with the table in
data-model §2:

- fresh database (no record, no module history tables) → pass;
- pre-release database (no record, module tables present) → pass while `F` is `0.0.0`, that
  is throughout 0.x (FR-030); from 1.0.0 on, `F` is a real release and a database with no
  record is older than any release, so it is refused with "upgrade through 0.N first";
- recorded `R` or upgrade mark `M` from a newer **major.minor** than `V` → refuse ("newer");
- `R` below `F` → refuse ("too old", naming `F`);
- otherwise pass. Before the first module migration, if `V`'s major.minor is above `R`'s, the
  mark `upgrading_to = V` is written; after all modules migrate, `recorded = max(R, V)` and the
  mark is cleared in one statement. A patch (same major.minor, either direction) never writes
  the mark and never lowers `recorded`.

**Refusal** is decided *before* the host is built (`InstallationPreflight`), so no hosted
service (outbox, schedules, backfills, sweeps) is even registered against a database it does not
understand. Instead of exiting, the same app serves a **refusal pipeline** (one terminal
middleware; a separate host would not be the app `WebApplicationFactory` tests start) that
answers `/health/live` 200, `/health/ready` 503 with `state: "Incompatible"` and a detail that
gives the reason and the next step without version numbers (Principle IX: probes are
anonymous), and 503 to everything else. The full message with both versions and the link to
the guide is logged at Critical once at startup (FR-029, data-model §2 messages). The
`migrate` command applies the same check and exits with code 3.

**Why**: forward-only migrations make an older binary on a newer database the most likely way
to corrupt data; a refusal before *any* module code runs is the only safe one. Staying alive
keeps the established D-17 behaviour (a reason on the probe instead of a crash loop that
hides it behind restart noise), while the separate host guarantees nothing else runs.

**Rejected**: exiting non-zero (crash loop under `restart: unless-stopped`; the probe the spec
asks for would not answer); the existing `Failed` state (requests and hosted services still
run); comparing EF migration history (`GetAppliedMigrations` minus known ones) instead of
versions — it detects "newer" but cannot name a release to the operator or tell a safe patch
from an unsafe minor.

## RL-09 Which snapshots each upgrade gate starts from

**Decision** (clarification Q3, FR-008): `qb-release upgrade-sources` computes, from the
list of available releases:

1. the previous available release;
2. the first release of `V`'s major (`X.0.0`, or `0.1.0` while in 0.x — clarification Q5);
3. when `V` starts a major, the last minor of the previous major (its highest patch).

Duplicates collapse. For the first release, 0.1.0, the list is empty and the single source is
the **pre-release baseline** (RL-15). Each source is one leg of the `upgrade` matrix.

## RL-10 Smoke gate on the published image

**Decision**: the reference compose file stops building. `docker-compose.yml` names
`image: ${QUICKBUILDER_IMAGE:-ghcr.io/trungnd1985/quickbuilder:0}` with no `build:`; a new
`docker-compose.build.yml` override adds `build:` and `image: quickbuilder:local` for
developers and pull-request CI. `ComposeDeployment` gains a mode: when `QB_SMOKE_IMAGE` is
set it pulls that reference (`docker compose pull`) instead of building, and records the
digest it ran in the test output. The release `smoke` job sets it to the candidate index
digest. The journey itself (`ReferenceDeploymentSmokeTests`) is unchanged; it already covers
setup wizard → create page → publish → view (FR-007).

Each release also attaches a `docker-compose.yml` whose image line is pinned to the exact
version, generated from the repository file; the operations guide tells operators to use
that, so an install never upgrades by surprise.

**Why**: the gate must test what operators pull, by digest, not a local rebuild. Keeping one
journey class for PR and release means a broken journey cannot pass in one place and fail in
the other. **Rejected**: keeping `build:` in the reference file (operators would build from
source and never see a released image).

## RL-11 arm64 in the pipeline

**Decision**: gates run on amd64 (FR-010). The `arm64-boot` job on the native arm64 runner
pulls the candidate (Docker resolves the arm64 entry of the index), starts the reference
compose, waits for `/health/ready`, checks `GET /api/v1/identity/status` reports setup mode,
and uploads compose logs. About three minutes.

**Why**: proves the arm64 image starts, migrates and serves (SC-004) without doubling the
long gates. **Rejected**: full smoke and upgrade on both (doubles runtime for code paths that
are architecture-independent except libvips, which `VariantEncodingGoldenTests` covers).

## RL-12 Shipped migrations are frozen

**Decision** (FR-017): `qb-release migrations check --since <ref>` lists files under
`src/**/Infrastructure/Migrations/` that differ between `<ref>` and `HEAD`. Any
**modified, renamed or deleted** migration file (`<ts>_<Name>.cs` or `.Designer.cs`) fails,
naming it; added files and the `*ModelSnapshot.cs` files are allowed. In CI the ref is the
latest available release tag (`ci.yml` job `release-checks`, checkout with `fetch-depth: 0`);
before the first release there is no tag and the check passes with a notice. The release
`prepare` job runs the same command. The `platform` migrations are included.

**Why**: git already holds the shipped bytes; a manifest of hashes would be a second copy to
keep in step. Comparing with the last *release* (not `main`) is right: migrations may still be
reworked between releases, before any operator has run them.

## RL-13 Patch releases change no stored data

**Decision** (FR-034a): for a patch tag, `qb-release patch check --since vX.Y.0` fails when
the range adds any migration file, any `vN.schema.json` under `docs/contracts/`, or changes a
`CurrentVersion` constant (`PageSchema`, `MenuSchema`, `DesignTokens`, `CollectionSchema`)
or the binding version constant. It runs in `prepare`.

**Why**: clarification Q1 lets an older patch run on a newer patch's database; that is only
safe because patches do not touch the database shape or the document formats. A mechanical
check is what makes the rule true.

## RL-14 Snapshots: accumulated, from the release's own image

**Decision** (FR-011 – FR-014): a snapshot is made **inside the upgrade leg from the previous
release**, after that leg has passed:

1. restore snapshot(N-1) (or the baseline, RL-15) into a fresh compose project;
2. start the candidate N; verify (RL-17);
3. run the **content journey** of N against it: it signs in as the journey administrator and
   creates a new site `release-<N>` with one of every kind of stored data (data-model §4);
4. capture the expectations (rendered pages, record counts) of the *whole* database;
5. `pg_dump --format=custom` the database and `tar` the media volume;
6. attach `quickbuilder-<N>-snapshot.dump`, `-media.tar.gz` and `-expectations.json` to the
   draft release.

So snapshot(N) contains everything snapshot(N-1) contained, upgraded by N's migrations but
with documents still in whatever schema version they were stored under (FR-023a), plus N's own
data. Content stored under every older schema version therefore stays in the chain (FR-014),
and the upgrade gate renders all of it, not only the newest.

The journey is code (`tests/QuickBuilder.SmokeTests/Release/ContentJourney.cs`), driven
through the public API like the smoke journey. A feature that adds a kind of stored data adds
a step to it (contributing.md, RL-21). Snapshot data is invented: fixed fake names,
`example.test` addresses, passwords generated per run and stored only in the expectations
file as the sign-in check needs them, no real secret (FR-013). The `POSTGRES_PASSWORD` of the
gate's compose project is not in the dump.

**Why**: a snapshot written by the real old binary is the only thing that catches a migration
edited after shipping or a value only old code wrote — the gap in today's tests. Accumulating
keeps old formats alive without a separate fixture per version. **Rejected**: dumping a fresh
database after only N's journey (loses older schema versions); keeping snapshots in git
(binary churn in every clone; spec assumption chose release assets).

## RL-15 The pre-release baseline for 0.1.0

**Decision**: the first release has no previous snapshot. Its `upgrade` leg uses the
**baseline**: the image built from the merge base of this feature on `main` (commit
`5f549ae`, recorded as the tag `pre-0.1.0-baseline`), started with the reference compose
file, set up, and filled by **0.1.0's** content journey through the API (the API that journey
uses exists unchanged at the baseline). Then it is dumped and treated exactly like a previous
release's snapshot. The leg builds the baseline image itself (`docker build` at that ref).

**Why**: this is "a database from the last pre-release state on main, the way the existing
upgrade tests build theirs" (spec assumption) made stronger: written by the old binary, not
reconstructed by migrating today's code down. It also proves pre-release databases pass the
check (FR-030). The baseline leg disappears from the second release on.

## RL-16 Release notes: template, fragments, assembly

**Decision** (FR-032 – FR-034):

- **Template**: `docs/releases/TEMPLATE.md` with the sections of contracts/release-notes.md.
- **Fragments**: each pull request with operator-visible change adds
  `docs/releases/unreleased/<issue>-<slug>.md`: front matter (`issue`, `kind`
  added|changed|fixed|security, `breaking`, `config` added/changed/removed keys,
  `background`, `caches`, `manual`) and a short body. This replaces writing a new "Upgrading
  from feature 0NN" section in operations.md; the plan's "Operator impact" becomes the
  fragment.
- **Facts the tool derives itself** (not trusted to fragments): migrations added per module
  since the previous release (git, RL-12), content schema versions raised (RL-13's
  detection), the candidate digests, the supported upgrade path (RL-09 and the floor).
- **Assembly**: `qb-release notes --version V --previous P` merges fragments and facts into
  `docs/releases/<V>.md` (written into the release, not committed by the pipeline) and fails
  when: a required section is missing; a fragment is malformed; any fragment says
  `breaking: true` and `V` is not a new major (1.0.0 counts; 0.x minors do not — FR-001a);
  config keys in fragments disagree with the keys added or removed in
  `docs/configuration.md` between the two refs. Every empty list renders "None."
- **After approval** the maintainer's next PR moves the used fragments to
  `docs/releases/<V>/` together with the `VersionPrefix` bump (one command, `qb-release
  archive V`).
- **CI guard**: `release-checks` fails a PR that adds a migration, changes
  `docs/configuration.md` or a contract schema, without adding a fragment.

**Why**: notes "generated per release from the changes it contains" need structured inputs;
people know the meaning, tools know the facts. Fragments avoid merge conflicts in one shared
file. **Rejected**: generating from commit messages or PR titles (no place for config keys or
manual steps); GitHub's auto-generated notes (lists PRs, not operator impact).

## RL-17 What "unchanged" means in the upgrade gate

**Decision** (FR-012): the expectations file (contracts/snapshot.md) records, before the
upgrade:

- **pages**: for every published page and item page of every site, by host name and path:
  status code and the *visible fingerprint* — the text content of `<body>` with whitespace
  collapsed, plus every `href`, `src` and `alt` value, with the content hash in asset file
  names stripped (`blocks.<hash>.css` → `blocks.css`, under Rendering's assets prefix). Markup, class names and cache
  headers may change between releases; what visitors read and where links lead may not.
- **records**: row count per table per module schema, excluding the volatile tables listed in
  the contract (outbox, processed events, sessions, rate windows, migration history);
- **sign-in**: one member per role and their passwords.

After the upgrade, every page must have the same status and fingerprint, every counted table
at least as many rows (a migration may add derived rows, never lose any), every member must
sign in, and a draft of each kind must open through the admin API. Background work (backfills)
is allowed to run; pages are compared once readiness is reached, and again after background
work reports idle, so a backfill that broke rendering is caught. Differences are printed per
page as a text diff.

The visible form is computed by a small hand-written tokenizer (start tags, attributes, text,
entities) in `STESTS/Release/VisibleForm.cs`: it only ever reads the server's own
well-formed output, so no HTML parser package is added (Principle XI; none is referenced in
the solution today).

**Allowed visible changes** (FR-012a, analysis U3): a release that deliberately changes what
visitors read on existing pages adds a reviewed `docs/releases/unreleased/<issue>-visible-changes.json`
(snapshot.md §3). The gate accepts differences only on listed pages, fails on entries that
match nothing, and the notes list every entry. **Rejected**: a manual override at approval
(an unreviewed bypass of the one gate that protects content); deferring (the first wording
change would block a release with no way through).

**Time-dependent content** (analysis U1): the journey schedules only in the far future
(2099) and capture refuses pages that show the current date, so a snapshot restored months
later is unchanged by background schedules.

**Why**: comparing raw HTML breaks on every stylesheet change, which is legitimate (012
renders again after such upgrades); comparing only status codes misses lost content. The
existing `UpgradeFrom` tests compare body HTML because they run inside one release; between
releases visible content is the promise (US1 scenario 2).

## RL-18 Rollback drill

**Decision** (FR-036): the `upgrade` leg from the previous release ends with the rollback the
guide describes: stop the stack, drop and recreate the database volume, `pg_restore` the
pre-upgrade dump, restore the media tar, start the **previous** release's image, wait ready,
and compare against the pre-upgrade expectations. Then it does the wrong thing on purpose:
start the previous image against the *upgraded* database and assert the refusal (state
`Incompatible`, the log line, zero rows changed — SC-006).

**Why**: rollback guidance that is never run is a guess. The second half proves the check
from the operator's side of the mistake.

## RL-19 Content schema upgrades: chains in the owning module, applied on read

**Decision** (clarification Q4, FR-020 – FR-026):

- **Mechanism**: `SchemaUpgradeChain` in Shared.Infrastructure (`Schemas/`): an ordered list
  of `ISchemaUpgradeStep` (`From`, `Upgrade(JsonObject) → JsonObject`) for one contract, with
  `Read(JsonElement document, int storedVersion)` returning *current*, *upgraded* (a new
  document at the current version), *too new* or *failed*. Pure, no I/O, no module concepts.
- **Ownership**: each contract's chain lives in the module that stores it — Pages (page
  schema, used for pages and components; menu; bindings nested in page documents), Editor
  (design tokens), Cms (collection schema and item values). Today every chain is empty
  because every contract is at version 1.
- **Where it applies**: in the owning module's read paths, before the document leaves the
  module — `PublishedContentReader` (pages, components, menus to Rendering), `PageReads`,
  `ComponentReads`, `MenuWrites`' read, `PublishedDesignReader`, the Editor draft reads, and
  `ItemReads`/collection reads. Rendering and the admin only ever see current versions, so
  the renderer's `SupportedSchemaVersions` become "the current version" plus the guard.
- **Writing back**: never on read. A save or publish validates and stores the (already
  upgraded) document at the current version (FR-022); `PublishingWrites` stamps the current
  version when it publishes. Published history rows keep theirs (FR-023a); rolling back to
  one publishes it again through the chain.
- **Too new** (FR-026): the read reports it; Rendering treats it as an unrenderable document
  (012's existing "fails safely" path); the admin API answers `409 document_too_new` and
  refuses to save over it. Patches cannot create this (RL-13) and the startup check prevents
  it across minors (RL-08); this is defence in depth.
- **Failed** (FR-025): a step that throws or yields a document that fails validation is
  caught per document: logged as `schema_upgrade_failed` with contract, subject id and stored
  version only, and that page fails like any unrenderable page; nothing else is affected.
- **Guards** (FR-023, FR-024): `SchemaUpgradeCoverageTests` fails when, for any contract,
  `CurrentVersion > 1` and a step from some `v` to `v+1` is missing, or a step lacks golden
  files under `docs/contracts/<contract>/upgrades/v<v>-to-v<v+1>/` (pairs `*.in.json` /
  `*.out.json`), which `SchemaUpgradeGoldenTests` runs. Raising a version is documented as a
  procedure (contracts/schema-upgrades.md, contributing.md), including relaxing the
  `CHECK (schema_version = N)` constraints to the supported range.

The mechanism is proven now, with no real version 2, by unit tests over a test-only contract
with three versions, and integration tests that store a page, a component, a menu, a design
token set and an item at `CurrentVersion + 1` (too new) and with a step forced to fail
(through a test-registered chain), checking that only that page fails and the rest render.

**Why**: one upgrade place per contract, next to the validator and the data; the renderer and
the editor stay version-agnostic. Upgrading on read keeps untouched content working without a
bulk rewrite (clarification Q4). **Rejected**: making the renderer render every version
directly (every renderer and the TypeScript editor would grow per-version branches); bulk
rewrite at startup (rejected in Q4); building a real page-schema v2 now to exercise the path
(Principle XI: no breaking change is needed yet).

**Constitution and contract wording**: Principle VI says a breaking change needs "a forward
migration of stored data". An upgrade step applied on read and persisted on the next save or
publish *is* that migration, applied lazily; the PATCH clarification (RL-07's amendment)
says so explicitly. page-schema.md §6 item 3 and binding-expression.md's matching paragraph
change from "rewrites every stored document" to "an upgrade step from N-1, applied on read
and stored on the next save or publish".

## RL-20 Migrations: forward-only and safe to re-run, checked

**Decision** (FR-015, FR-016, FR-018):

- **Re-run** (SC-007): `MigrationRerunTests` restores the baseline snapshot into
  Testcontainers PostgreSQL, migrates, fingerprints every row with its `xmin` (statistics views are flushed late from PostgreSQL 15 on), records insert/update/delete
  counters, runs the runner again (both `RunAsync(force: false)` and the `migrate` command
  path) and asserts zero new writes outside `platform` and the history tables. The release
  `upgrade` leg repeats this on the real image (restart the app, compare counters).
- **Interrupted** (FR-018): each EF migration runs in its own transaction (Npgsql default), so
  a kill leaves the last completed migration applied and the next start resumes.
  `MigrationTransactionTests` (architecture) fails if any migration uses
  `suppressTransaction: true` without `IF NOT EXISTS`/`IF EXISTS` in its SQL. A test applies
  half of a module's pending migrations, "crashes" (disposes), then starts the runner and
  checks completion and the cleared upgrade mark.
- **Forward-only**: `Down` methods stay as EF generates them (tests use them to build old
  states) but no product path calls them; `MigrationRunnerSurfaceTests` asserts the runner
  and the `migrate` command never pass a target migration.
- **Background rewrites** (FR-019): the existing pattern (e.g. `ItemIndexBackfillService`:
  idempotent, after ready, batched) is the rule; the upgrade gate's second page comparison
  (RL-17) covers "sites render while they run".

## RL-21 Admin and operator surfaces

**Decision**:

- **Version in the admin** (FR-006): `GET /api/v1/identity/installation` (Identity; new
  capability `identity.installation.view`, Administrator only, installation scope) returns
  `{ version, revision, databaseVersion, upgradingTo }`. The admin shows it in Settings →
  **About this installation**, with a link to the release notes of that version. Identity
  owns installation-level concerns (setup); it reads the values through
  `IInstallationVersion` in Shared.Infrastructure.
- **Startup log** (FR-006): the first log line is `QuickBuilder <version> (<revision>)`;
  after the check, `Database at <R>; upgrading to <V>` or `Database at <V>`.
- **Operations guide** (FR-035): a new **Upgrading and rolling back** chapter: before (read
  every release's notes between yours and the target; back up the database with `pg_dump` and
  the media volume), upgrade (change `QUICKBUILDER_IMAGE` to the exact version; `docker compose
  up -d`; watch readiness and the version), after (background work, caches), rollback (stop,
  restore both backups, start the previous exact version, check), what the startup refusal
  means and what to do, "no backup means no rollback; data written since the upgrade is lost".
  The existing per-feature "Upgrading from feature 0NN" sections stay as history of the
  pre-release builds.
- **Contributing guide**: "Making a release" (maintainer runbook), "Adding stored data"
  (journey step + fragment), "Raising a schema version" (RL-19).

**Why**: version disclosure only to Administrators (Principle IX); the readiness probe tells
an operator what to do without advertising a version to the internet.

## RL-22 The release tool

**Decision**: one small .NET console project, `tools/QuickBuilder.Release` (assembly
`qb-release`), with commands `version check`, `upgrade-sources`, `migrations check`, `patch
check`, `notes`, `fragment check` and `archive`. It shells out to `git` and reads files; it
does not call GitHub (the workflow passes in the release list from `gh release list`). Unit
tests in `tests/QuickBuilder.UnitTests/Release/` cover version ordering, upgrade sources, the
floor, fragment parsing, assembly and every refusal, against temporary git repositories.
No new package: front matter is parsed by hand (a fixed key set), SemVer by a 60-line type.

**Why**: the rules (SemVer ordering, floors, upgrade sources, breaking-change detection) are
logic that must be right and should be unit-tested in the language the team tests in; bash in
YAML is where such rules rot. **Rejected**: bash scripts (untestable here); a GitHub Action
from the marketplace for notes (cannot express config-key cross-checks or the breaking rule);
YamlDotNet for front matter (a dependency for eight keys).

---

## Deferred (recorded, not built)

- Image signing (cosign) and SBOM / provenance attestations: cheap to add later to `build`,
  not required by the spec.
- Row-level checksums for journey tables (record comparison counts rows; a deleted row offset
  by an added one is not caught — analysis U4, accepted).
- A second registry (Docker Hub).
- A bulk rewrite of stored documents to retire old upgrade steps (clarification Q4).
- Zero-downtime rolling upgrades and automatic downgrade (spec FR-037, FR-038).
- Driving the editor in a browser during the gates (exception E-5, until #25).
