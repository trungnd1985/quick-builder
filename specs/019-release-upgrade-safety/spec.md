# Feature Specification: Release Pipeline and Upgrade Safety

**Feature Branch**: `019-release-upgrade-safety`

**Created**: 2026-10-23

**Status**: Draft

**Issue**: #19

**Input**: User description: "#19 Release pipeline and upgrade safety. Problem / goal: Operators must be able to upgrade by replacing the image without losing data or breaking sites. Every release upgrades cleanly from the previous one, and content stored under an older schema version still renders. In: multi-arch image build and publish to a registry, release notes template, smoke test that boots the published image and completes the setup wizard, upgrade test that restores a database snapshot from the previous release and boots the new image, forward-only idempotent migrations, content schemaVersion migration on read or on publish, version compatibility checks at startup, upgrade notes generated per release, rollback guidance. Out: automatic downgrade, zero-downtime rolling upgrades."

## Context

Features 001 to 035 built the product one slice at a time. Each slice already proves its own
upgrade: a test builds the previous slice's database by migrating down, starts the
application against it and checks that pages render the same. Every pull request builds the
container image and runs the reference deployment journey (setup, create page, publish, view)
against it.

None of that is a release. Today there is no version number an operator can see, no image in
a registry, no record of what changed between two versions and no stored database from a
version an operator actually ran. The upgrade tests rebuild the past from today's code, so
they cannot catch a migration that was edited after it shipped, or data that only an old
version wrote. Nothing stops an operator from starting an old image against a database that a
newer version has already changed, or from skipping several major versions at once.

This feature adds:

- a **release**: a numbered version, built for both common server architectures, published
  to a container registry, with release notes and upgrade notes;
- **release gates** that run against the published image itself: a smoke test that boots it
  and completes the setup wizard, and an upgrade test that restores a database captured from
  the previous release and starts the new image on it;
- a **database snapshot** captured from each release and kept as the starting point for the
  next release's upgrade test;
- **migration rules** that are checked, not only written down: migrations only move forward,
  a shipped migration never changes, and running them twice changes nothing;
- **content schema upgrades**: content stored under an older `schemaVersion` still renders,
  and is brought up to the current version when it is next saved or published;
- **startup compatibility checks** that refuse to run a version against a database it cannot
  safely use, with a message that tells the operator what to do;
- **rollback guidance** that tells an operator how to return to the previous version from a
  backup, since downgrade is not automatic.

Constitution Principle II applies because upgrading must stay "replace the image", with
migrations that are forward-only and safe to re-run. Principle III is the heart of this
feature: Semantic Versioning, a snapshot upgrade test for every release, and breaking changes
only in a major version, listed in upgrade notes. Principle VI applies because every stored
document carries a `schemaVersion` and the renderer must render every version still in the
database. Principle X applies because the release pipeline boots the published image and runs
the smoke test. The Technology Constraints fix the distribution: multi-arch images built by
GitHub Actions and published to a container registry, with release notes and upgrade notes.

## Clarifications

### Session 2026-10-23

- Q: When an operator starts an older release against a database that a newer release has
  already migrated, when should the older release be allowed to run? → **A: only within the
  same minor version**; patch releases add no migrations and raise no schema versions, so
  1.4.2 may run against a database migrated by 1.4.3, while anything from a newer minor or
  major is refused (FR-027, FR-028, FR-034a).
- Q: After a release passes both the smoke test and the upgrade test, should it become
  available to operators automatically, or wait for the maintainer to approve it? → **A: it
  waits for the maintainer's approval** as a candidate with its draft notes; approval makes it
  available and moves the moving tags (FR-009, FR-009a).
- Q: Which earlier releases should each release's upgrade test start from? → **A: the
  release just before, plus the first release of the current major (x.0.0)**, plus the last
  minor of the previous major when the new release starts a major (FR-008); 0.1.0 plays the
  part of x.0.0 while the product is in 0.x.
