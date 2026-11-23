# Feature Specification: Operator Documentation

**Feature Branch**: `026-operator-documentation`

**Created**: 2026-11-22

**Status**: Draft

**Issue**: #26

**Input**: User description: "#26 Operator documentation. Problem / goal: For a product people install themselves, documentation is part of the product. An operator must be able to install, configure, back up, upgrade and troubleshoot without reading source code, and every release must keep those docs true. In scope: documentation site in the repository (install with docker-compose, configuration reference generated from the configuration schema, reverse proxy and TLS examples for Nginx/Caddy/Traefik, backup and restore, upgrade guide, troubleshooting, security checklist); a CI check that every configuration key is documented; docs published on release. Out of scope: end-user/editor help content inside the admin UI, translated documentation."

## Context

QuickBuilder is installed and run by customers on their own infrastructure (constitution
Principle II). Operators vary in skill (Principle IX), and nobody from the project is there
to help them. Whatever they need to know to run the product safely has to be written down.

Operator knowledge exists today, but it grew feature by feature:

- `docs/configuration.md` lists configuration keys in hand-written tables. Feature 019
  (release and upgrade safety) compares the keys in that file between releases so the
  release notes name added and removed keys, but nothing checks that the file matches the
  keys the application actually reads. A key can be added to the product and never
  documented, or documented with a default that is no longer true.
