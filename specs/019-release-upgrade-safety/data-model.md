# Data Model: Release Pipeline and Upgrade Safety

**Feature**: [spec.md](./spec.md) · **Research**: [research.md](./research.md)

This feature adds one table to the database. Everything else it stores lives outside the
product: in the git repository (version, floor, note fragments) and in GitHub releases
(notes, snapshots, image references).

## 1. `platform.database_version` (new schema, new table)

Owned by the migration infrastructure in Shared.Infrastructure (RL-07), through
`PlatformDbContext` and its own history table `platform.__ef_migrations_history`. Migration
`Initial` creates the schema and the table. Additive changes only, forever.

| Column | Type | Null | Meaning |
|---|---|---|---|
| `id` | `smallint` | no | Always `1`. `CHECK (id = 1)`, primary key: one row per database. |
| `recorded_version` | `text` | no | Highest release version that has completed migrating this database (FR-027). Full SemVer string, e.g. `0.2.1`, `0.3.0-dev`, `0.3.0-rc.1`. Never lowered. |
| `recorded_at` | `timestamptz` | no | When `recorded_version` last changed. |
| `upgrading_to` | `text` | yes | Set before the first module migration of an upgrade to a newer major.minor; cleared when all modules have migrated (FR-030). Non-null after a run that stopped halfway. |
| `upgrading_since` | `timestamptz` | yes | When `upgrading_to` was set. |

- **No row** means either a fresh database or a database written before 0.1.0; the module
  history tables tell them apart (§2). Once written, the row is never deleted.
- Writes happen only in `InstallationGate`, under the advisory lock derived from `"platform"`,
  so several instances starting together agree (spec edge case "several instances").
- Not site-owned: no `SiteId`, no query filter (Principle V applies to site data only). Listed
  in `SiteFilterCoverageTests`' exemptions with that reason.
- Excluded from the snapshot's record comparison (contracts/snapshot.md §3): it changes on
  every upgrade by design.

### Version values

`ApplicationVersion` parses the build's informational version (`0.2.0+abc123`, `0.3.0-dev`)
into `SemanticVersion(major, minor, patch, preRelease)`. Ordering follows SemVer 2.0 §11
(pre-release sorts below its release; build metadata ignored). `QbUpgradeFloor` is a plain
`major.minor.patch`.

| Property (Directory.Build.props) | Example | Used for |
|---|---|---|
| `VersionPrefix` | `0.2.0` | the next release; the tag must equal it (RL-01) |
| `VersionSuffix` | `dev` (empty in releases) | marks non-release builds |
| `QbUpgradeFloor` | `0.0.0` throughout 0.x; `0.N.0` from 1.0.0 | oldest release this build upgrades from (FR-031) |

## 2. The compatibility decision

Inputs: running version `V`, floor `F`, the row (`R` = `recorded_version`, `M` =
`upgrading_to`), and whether any module migration history table has rows (`H`). "Newer"
compares **major.minor** only (clarification Q1); pre-release labels count as their
major.minor (`0.3.0-dev` is minor 0.3).

Evaluated top to bottom; the first matching row wins.

| # | Condition | Outcome | Writes |
|---|---|---|---|
| 1 | no row, `H` false | **Fresh**: pass | before migrations: insert `R = 0.0.0`, `M = V`; after: `R = V`, `M` null |
| 2 | no row, `H` true, `F` = `0.0.0` | **Pre-release**: pass (FR-030) | as rule 1 |
| 3 | no row, `H` true, `F` > `0.0.0` | **Refuse: too old** — "upgrade through 0.{last} first" | none |
| 4 | `M` set and `M`.major.minor > `V`.major.minor | **Refuse: newer** (an upgrade to `M` started) | none |
| 5 | `M` set and `M`.major.minor = `V`.major.minor | **Resume** an interrupted install or upgrade: pass | after: `R = max(R, V)`, `M` null |
| 6 | `R`.major.minor > `V`.major.minor | **Refuse: newer** | none |
| 7 | `R` < `F` | **Refuse: too old** — "upgrade through `F` first" | none |
| 8 | `V`.major.minor > `R`.major.minor | **Upgrade**: pass | before: `M = V`; after: `R = V`, `M` null |
| 9 | otherwise (same major.minor) | **Same or patch**: pass | after: `R = max(R, V)` only if it changes |

`R = 0.0.0` means "no release has completed migrating this database yet" (fresh or
pre-release). Rule 5 comes before rule 7 so that an interrupted first install is resumed rather
than judged too old. Rule 9 covers both "1.4.2 on a database at 1.4.3" (no change to `R`) and a
restart of the same version (no writes at all: SC-007). A `M` older than `V`'s major.minor
cannot occur (rule 8 only writes `M = V`); if found, it is treated like rule 8.

### Outcomes as the operator sees them