- Q: Should content saved under an older schema version stay in the old format until someone
  next saves or publishes it, or should a release also rewrite all of it in the background
  after upgrading? → **A: no bulk rewrite**; older documents are converted when read and
  stored in the current format on their next save or publish, and every conversion step is
  kept for as long as any supported database may still hold that version (FR-021 to FR-023a).
- Q: What version number should the first release have: 1.0.0, or a 0.x number until the
  product is feature-complete? → **A: 0.1.0, with the full upgrade promise applied to 0.x**:
  0.x counts as one major version, every 0.x release upgrades from every earlier one, and
  breaking changes wait for 1.0.0 (FR-001a).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Upgrade by replacing the image (Priority: P1)

An operator runs version N of QuickBuilder with real sites and content. Version N+1 is
released. The operator reads the upgrade notes, takes a backup, changes the image tag in their
compose file and restarts. The application migrates the database on its own, comes back
healthy, and every site renders as before. Members sign in with their existing accounts and
find their drafts, published pages, collections, media and form submissions where they left
them.

**Why this priority**: This is the promise the issue makes and Principle III requires. Without
it, operators either never upgrade (and miss security fixes) or lose data when they do.

**Independent Test**: Restore the database snapshot captured from the previous release, start
the new release's published image against it, and check that it becomes ready, that every
page captured in the snapshot renders the same visible content, and that every kind of stored
data is still present and usable.

**Acceptance Scenarios**:

1. **Given** a database written by release N with sites, pages, components, collections,
   items, media, menus, redirects, forms and submissions, **When** the operator starts release
   N+1 against it, **Then** the migrations apply without manual steps and the application
   reports ready.
2. **Given** that upgraded installation, **When** a visitor requests any page that was
   published under release N, **Then** the page shows the same visible content it showed
   under release N.
3. **Given** that upgraded installation, **When** a member who existed under release N signs
   in, **Then** they sign in with their existing password and see their sites, drafts and
   submissions unchanged.
4. **Given** the upgrade has completed, **When** the operator restarts release N+1 again,
   **Then** no migration runs a second time and nothing in the data changes.
5. **Given** migrations are set to run manually, **When** the operator runs the migrate
   command twice in a row, **Then** the second run reports that nothing was pending and
   succeeds.

---

### User Story 2 - A release cannot ship unless it upgrades and boots (Priority: P1)

The maintainer tags a new version. The pipeline builds the image for both architectures,
publishes it to the registry, then tests that exact published image: a smoke test boots it
with the reference compose file and completes the setup wizard and the publish journey, and
an upgrade test restores the previous release's database snapshot and starts the new image on
it. If either fails, the release is not marked as available and operators are not pointed at
it.

**Why this priority**: The upgrade promise is only as good as the gate that enforces it.
Testing the published image, not a local build, is what proves the thing operators download
works.

**Independent Test**: Tag a release candidate; confirm that an image is published for both
architectures, that both gates run against the published image by its digest, and that a
deliberately broken migration or a broken setup wizard stops the release from being marked
available.

**Acceptance Scenarios**:

1. **Given** a version tag is pushed, **When** the pipeline runs, **Then** one image is
   published that runs on both amd64 and arm64 servers, tagged with the exact version.
2. **Given** the image is published, **When** the smoke gate runs, **Then** it boots that
   image with the reference compose file and completes setup wizard, create page, publish and
   view.
3. **Given** the image is published, **When** the upgrade gate runs, **Then** it restores the
   snapshot of the release just before and of the first release of the current major, boots
   the new image against each, and checks readiness, rendered pages and stored data.
4. **Given** either gate fails, **When** the pipeline finishes, **Then** the release is not
   marked as the latest version, its notes say it is not available, and the failure output is
   kept for the maintainer.
5. **Given** both gates pass, **When** the pipeline finishes, **Then** the release waits as a
   candidate with its draft release notes and upgrade notes, and nothing operators follow
   points at it yet.
5a. **Given** a candidate that passed both gates, **When** the maintainer approves it,
   **Then** it is marked available with its notes, and the moving tags (latest, latest of its
   major, latest of its minor) point at the same image the gates tested.