- `docs/operations.md` (over 1,100 lines) is organised by product feature ("Media
  storage", "Publishing and rendering", "Upgrading from feature 012"), not by the tasks an
  operator performs. Installing, backing up, upgrading and troubleshooting are spread
  across many sections, and some sections describe upgrades between internal feature
  milestones rather than between releases.
- There are no worked reverse proxy or TLS examples, no single backup and restore
  procedure that covers the database and media together, no troubleshooting guide and no
  security checklist.
- The documentation is only readable inside the repository. A released version has no
  published documentation an operator can read before installing.

This feature turns that material into a task-oriented operator documentation site kept in
the repository, makes the configuration reference come from the product's own definition of
its settings, adds a check that fails when a setting is undocumented, and publishes the
documentation for each release.

## Clarifications

### Session 2026-11-22

- Q: Should the automated pipeline run the documented install, the three proxy examples and backup-then-restore, or is human review enough? → A: Run all of them on every release; on a pull request, run them only when the change touches the documentation, the reference compose file or the proxy examples (FR-008, FR-020, FR-026, FR-043).
- Q: How many versions of the published documentation should operators be able to choose from? → A: One version per minor line (for example 0.3), replaced by each patch release of that line; every minor line stays published (FR-036, FR-037, FR-039).
- Q: Where should the documentation be published on release? → A: A public static website served by the code host's pages service, versioned per minor line; no separate hosting or custom domain, and no built bundle attached to releases beyond the documentation sources already in the release's downloadable sources (FR-036, FR-041).
- Q: What should happen to the "Upgrading from feature NNN" sections in `docs/operations.md`? → A: Steps an operator upgrading from 0.1.0 still needs move into the next release's upgrade notes; sections that only apply between unreleased milestones are deleted (FR-004, FR-044).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - An operator installs QuickBuilder from the documentation alone (Priority: P1)

A person who has never seen QuickBuilder opens the documentation, follows the install guide
on a server with Docker, and ends with a running installation and an administrator account,
without reading source code or asking anyone.

**Why this priority**: Installing is the first thing every operator does. If it fails, no
other documentation matters. The constitution promises a working admin within 5 minutes of
`docker compose up`; the documentation is what makes that promise reachable for a stranger.

**Independent Test**: Give the install guide to someone unfamiliar with the project and a
clean server with Docker. They follow it word for word and reach the signed-in admin,
recording any step where they had to guess.

**Acceptance Scenarios**:

1. **Given** a clean server meeting the stated prerequisites, **When** the operator follows
   the install guide step by step, **Then** they reach the setup wizard, create the first
   administrator and sign in, with no step requiring knowledge the guide does not give.
2. **Given** the install guide, **When** the operator reads its prerequisites section,
   **Then** it states the minimum resources, supported architectures, required software and
   network ports before any command is run.
3. **Given** the operator completed the install, **When** they read the guide's final
   section, **Then** it tells them what to do next: put a reverse proxy with TLS in front,
   set up backups, and review the security checklist, each linked.
4. **Given** the install guide's reference compose file and commands, **When** the
   project's automated pipeline runs them as written, **Then** they produce a working
   installation, so the guide cannot silently drift from what works.

---

### User Story 2 - An operator configures the product from a complete, accurate reference (Priority: P1)

An operator who needs to change behaviour (storage provider, email, base path, cache,
proxy trust) looks the setting up in the configuration reference and finds every setting the
product reads, with its purpose, type, default, allowed values, whether it is required, and
how to set it as an environment variable.

**Why this priority**: Configuration is the operator's only control surface. An
undocumented setting is a hidden feature; a wrong default is a trap. The constitution
requires documented keys with safe defaults.

**Independent Test**: Compare the reference against the settings the application accepts:
every accepted setting appears in the reference, every setting in the reference is accepted,
and each documented default matches what the product does when the setting is absent.

**Acceptance Scenarios**:

1. **Given** the configuration reference, **When** an operator looks up any setting the
   product reads, **Then** they find its name, its environment variable form, its type,
   its default (or that it has none), whether it is required, its allowed values or range,
   and a plain-language description.
2. **Given** a setting is added to the product, **When** the reference is regenerated,
   **Then** the new setting appears without anyone editing the reference by hand, and the
   description, which a person wrote next to the setting's definition, appears with it.
3. **Given** a setting is a secret (a password, key or connection string), **When** it
   appears in the reference, **Then** it is marked as secret, with advice not to put it in
   files committed to version control and how to provide it safely.
4. **Given** a setting that only applies when another is chosen (for example, credentials
   for an optional storage provider), **When** it appears in the reference, **Then** the
   reference says which choice it belongs to.
5. **Given** a released version of the documentation, **When** an operator reads the
   reference, **Then** it describes exactly the settings of that release, not of a later
   development version.

---

### User Story 3 - A contributor cannot merge an undocumented or stale setting (Priority: P1)

A contributor adds, renames or removes a setting. The automated checks on their change fail
until the configuration reference matches the product and the new setting has a description,
so no release ever ships a setting the documentation does not explain.

**Why this priority**: The issue's goal is that every release keeps the docs true.
Without an automatic gate, accuracy depends on memory, and the current hand-written tables
already prove that does not hold over time.

**Independent Test**: On a branch, add a setting without a description; the check fails and
names the setting. Add the description and regenerate; the check passes. Remove a setting
but leave it in the reference; the check fails and names it.

**Acceptance Scenarios**:

1. **Given** a change adds a setting with no description, **When** the checks run,
   **Then** they fail and name the setting and what is missing.
2. **Given** a change adds, renames or removes a setting, or changes a default, without
   regenerating the reference, **When** the checks run, **Then** they fail, name each
   difference, and say how to regenerate.
3. **Given** the reference was edited by hand so it no longer matches the product,
   **When** the checks run, **Then** they fail and name the difference.
4. **Given** the reference matches and every setting is described, **When** the checks
   run, **Then** they pass.
5. **Given** the release notes already list configuration keys added and removed since the
   previous release (feature 019), **When** the reference becomes generated, **Then** that
   release-notes comparison keeps working against the generated reference.

---

### User Story 4 - An operator backs up and restores an installation (Priority: P1)

An operator follows one procedure to back up everything an installation needs (database,
media, configuration and secrets) and, on a new or wiped server, follows the matching
procedure to restore it and confirm that sites, pages, collections and media are back.

**Why this priority**: Upgrades never losing data (Principle III) also depends on the
operator having a backup that actually restores. A backup procedure that has never been
restored is a guess.

**Independent Test**: Follow the backup procedure on an installation with content and
media, destroy the installation, follow the restore procedure on a fresh server, and check
that published pages, drafts, collections and media match the original.

**Acceptance Scenarios**:

1. **Given** a running installation, **When** the operator follows the backup guide,
   **Then** they produce a backup of the database, the media (for the default local storage
   and with pointers for the optional storage providers), and the configuration and
   secrets, and the guide says which of these are needed together and why.
2. **Given** a backup, **When** the operator follows the restore guide on a clean server,
   **Then** the installation starts, the same sites answer on the same host names, and
   content and media are intact.
3. **Given** the backup guide, **When** the operator reads it, **Then** it explains how
   to take a consistent backup while the installation is running or what downtime is
   needed, how often to back up, and how to verify a backup.
4. **Given** the product also offers per-site export and import, **When** the operator
   reads the backup guide, **Then** it explains when to use a full backup and when a site
   export is enough, and links to the export and import instructions.
5. **Given** the backup and restore procedures, **When** the project's automated pipeline
   runs them on a test installation, **Then** the restored installation passes the same
   smoke test as a fresh one.

---

### User Story 5 - An operator upgrades between releases safely (Priority: P1)

An operator running one release reads the upgrade guide, learns what to check before,
during and after upgrading, which upgrade paths are supported, and how to roll back, and
reads the version-specific upgrade notes for the releases they are crossing.

**Why this priority**: Upgrading is the riskiest routine task and the one done most often
after install. Feature 019 defines the safety rules; operators need them written as a
procedure.

**Independent Test**: An operator on the previous release follows the upgrade guide to the
current one, including the pre-upgrade backup, and ends on the new version with content
intact; then follows the rollback section and returns to the previous version.

**Acceptance Scenarios**:

1. **Given** the upgrade guide, **When** an operator reads it, **Then** it covers the
   supported upgrade paths, the pre-upgrade checklist (backup, read notes, check
   configuration changes), the upgrade itself, how to confirm success, and rollback.
2. **Given** an operator crossing several releases, **When** they open the upgrade notes,
   **Then** they find each release's notes in order, including configuration keys added,
   changed or removed and any manual step, without reading notes of releases they are not
   crossing.
3. **Given** the product refuses to start after an upgrade (for example an unsupported
   jump or a configuration that no longer validates), **When** the operator looks up the
   message, **Then** the documentation explains the cause and the fix.
4. **Given** the existing operations material describes upgrades between internal feature
   milestones, **When** this feature is complete, **Then** every step an operator on 0.1.0
   needs to reach the next release is in that release's upgrade notes, and no operator page
   refers to upgrading from or to a feature milestone.

---

### User Story 6 - An operator puts QuickBuilder behind a reverse proxy with TLS (Priority: P2)

An operator chooses Nginx, Caddy or Traefik, copies the matching worked example, adapts the
host names, and ends with HTTPS working, sites resolving by host name, real client
addresses seen by the product, and the admin reachable only where intended.

**Why this priority**: TLS and domain routing are the operator's responsibility
(Principle V), and getting forwarded headers wrong breaks site resolution and sign-in
throttling. It is P2 because the product works without a proxy for evaluation, but no
production install should run that way.

**Independent Test**: For each of the three proxies, start the example alongside the
reference installation and check that a site answers over HTTPS on its host name, the
product sees the client address and scheme from the proxy, and the admin restriction (when
configured) holds.

**Acceptance Scenarios**:

1. **Given** the reverse proxy guide, **When** an operator picks one of Nginx, Caddy or
   Traefik, **Then** they find a complete example covering TLS, forwarding of host,
   scheme and client address, upload size limits, and the matching product settings that
   name the proxy as trusted.
2. **Given** an installation with several sites on different domains, **When** the
   operator follows the guide, **Then** it shows how each domain reaches the installation
   and how certificates are obtained for each.
3. **Given** the installation serves under a sub-path, **When** the operator follows the
   guide, **Then** the example shows the base path setup, including the root addresses
   (such as `robots.txt`) that must still reach the product.
4. **Given** the examples, **When** the project's automated pipeline runs them with test
   certificates, **Then** each one serves a site over HTTPS and the product reports the
   forwarded client address and scheme correctly.
5. **Given** a common mistake (proxy not trusted, header missing), **When** the operator
   looks at the guide's checks section, **Then** they find how to tell it has happened and
   how to fix it.

---

### User Story 7 - An operator troubleshoots a problem without reading source code (Priority: P2)

Something goes wrong: the container will not start, readiness stays failed, a site shows
"not found", uploads fail, email is not sent, sign-in is blocked. The operator finds the
symptom in the troubleshooting guide and follows it to the cause and fix.

**Why this priority**: Troubleshooting is what operators turn to when they are already
under pressure. It is P2 because it builds on the other guides and refers to them.

**Independent Test**: Reproduce each listed symptom on a test installation and follow the
guide; it leads to the cause and a fix that clears the symptom.

**Acceptance Scenarios**:

1. **Given** the troubleshooting guide, **When** an operator scans it, **Then** problems
   are listed by symptom as the operator sees it (a message, a status, a behaviour), not by
   internal component.
2. **Given** a startup failure caused by configuration, **When** the operator searches for
   the message the product printed, **Then** the guide has an entry for it, explaining what
   it means and how to fix it.
3. **Given** the operator needs more information, **When** they read the guide, **Then**
   it explains how to read the logs, what the health endpoints report, and what to collect
   before asking for help, without asking for content or personal data.
4. **Given** the guide, **When** each entry is followed, **Then** it links to the
   configuration reference entry or the other guide that holds the full explanation.

---

### User Story 8 - An operator hardens an installation with a security checklist (Priority: P2)

Before going live, an operator works through a short security checklist and can confirm each
item on their installation: TLS in front, proxies named, secrets out of version control,
administrative surface limited to its host names, backups encrypted and tested, updates
watched, optional outbound features left off unless needed.

**Why this priority**: The product is secure by default (Principle IX), but some protection
depends on choices only the operator makes. A checklist turns them into a short, verifiable
task. It is P2 because it is short and mostly links to other guides.

**Independent Test**: Take an installation configured from the install guide only and work
through the checklist; every item can be checked with a stated, observable test and leads to
instructions for fixing it.

**Acceptance Scenarios**:

1. **Given** the security checklist, **When** an operator reads each item, **Then** each
   item says what to check, how to check it, and where the instructions to fix it are.
2. **Given** a setting whose secure value differs from a convenient one, **When** it
   appears in the checklist, **Then** the checklist states the secure value and the risk of
   the other.
3. **Given** the checklist, **When** it is read, **Then** it covers at least: TLS,
   trusted proxies, administrative host names, secret handling, database exposure, media
   storage exposure, backups, update policy, logging of personal data, and outbound network
   features.
4. **Given** the checklist, **When** an operator wants to report a vulnerability, **Then**
   it tells them how.

---

### User Story 9 - An operator reads the documentation for their release online (Priority: P2)

Before installing or upgrading, an operator opens the published documentation for the
release they run or plan to run, without cloning the repository, and can switch to another
release's documentation.

**Why this priority**: People evaluate and plan before they download. It is P2 because the
same content is also readable in the repository, so the information exists even before
publishing works.

**Independent Test**: Cut a release; the published documentation for its minor line
appears, shows the exact release number, and an older minor line's documentation is still
reachable and unchanged.

**Acceptance Scenarios**:

1. **Given** a release is published, **When** the release process completes, **Then**
   the documentation built from that release's sources is published as the version of its
   minor line (for example 0.3) and shows the exact release it was built from (for example
   0.3.2).
6. **Given** a patch release of a minor line that is already published, **When** it is
   published, **Then** it replaces that minor line's documentation, and no other minor
   line's documentation changes.
2. **Given** several releases, **When** an operator opens the published documentation,
   **Then** they land on the latest stable release and can choose an earlier one.
3. **Given** the documentation fails to build (a broken link, a missing page, an
   undocumented setting), **When** a release is attempted, **Then** the release stops
   before anything is published and names the problem.
4. **Given** a release is published, **When** an operator opens the release notes,
   **Then** they link to the upgrade guide and the documentation for that release.
5. **Given** changes are merged between releases, **When** they reach the main branch,
   **Then** the published documentation for released versions does not change.

---

### Edge Cases

- **Setting with a computed default**: a setting whose default depends on another setting
  or the environment (for example a default path inside the container) is documented with
  the rule, not a misleading literal value.
- **Collection-style settings**: settings that take a list (numbered entries such as
  trusted proxies) or a map are documented once with how to add entries, including in the
  environment variable form.
- **Settings read by an optional provider only**: they appear in the reference grouped
  under that provider and are covered by the same documentation check.
- **Settings that are never meant for operators** (internal test switches, development-only
  values): they are either excluded from the reference by an explicit, reviewed marking, or
  documented; nothing is excluded silently.
- **Deprecated or renamed settings**: a setting kept for compatibility is documented as
  deprecated with its replacement and the release in which it will be removed.
- **Secret values in examples**: no example contains a real-looking secret; every example
  secret is an obvious placeholder, and examples show how to provide secrets without writing
  them into compose files that are committed.
- **Documentation of a release with no published predecessor**: the first published
  release has no older version to choose, and the upgrade guide says that no earlier upgrade
  path exists.
- **Release that fails after the docs step**: if a release is abandoned after its
  documentation was built, the documentation is not published, or is withdrawn, so no
  published documentation describes a release that does not exist.
- **Proxy example versions**: each proxy example states the proxy version it was checked
  with; behaviour differences in other versions are the operator's to verify.
- **Offline installations**: an operator with no internet access can read the documentation
  from the repository or the release download, because the published site is not the only
  copy.
- **Restoring to a different release**: restoring a backup into a newer release is an
  upgrade and follows the upgrade rules; restoring into an older release is refused by the
  product, and the restore guide says so.
- **Media on an optional storage provider**: the backup guide explains that the provider's
  own backup features apply and what must be kept consistent with the database backup.

## Requirements *(mandatory)*

### Functional Requirements

**Documentation site**

- **FR-001**: The repository MUST contain an operator documentation site, organised by the
  operator's tasks: install, configure, reverse proxy and TLS, back up and restore,
  upgrade, troubleshoot, security checklist, and reference. Each task MUST be reachable
  from the site's start page in one step.
- **FR-002**: The documentation MUST be readable as plain text files in the repository
  without building the site, so it is usable offline and in review.
- **FR-003**: The documentation site MUST build with an automated check that fails on
  broken internal links, missing pages referenced from navigation, and references to
  configuration settings that do not exist.
- **FR-004**: Existing operator material in `docs/configuration.md` and
  `docs/operations.md` MUST be moved into the site's task structure or into the reference,
  so there is one place for each topic and no conflicting copies remain. Material that is
  for contributors rather than operators MUST stay out of the operator site.
- **FR-005**: The documentation MUST NOT require reading source code for any operator
  task in scope; where a procedure needs a command, the command is given in full.

**Install**

- **FR-006**: The install guide MUST state prerequisites (supported architectures,
  minimum resources, required software and versions, network ports) before the first
  command.
- **FR-007**: The install guide MUST take the operator from a clean server to a signed-in
  administrator using the reference compose setup, including how to provide the required
  secrets without committing them, and MUST end with links to the reverse proxy, backup and
  security guides.
- **FR-008**: The install procedure MUST be exercised by the project's automated pipeline
  from the documented commands and compose file, on the schedule in FR-043, so a change
  that breaks the documented install fails the checks.

**Configuration reference**

- **FR-009**: The configuration reference MUST be generated from the product's own
  definition of the settings it reads, not written by hand.
- **FR-010**: For every setting the reference MUST give: the setting's name, its
  environment variable form, its type, its default or that it has none, whether it is
  required, allowed values or range where they exist, a plain-language description, and
  whether it is a secret.
- **FR-011**: Settings MUST be grouped by area (for example database, hosting, media
  storage, email, cache, logging) and, where they only apply to an optional provider,
  under that provider.
- **FR-012**: Each setting's description MUST be written by a person next to the setting's
  definition in the product, so that changing a setting and its description happens in
  the same place.
- **FR-013**: Explanatory material that does not fit a single setting (for example why
  trusting no proxy is the default) MAY sit alongside the generated tables, but MUST NOT
  restate defaults or allowed values that the generated part already gives.
- **FR-014**: Deprecated settings MUST be marked with their replacement and planned
  removal release.

**Documentation checks**

- **FR-015**: The automated checks run on every proposed change MUST fail when any setting
  the product reads is missing from the reference, when the reference lists a setting the
  product does not read, when a documented default, type, required flag or allowed value
  differs from the product, or when a setting has no description. Each failure MUST name the
  setting and how to fix it.
- **FR-016**: A setting MAY be excluded from the reference only by an explicit marking on
  its definition with a stated reason; the check MUST list excluded settings so reviewers
  see them.
- **FR-017**: The configuration-key comparison used for release notes (feature 019) MUST
  keep producing the same lists of added and removed keys once the reference is generated.

**Reverse proxy and TLS**

- **FR-018**: The documentation MUST include complete, working examples for Nginx, Caddy
  and Traefik that cover TLS termination, forwarding of host, scheme and client address,
  request size limits for uploads and imports, and the matching product settings for
  trusted proxies.
- **FR-019**: The examples MUST show multiple sites on different domains, the
  administrative host name restriction, and a sub-path installation, either in each example
  or as clearly marked variants.
- **FR-020**: Each example MUST be exercised by the project's automated pipeline against
  the reference installation using test certificates, on the schedule in FR-043, checking that a site answers over
  HTTPS and the product sees the forwarded client address and scheme.
- **FR-021**: Each example MUST state the proxy version it was checked with.

**Backup and restore**

- **FR-022**: The backup guide MUST cover the database, media for the default local
  storage, configuration and secrets, and MUST explain what to do for media held by the
  optional storage providers.
- **FR-023**: The backup guide MUST explain consistency between the database and media
  backups, whether downtime is needed, how often to back up, how to verify a backup, and
  how to protect backups (they contain personal data and secrets).
- **FR-024**: The restore guide MUST take the operator from a backup to a working
  installation on a clean server, including the rule that restoring into a newer release is
  an upgrade and restoring into an older release is refused.
- **FR-025**: The documentation MUST explain the difference between a full backup and a
  per-site export, and when to use each.
- **FR-026**: The documented backup and restore procedure MUST be exercised by the
  project's automated pipeline, on the schedule in FR-043: back up a test installation with content and media,
  restore it onto a fresh one, and pass the smoke test.

**Upgrade**

- **FR-027**: The upgrade guide MUST describe, in terms of releases: supported upgrade
  paths, pre-upgrade steps (backup, read upgrade notes, review configuration changes), the
  upgrade, how to confirm success, and rollback.
- **FR-028**: Upgrade notes for each release MUST be reachable from the upgrade guide in
  release order, and each MUST list configuration settings added, changed, deprecated or
  removed and any manual step.
- **FR-044**: Each "Upgrading from/to feature NNN" section in the existing operations
  material MUST be reviewed: any step an operator upgrading from 0.1.0 still needs MUST be
  moved into the next release's upgrade notes, and sections that apply only between
  unreleased milestones MUST be deleted. After this, no operator page refers to feature
  milestones as upgrade points.
- **FR-029**: Every message the product prints when it refuses to start because of an
  upgrade rule MUST have a documentation entry explaining cause and fix.

**Troubleshooting**

- **FR-030**: The troubleshooting guide MUST be organised by symptom and MUST cover at
  least: the container does not start, startup stops on a configuration error, readiness
  stays failed (including pending migrations), the database cannot be reached, a host name
  shows the not-found page, the admin is unreachable on a host name, uploads or imports are
  rejected, email is not sent, sign-in is throttled or refused over HTTP, and pages show
  stale content.
- **FR-031**: Every configuration error message the product can print at startup MUST be
  findable in the documentation by the message text or the setting it names.
- **FR-032**: The troubleshooting guide MUST explain how to read logs and health
  endpoints and what to collect when asking for help, and MUST tell operators not to share
  secrets, content or personal data in reports.

**Security checklist**

- **FR-033**: The security checklist MUST cover at least: TLS in front of the product,
  trusted proxies, administrative host names, secret handling, database and media storage
  not exposed publicly, backups protected and tested, an update policy, personal data in
  logs, and outbound network features left disabled unless needed.
- **FR-034**: Each checklist item MUST state what to check, how to verify it on a running
  installation, and where to find the fix.
- **FR-035**: The checklist MUST say how to report a security vulnerability.

**Publishing**

- **FR-036**: The release process MUST build the documentation from the release's own
  sources and publish it, as part of publishing the release, to a public static website
  served by the project's code host (its pages service), with no other hosting required, as the documentation version of
  that release's minor line, showing the exact release number it was built from.
- **FR-037**: The published documentation MUST keep one version per minor line, keep every
  minor line ever released available, open on the latest stable minor line by default, and
  let a reader choose another. A patch release replaces its own minor line's version only.
- **FR-038**: A release MUST NOT be published if its documentation fails to build or fails
  the documentation checks; documentation MUST NOT be published for a release that was not
  published.
- **FR-039**: Changes merged after a release MUST NOT alter published documentation; a
  minor line's published version changes only when a later release of that same line is
  published.
- **FR-040**: The release notes MUST link to the published documentation and the upgrade
  guide for that release.
- **FR-041**: The documentation sources for a release MUST also be included with the
  release's downloadable sources, readable without building (FR-002), so operators without
  internet access have them. No separately built documentation bundle is attached.

**When procedures are exercised**

- **FR-043**: The executed procedures (install FR-008, proxy examples FR-020, backup and
  restore FR-026) MUST run on every release, and a failure MUST stop the release before
  anything is published. On a proposed change they MUST run when the change touches the
  operator documentation, the reference compose file or the proxy examples, and MAY be
  skipped otherwise. The configuration documentation check (FR-015) and the site build
  check (FR-003) run on every proposed change regardless.

**Scope boundaries**

- **FR-042**: The documentation MUST NOT include help content for editors or designers
  inside the admin UI, and MUST be in English only.

### Key Entities

- **Setting definition**: one configuration setting the product reads, with its name,
  area, type, default, required flag, allowed values, secret flag, description, optional
  provider it belongs to, deprecation and exclusion markings. The single source for the
  generated reference and for the documentation check.
- **Configuration reference**: the generated, grouped list of all setting definitions not
  excluded, plus hand-written explanatory notes that do not restate generated facts.
- **Operator guide**: one task-oriented page (install, reverse proxy, backup and restore,
  upgrade, troubleshooting, security checklist), with commands given in full and links to
  the reference.
- **Proxy example**: a complete, runnable configuration for one reverse proxy, with the
  proxy version it was checked with and the matching product settings.
- **Published documentation version**: the documentation of one minor line, built from the
  latest published release of that line and showing that release's exact number; replaced
  only by a later release of the same line.
- **Upgrade notes**: per-release notes listing configuration changes and manual steps,
  produced by the release process (feature 019) and linked from the upgrade guide.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A person new to the project, given only the published documentation and a
  clean server with Docker, reaches a signed-in administrator within 15 minutes, with no
  step requiring outside help.
- **SC-002**: 100% of the settings the product reads are in the reference with a
  description, or are listed as excluded with a reason; 0 settings in the reference are
  ones the product does not read.
- **SC-003**: Every documented default matches the product's behaviour when the setting is
  absent, verified automatically on every change.
- **SC-004**: A change that adds, renames or removes a setting without updating its
  documentation fails the automated checks 100% of the time.
- **SC-005**: Each of the three reverse proxy examples serves a site over HTTPS with the
  correct client address and scheme seen by the product, verified automatically.
- **SC-006**: Following the documented backup and restore procedure restores an
  installation that passes the same smoke test as a fresh one, verified automatically.
- **SC-007**: Every startup error message the product can print has a documentation entry
  findable by its text or by the setting it names.
- **SC-008**: Every release has its minor line's documentation published within the same
  release run, and every other minor line's documentation is still reachable and
  unchanged.
- **SC-009**: An operator can find the answer to each scenario in User Stories 1 to 8
  starting from the documentation start page in at most 3 navigation steps.
- **SC-010**: No operator topic is described in two places with conflicting content after
  the existing material is moved.

## Assumptions

- **Audience**: operators who can run Docker and edit text files, but who may not know
  .NET, PostgreSQL internals or reverse proxy details beyond the examples.
- **Install path**: the documented install path is the reference compose setup (app plus
  PostgreSQL). Other orchestrators (Kubernetes, managed container services) are not given
  step-by-step guides; the configuration reference applies to them unchanged.
- **Source of settings**: "generated from the configuration schema" means generated from
  the product's own definition of the settings it reads and validates at startup. The plan
  decides how that definition is expressed and read; if parts of it do not yet carry all
  the facts the reference needs (description, allowed values, secret flag), adding them is
  part of this feature.
- **Publishing destination**: the published documentation is a public static website on
  the code host's pages service (see Clarifications). Pre-release builds are not published
  as a version unless the plan chooses to show them clearly marked.
- **Version granularity**: one documentation version per minor line, replaced by each
  patch release of that line (see Clarifications); patch releases change no configuration
  or procedures under Semantic Versioning, so per-patch versions would be near-duplicates.
- **Proxies**: one current stable version of each of Nginx, Caddy and Traefik is checked;
  certificates in the automated checks come from a local test authority, while the examples
  show how to obtain public certificates.
- **Upgrade notes**: release and upgrade notes are produced by the release tooling from
  feature 019; this feature links to them and makes the upgrade guide release-based, but
  does not change how notes are assembled beyond keeping the configuration-key comparison
  working.
- **Language and tone**: English only, plain language, commands in full; no screenshots
  are required.
- **Publishing precondition**: before the first release that publishes documentation, the
  publishing target is reachable by the public: the repository is public, or
  `DOCS_PAGES_REPOSITORY` names a public repository with Pages enabled. The same holds for
  the release's downloadable sources (FR-041). Until then, releases are still published,
  but their documentation step fails and says why (research OD-14).
- **Vulnerability reporting**: the project's existing or a new security policy file holds
  the reporting route; the checklist links to it.

## Out of Scope

- Help content for editors and designers inside the admin UI, and end-user guides for
  building sites.
- Translated documentation.
- Step-by-step guides for orchestrators other than the reference compose setup.
- Contributor documentation (`docs/contributing.md`) and contract documentation
  (`docs/contracts/`), which stay where they are and are not part of the operator site,
  although the site may link to them.
- Changing product behaviour, error messages or defaults, except: adding the descriptive
  facts needed to generate the reference; the documentation link inside messages, which
  moves to the published documentation; and naming the setting in a startup validation
  message that does not yet name it.
- A hosted search service or analytics on the published site; the published site sends no
  data about readers to third parties.

## Dependencies

- Feature 001 (foundation skeleton): configuration loading, startup validation, health
  endpoints and the reference compose file.
- Feature 019 (release and upgrade safety): the release pipeline, release and upgrade
  notes, and the configuration-key comparison that must keep working.
- Feature 020 (site export and import): the per-site export the backup guide contrasts
  with full backups.
- Features 003 and 009 (media storage, sites and host names): the storage providers and
  host name rules the backup and proxy guides describe.
- The release smoke test (constitution Principle X), reused to verify the documented
  install and restore.