| Outcome | Readiness `state` | Readiness `detail` (anonymous) | Critical log (once) | `migrate` exit |
|---|---|---|---|---|
| Refuse: newer | `Incompatible` | `This database was upgraded by a newer QuickBuilder release. Start that release, or restore the backup taken before the upgrade. See the logs.` | `Database was migrated by QuickBuilder {R or M}; this is {V}, which cannot run on it. Start {R or M} or newer, or restore the backup taken before upgrading: {guide link}#rolling-back` | 3 |
| Refuse: too old | `Incompatible` | `This database is too old for this release to upgrade directly. See the logs.` | `Database was last migrated by QuickBuilder {R or "a pre-release build"}; {V} upgrades from {F} or newer. Upgrade to {F} first, then to {V}: {guide link}#upgrade-path` | 3 |
| Pass | as today (`Migrating` → `Ready`, or `MigrationsPending`) | as today | Information: `Database at {R}; upgrading to {V}` or `Database at {V}` | 0 |

`Incompatible` is a new `StartupPhase` value; it is served only by the refusal host (RL-08).

## 3. Snapshot bundle (release assets)

One per available release, attached to its GitHub release (RL-14). Format in
[contracts/snapshot.md](./contracts/snapshot.md).

| Asset | Content |
|---|---|
| `quickbuilder-<V>-snapshot.dump` | `pg_dump --format=custom --no-owner --no-acl` of the whole database |
| `quickbuilder-<V>-media.tar.gz` | the media volume's contents |
| `quickbuilder-<V>-expectations.json` | what must hold after any later release upgrades this snapshot |

Relationships: snapshot(N) = upgrade(snapshot(N-1)) + journey(N). The chain starts at the
pre-release baseline (RL-15).

## 4. Content journey: what a snapshot must contain

Each release's journey creates one new site `release-<V>` (host `release-<V-with-dashes>.example.test`)
containing at least one of each, all through the public API (FR-011):

| Kind | Since | Notes |
|---|---|---|
| Site with two host names, one disabled site | 009 | |
| Members: one Administrator, Designer, Editor (site-scoped) | 002 | passwords generated, kept in expectations |
| Pages: home, nested child, draft-only page, page with unpublished draft changes | 010–012 | |
| Every block type, incl. rich text with every 035 format | 011, 035 | |
| Shared component, used on two pages | 010, 012 | |
| Published design tokens incl. menu style | 011, 024 | |
| Media: image with variants, a document file | 003, 013 | |
| Collection with every field type; items published, draft, scheduled | 014, 015 | schedules at `2099-01-01` or later, never reached by a gate (snapshot.md §3) |
| Item template and a collection list with paging and filters | 016 | |
| SEO settings, redirects, canonical addresses, structured data | 017 | |
| Menu with nested entries, used by a Navigation block | 024 | |
| Form with every field type; submissions incl. one in spam | 018 | |
| Rolled-back page version | 012 | keeps an old published version in history |

A feature that adds stored data adds a row here and a step to the journey (contributing.md).
Nothing in the journey may depend on the date it runs (snapshot.md §3).

## 4a. Visible-change allow-list

File `docs/releases/unreleased/<issue>-visible-changes.json`, format in
[contracts/snapshot.md](./contracts/snapshot.md) §3: `issue`, `reason`, `pages[]` of
`{host, path}` (`*` host, `/*` path suffix). Lives and is archived with the fragments (§5);
consumed by the upgrade gate and by `qb-release notes`.

## 5. Release note fragment

Markdown file `docs/releases/unreleased/<issue>-<slug>.md`. Format in
[contracts/release-notes.md](./contracts/release-notes.md) §2.

| Key | Type | Required | Meaning |
|---|---|---|---|
| `issue` | integer | yes | GitHub issue |
| `kind` | `added` \| `changed` \| `fixed` \| `security` | yes | grouping |
| `breaking` | boolean | yes | true only with a new major (FR-034) |
| `config.added` / `.changed` / `.removed` | list of keys | no | checked against docs/configuration.md |
| `background` | list of text | no | background rewrites after upgrade |
| `caches` | list of text | no | caches that refill |
| `manual` | list of text | no | steps the operator must take |
| body | markdown | yes | one to five sentences for the highlights |

## 6. Release lifecycle

States of a GitHub release for tag `vX.Y.Z` (RL-05):

```text
tag pushed ──► prepare ──fail──► (no release object; run log only)
                 │
                 ▼
            build, manifest, smoke, arm64-boot, upgrade
                 │                       │
             all pass                 any fail
                 ▼                       ▼
            CANDIDATE (draft)        FAILED (draft, banner, logs kept)
                 │                       │
        maintainer approves      re-run the workflow, or delete the tag
                 ▼
            AVAILABLE (published; X.Y.Z, X.Y, X, latest tags)
```

- A failed candidate never becomes available; fixing it means a new commit and a new tag (or
  the same tag deleted and pushed again before it was ever available — FR-005 permits it,
  because the exact tag was never set).
- Pre-release tags reach AVAILABLE as GitHub pre-releases with only their exact tag.