6. **Given** a migration that shipped in an earlier release has been edited since, **When**
   the pipeline or any pull request build runs, **Then** it fails and names the changed
   migration.

---

### User Story 3 - Old content keeps rendering (Priority: P1)

A page was saved and published under an older content schema version. A later release changes
the page tree, collection schema or binding format and raises the schema version. The page
still renders for visitors, without anyone touching it. When an editor next opens and saves
or publishes it, it is stored under the current version.

**Why this priority**: Principle VI promises it. Sites have pages nobody edits for years; they
must not break because the format moved on.

**Independent Test**: Store a page, a component, a collection schema and an item under each
schema version that has ever existed, then render each on the current release and open each
in the editor; confirm that they render and edit correctly and are stored at the current
version after the next save or publish.

**Acceptance Scenarios**:

1. **Given** a published page stored under an older schema version, **When** a visitor
   requests it, **Then** it renders the same visible content it rendered under the release
   that stored it.
2. **Given** a draft stored under an older schema version, **When** an editor opens it,
   **Then** the editor shows it as the current version would, with nothing lost.
3. **Given** that draft, **When** the editor saves or publishes it, **Then** it is stored
   under the current schema version.
4. **Given** content stored under a schema version newer than the running release knows,
   **When** it is read, **Then** the release does not overwrite it, and the startup check
   (User Story 4) has already refused to run.
5. **Given** a schema version upgrade cannot be applied to one document (for example, it is
   damaged), **When** it is read, **Then** that one page fails safely the same way any
   unrenderable page does, the failure is logged without content, and every other page keeps
   rendering.

---

### User Story 4 - Startup refuses unsafe version combinations (Priority: P2)

An operator starts an image against a database. Before serving anything, the application
compares its own version with the version that last wrote the database. If the database was
written by a newer release (an accidental downgrade), or by a release too old to upgrade from
directly, the application stops with a clear message saying what it found, what it expected
and what to do next, instead of half-migrating or corrupting data.

**Why this priority**: Forward-only migrations make an accidental downgrade the most likely
way to damage data. A clear refusal turns a silent corruption into a one-line fix.

**Independent Test**: Start an older image against a database written by a newer one, and a
new image against a database from an unsupported old version; confirm both refuse to start,
change nothing, and print a message naming both versions and the next step.

**Acceptance Scenarios**:

1. **Given** a database last written by release 1.4.0, **When** release 1.3.0 starts against
   it, **Then** it refuses to start, changes nothing, and says the database belongs to a newer
   release and points to the rollback guidance.
2. **Given** a database last written by a release in an earlier major version than the
   oldest one the running release supports upgrading from, **When** it starts, **Then** it
   refuses to start, changes nothing, and names the release the operator must upgrade
   through first.
2a. **Given** a database last migrated by release 1.4.3, **When** release 1.4.2 starts
   against it, **Then** it starts normally, because both share the same minor version and
   patch releases change no stored data; the recorded version stays 1.4.3.
3. **Given** an empty database, **When** any release starts, **Then** the check passes and
   the first-run setup proceeds as today.
4. **Given** a database written by the pre-release builds (before the first numbered
   release), **When** a 0.x release starts, **Then** it treats the database as upgradable and
   records its own version once migrations succeed (from 1.0.0 on, FR-030 refuses it).
5. **Given** the check refuses to start, **When** the operator looks at the readiness health
   check or the logs, **Then** the reason is visible there as well as in the startup output.
6. **Given** a successful start, **When** an administrator looks at the admin, **Then** they
   can see the running release version.

---

### User Story 5 - Operators know what changes and how to go back (Priority: P2)

Before upgrading, an operator reads the upgrade notes for every release between the one they
run and the one they want. The notes say what changes for them: new configuration keys,
migrations and roughly how long they take, data rewritten in the background, caches that
will refill, breaking changes, and anything they must do by hand. If an upgrade goes wrong,
the rollback guidance tells them how to return to the previous version from the backup they
took.

**Why this priority**: The image can be perfect and an operator can still be surprised. Notes
and a tested way back are what make operators willing to upgrade at all.

**Independent Test**: Cut a release and check that its release notes and upgrade notes are
produced from the template, contain every required section, and list every migration and
configuration change in the release; follow the rollback guidance on a test installation and
confirm the previous version comes back with its data.

**Acceptance Scenarios**:

1. **Given** a release is cut, **When** its notes are produced, **Then** they follow the
   release notes template and contain the version, the changes grouped by kind, the upgrade
   notes, the image references by digest and the supported upgrade path.
2. **Given** a release adds a migration or a configuration key, **When** the upgrade notes
   are produced, **Then** each one is listed, and "none" is written explicitly when there are
   none.
3. **Given** a release contains a breaking change, **When** it is not a major version,
   **Then** the release is stopped before publishing.
4. **Given** an operator upgraded and wants to go back, **When** they follow the rollback
   guidance, **Then** they restore the backup taken before the upgrade, start the previous
   image, and their installation is as it was at the time of the backup.
5. **Given** an operator skips the backup, **When** they read the rollback guidance, **Then**
   it states plainly that without the backup going back is not supported, and that data
   written since the upgrade is lost by any rollback.

---

### Edge Cases

- **Upgrade interrupted halfway** (container killed during migrations): the next start resumes
  from the last completed migration and finishes; no migration is applied twice and no data
  is left half-converted.
- **Several instances start the new image at once**: only one migrates; the others wait and
  then start (the existing per-module migration lock); none serves requests on a half-migrated
  database.
- **Skipping releases within a major** (1.1 → 1.5): works by replacing the image, as Principle
  III requires; migrations and content upgrades of every release in between apply in order.
  The gate's run from the first release of the major proves the longest such jump on every
  release.
- **Background rewrites still running after the upgrade** (index backfills, render stamps):
  sites keep rendering while they run; the release notes say they exist.
- **Old image started after a failed upgrade without restoring the backup**: refused by the
  startup check, because the newer release marks the database as being upgraded before its
  first migration runs (FR-030).
- **Registry unreachable or publishing partly failed** (one architecture pushed, the other
  not): the release is not marked available; re-running the pipeline publishes the same
  version again without producing a second, different image under the same tag.
- **Re-running a release pipeline for an existing version**: the version tag never moves to a
  different build once marked available.
- **Previous release has no snapshot** (the first numbered release): the upgrade gate starts
  from a database written by the last pre-release build on the main branch (its image,
  filled by the content journey); from the second release on, it uses the stored snapshots of
  earlier releases.
- **Time-dependent content**: snapshots never hold content whose visible form depends on the
  date a gate runs (schedules due before the far future, "now"-relative text), so a snapshot
  restored months later shows what it showed when it was taken.
- **Intentional visible changes**: a release that deliberately changes what visitors see on
  existing pages lists those pages and the reason in a reviewed allow-list; the upgrade gate
  accepts exactly those differences, and the release notes list them.
- **Snapshot contains content stored under every older schema version**: the upgrade gate
  renders all of it, not just the latest version.
- **Migrations disabled at startup and not yet run**: the startup check still runs; readiness
  reports migrations pending, as it does today, and the version is recorded only once they
  have run.
- **Pre-release or release-candidate tags**: published under their own tag, never moved to
  latest, and never used as the "previous release" for the next upgrade gate.

## Requirements *(mandatory)*

### Functional Requirements

**Versions and releases**

- **FR-001**: Every release MUST have a Semantic Versioning number (major.minor.patch). The
  number MUST be built into the image so the running application knows its own version.
- **FR-001a**: The first release MUST be 0.1.0. While the product is in 0.x, all 0.x releases
  MUST be treated as one major version: every rule in this spec that applies within a major
  version (supported upgrade path, startup checks, no breaking changes outside a new major)
  applies across 0.x, and the first breaking change MUST wait for 1.0.0.
- **FR-002**: A release MUST be started by the maintainer pushing a version tag. No other
  event publishes a release image.
- **FR-003**: Each release MUST produce one image usable on both amd64 and arm64 servers,
  published to a container registry, and pullable without credentials.
- **FR-004**: The image MUST be tagged with its exact version. Once a release is marked
  available, the moving tags (latest, latest of its major, latest of its minor) MUST point at
  it; pre-release versions MUST NOT move any of them.
- **FR-005**: An exact version tag MUST NOT be reassigned to a different build once that
  release is marked available.
- **FR-006**: The running version MUST be visible to administrators in the admin and in the
  startup log.

**Release gates**

- **FR-007**: The release pipeline MUST run the smoke test against the published image, pulled
  from the registry by digest, using the reference compose file: setup wizard, create page,
  publish, view.
- **FR-008**: The release pipeline MUST run the upgrade test against the published image:
  restore a release's database snapshot (and its stored media), start the new image, and
  verify readiness, rendered pages and stored data as defined in FR-012. It MUST run once from
  each of these snapshots, skipping duplicates:
  - the release just before (the highest available release lower than the new one);
  - the first release of the new release's major version (x.0.0, or 0.1.0 while the product
    is in 0.x);
  - when the new release is a new major version, the last minor release of the previous
    major.
- **FR-009**: A release MUST NOT be marked available, and the moving tags MUST NOT move, unless
  both gates pass and the maintainer has approved it. A failed release MUST keep the logs of
  both gates for the maintainer.
- **FR-009a**: When both gates pass, the release MUST wait as a candidate with its draft
  release notes and upgrade notes until the maintainer approves it. Approval MUST publish the
  notes, mark the release available and move the moving tags, without rebuilding or
  re-pushing the image. Approval MUST NOT be possible while a gate has failed.
- **FR-010**: Both gates MUST run on amd64. The arm64 image MUST at least start and report
  ready in the pipeline.

**Database snapshots**

- **FR-011**: Each available release MUST capture a database snapshot (plus the media files it
  references) taken from that release's own image after a scripted content journey that
  creates every kind of stored data: sites with host names, pages and drafts, components,
  design, collections and items, templates and bindings, media with variants, menus, redirects,
  SEO settings, forms with submissions, members with roles. The snapshot MUST be kept with
  the release for the next release's upgrade gate.
- **FR-012**: The upgrade gate MUST record, before the upgrade, what each published page shows
  and what data exists, and MUST compare after the upgrade: every published page shows the
  same visible content (except pages allowed by FR-012a), every record still exists, and an
  existing member can sign in.
- **FR-012a**: A release MAY change what visitors see on existing pages only through a
  reviewed allow-list naming the affected pages and the reason. The upgrade gate MUST accept a
  difference only on a listed page, and the release notes MUST list every entry.
- **FR-013**: The snapshot MUST contain no secrets that are real and no personal data other
  than invented test data.
- **FR-014**: Snapshots MUST accumulate: the content journey of each release adds the data
  kinds introduced since the previous one, so content stored under every older schema version
  remains in the chain of snapshots.

**Migrations**

- **FR-015**: Migrations MUST be forward-only. A release MUST NOT include a step that requires
  going back to an earlier database schema.
- **FR-016**: Running migrations on an already-migrated database MUST change nothing and MUST
  succeed, whether started by the application at startup or by the migrate command.
- **FR-017**: A migration that has shipped in a release MUST NOT be edited or removed. Every
  pull request and every release build MUST fail when a shipped migration differs from the one
  in that release, naming the migration.
- **FR-018**: An interrupted migration run MUST be resumable by starting the application
  again, without manual repair.
- **FR-019**: Data rewrites that are too slow for startup MUST run in the background after the
  application is ready, MUST be idempotent, and MUST NOT stop sites from rendering while they
  run.

**Content schema versions**

- **FR-020**: Every stored document that carries a `schemaVersion` (page trees, components,
  design, collection schemas, item data, binding expressions, forms) MUST render on the current
  release for every version still present in any supported database.
- **FR-021**: When an older document is read, it MUST be upgraded in memory to the current
  version before it is rendered or shown in the editor, without writing it back on read.
- **FR-022**: When an older document is next saved or published, it MUST be stored under the
  current version.
- **FR-023**: Each raise of a schema version MUST ship its upgrade step from the previous
  version, and the steps MUST chain so that any older version reaches the current one.
- **FR-023a**: Releases MUST NOT rewrite stored documents in bulk to raise their schema
  version. Published versions kept in history (for rollback) MUST keep the schema version they
  were published with. Upgrade steps MUST NOT be removed while any supported upgrade path
  (FR-031) could still bring documents of that version.
- **FR-024**: Each upgrade step MUST have golden-file tests with documents from the older
  version as input and the expected current-version document as output.
- **FR-025**: A document that cannot be upgraded MUST fail on its own page the same way any
  unrenderable page fails today, MUST be logged with its identifier and version only (no
  content), and MUST NOT stop other pages from rendering.
- **FR-026**: A document stored under a version newer than the running release knows MUST NOT
  be modified by that release.

**Startup compatibility checks**

- **FR-027**: The database MUST record the highest release version that has migrated it. A
  release that is older than the recorded version MUST NOT lower it.
- **FR-028**: Before applying migrations or serving requests, the application MUST compare its
  version with the recorded one and MUST refuse to start, without changing anything, when:
  - the database was migrated by a release with a newer major or minor version (a newer
    patch of the same major.minor is allowed to have migrated it); or
  - the database was last migrated by a release older than the oldest version this release
    supports upgrading from.
- **FR-029**: A refusal MUST log one clear message naming the release found, the release
  running, and what to do next (upgrade through a named release, or follow the rollback
  guidance). The readiness health check MUST report the refusal and the next step, without
  version numbers, because it answers anonymous callers.
- **FR-030**: An empty database MUST pass the check. A database written by pre-release builds
  (no version recorded) MUST pass while the running release's oldest supported version is
  `0.0.0`, that is throughout 0.x; from 1.0.0 on it is refused with "upgrade through 0.N
  first" (FR-031). Before its first migration runs, a release from a newer minor or major MUST mark the
  database as being upgraded to its version, so an older release refuses it even if the
  upgrade stops halfway; the mark becomes the recorded version once all migrations succeed.
- **FR-031**: Every release MUST support upgrading directly from every earlier release in the
  same major version, and from the last minor release of the previous major version.

**Release notes, upgrade notes and rollback guidance**

- **FR-032**: The repository MUST contain a release notes template with these sections:
  version and date, highlights, changes grouped by kind (added, changed, fixed, security),
  upgrade notes, supported upgrade path, image references by digest, and known issues.
- **FR-033**: Upgrade notes MUST be generated for each release from the changes it contains and
  MUST list: migrations added per module, background rewrites, configuration keys added,
  changed or removed, content schema versions raised, caches that will refill, breaking
  changes, and manual steps. Each list MUST say "none" explicitly when empty.
- **FR-034**: A release MUST be stopped before publishing when it contains a breaking change
  and is not a new major version, or when its upgrade notes have a required section missing.
- **FR-034a**: A patch release MUST NOT add migrations or raise any content schema version. The
  release MUST be stopped before publishing when a patch release does either; such a change
  waits for the next minor release.
- **FR-035**: The operations guide MUST contain an upgrade procedure (read the notes, take a
  backup of the database and media, replace the image, check readiness and the version) and
  rollback guidance (stop, restore the backup taken before the upgrade, start the previous
  image, check), stating plainly that rollback without a backup is not supported and that
  data written after the upgrade is lost.
- **FR-036**: The rollback guidance MUST be exercised by a test that upgrades a snapshot,
  restores the backup, starts the previous image and checks that it is ready and renders.

**Out of scope**

- **FR-037**: Automatic downgrade (running migrations backwards, or an older release reading
  a newer database) is not provided.
- **FR-038**: Zero-downtime rolling upgrades (old and new versions serving at the same time
  against one database) are not provided. Operators stop all instances before upgrading.

### Key Entities

- **Release**: a published version of the product. Has a version number, a build date, image
  references for each architecture, release notes, upgrade notes, a status (candidate,
  available, failed) and a database snapshot.
- **Release Notes / Upgrade Notes**: the text published with a release. Release notes describe
  what changed; upgrade notes describe what an operator must know or do, generated per
  release from the template.
- **Database Snapshot**: a dump of a database and its media files written by a given release
  after the content journey. Starting point of the next release's upgrade gate.
- **Installation Version Record**: stored in the database. The highest release version that
  has migrated it and when it changed. Releases of the same major.minor may run against it;
  older minor or major releases may not.
- **Content Schema Version**: the `schemaVersion` carried by each stored document, and the
  chain of upgrade steps from each older version to the current one.
- **Migration**: a forward-only, idempotent change to a module's database schema or data,
  frozen once it has shipped in a release.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator upgrades from the previous release to the new one by changing one
  image tag and restarting, with zero manual database steps, and the installation is ready in
  under 5 minutes on the reference setup with the snapshot's data.
- **SC-002**: After the upgrade gate, 100% of pages published in the snapshot show the same
  visible content, and 100% of stored records of every kind are still present.
- **SC-003**: 100% of releases marked available have passed both the smoke gate and the
  upgrade gate on their published image; no release is marked available with a failed gate.
- **SC-004**: Every release image runs on both amd64 and arm64 servers.
- **SC-005**: Content stored under every schema version ever released renders on the current
  release; 0 pages fail because of their schema version.
- **SC-006**: Starting an older release against a newer database changes 0 rows and refuses to
  serve within 30 seconds, with a log message naming both versions.
- **SC-007**: Running migrations a second time on an upgraded database changes 0 rows.
- **SC-008**: Editing a shipped migration fails the build 100% of the time.
- **SC-009**: Every release has release notes and upgrade notes with all required sections
  filled or explicitly marked "none".
- **SC-010**: An operator following the rollback guidance with a pre-upgrade backup is back on
  the previous release, ready and rendering, in under 15 minutes on the reference setup.
- **SC-011**: From tag to a candidate ready for approval (published image, both gates run,
  draft notes) takes under 60 minutes, so a security fix can reach operators the same day.

## Assumptions

- **Registry**: GitHub Container Registry, public, alongside the repository. Pulling needs no
  account. Another registry can be added later without changing this spec.
- **First release**: there are no numbered releases yet. The first one is 0.1.0 (FR-001a).
  Its upgrade gate starts from a database written by the image of the last pre-release commit
  on the main branch and filled by the content journey; from the second release on, the
  gate uses the stored snapshots of earlier releases. The existing per-feature upgrade tests
  stay as they are.
- **Supported upgrade path**: any earlier release in the same major version, plus the last
  minor of the previous major, per Principle III. The gate proves the two ends of that range
  (FR-008); releases in between are covered because each was tested from its own predecessor.
- **Snapshot storage**: snapshots are stored as release assets next to each release (not in
  the git repository), so they stay small to fetch and tied to the release that produced them.
- **Content schema upgrade timing**: upgraded on read in memory, written back on the next save
  or publish (FR-021 to FR-023a). A bulk rewrite, needed only to retire old upgrade steps, is
  left to a later feature with its own spec.
- **Rollback**: restoring from the operator's own backup (database dump plus media) is the
  only supported way back. Built-in site export and import (#20) is a separate feature and not
  required here.
- **Backups**: the operations guide already explains how to back up and restore media; this
  feature adds the database part and ties both into the upgrade procedure.
- **Signing and provenance**: image signing and software bills of materials are useful but not
  required by the issue; the plan may include them if cheap.
- **Who runs releases**: the maintainer, by pushing a tag and then approving the candidate. No
  release calendar is set here.
