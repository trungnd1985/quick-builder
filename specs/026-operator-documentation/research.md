# Research: Operator Documentation

**Feature**: 026 · **Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md)

Each decision is numbered `OD-NN` so tasks, code comments and later specs can cite it.

## Starting point (what exists on `main` at af6167b)

- **Settings** are plain options classes bound with `AddOptions<T>().Bind(section)` and
  `ValidateOnStart()`: 20 root classes across Host, Shared.Infrastructure and five modules
  (`HostingOptions`, `DatabaseOptions`, `StorageOptions` with nested `S3` / `AzureBlob` /
  `Local`, `IdentityOptions`, `FormsOptions`, `PreviewOptions`, …). Defaults are property
  initialisers; constraints are DataAnnotations (`[Range]`, `[Required]`,
  `[RegularExpression]`) plus `IValidateOptions<T>` classes. XML summaries exist but are
  written for developers (they cite FR numbers) and `GenerateDocumentationFile` is `false`.
- **Direct reads** outside options binding: `GetConnectionString("Default")` (7 places),
  `Storage:Provider` read at registration, `Hosting:AdminHostNames` read again in
  `SitesOptions`, `Serilog:*` read by Serilog, `Telemetry` read again in `TelemetrySetup`.
- **`docs/configuration.md`** (293 lines, 67 key rows) is hand-written. The 019 release tool
  (`tools/QuickBuilder.Release/ConfigurationKeys.cs`) extracts the first backticked cell of
  every table row, at the previous tag and in the working tree, and refuses release notes
  whose fragments do not account for the difference.
- **`docs/operations.md`** (1,102 lines) is organised by feature and carries 15
  "Upgrading from/to feature NNN" sections.
- **Links into the docs from the product and the release**: `InstallationGate.Guide`
  (logged when the startup gate refuses) and `docs/releases/TEMPLATE.md` both point at
  `github.com/trungnd1985/quick-builder/blob/main/docs/operations.md`; the reference
  `docker-compose.yml` says "See docs/operations.md".
- **Release pipeline** (019): `release.yml` on a version tag builds, gates (smoke, arm64
  boot, upgrade) and drafts a release; `promote.yml` (manual) tags the tested digest and
  publishes. The smoke suite (`tests/QuickBuilder.SmokeTests`, `ComposeDeployment`) already
  boots the reference compose file against an image reference.
- **Released so far**: `v0.1.0`. Fragments for 020–023 sit in `docs/releases/unreleased/`;
  the next release is 0.2.0.
- **Repository visibility**: private. See OD-14.

---

## OD-01 — The setting definition is the options class, annotated with operator attributes

**Decision**: Keep options classes as the single definition. Add a small set of attributes
(in `QuickBuilder.Shared.Kernel`, namespace `QuickBuilder.Shared.Kernel.Settings`) that
carry what reflection cannot infer: `[SettingsSection]` on each root class (section key,
area, title), `[Setting]` on each bound property (operator description, secret flag,
default text when computed, "applies when"), `[SettingsGroup]` on nested-object properties,
`[NotASetting]` for bound members that are not operator settings, and `[DeprecatedSetting]`.
Name, type, default, required flag and range are **derived**, never typed twice
(see [contracts/setting-attributes.md](contracts/setting-attributes.md)).

**Rationale**: FR-009/FR-012 want the description next to the definition and the facts
derived from it. Defaults derived by reflection cannot disagree with the product (SC-003).
An attribute keeps the operator text separate from developer XML comments, which cite
requirement numbers and internal reasoning that operators should not read.

**Alternatives considered**:
- *XML doc comments as descriptions*: needs `GenerateDocumentationFile=true` across the
  solution, which with warnings-as-errors surfaces CS1591/CS1573 on every public member,
  and mixes developer and operator voices. Rejected.
- *A separate JSON Schema file*: a second definition that can drift from the classes —
  exactly the problem being solved. Rejected.
- *Hand-maintained table checked against code*: still two copies. Rejected.

## OD-02 — Defaults are computed by binding the shipped `appsettings.json` onto a fresh instance

**Decision**: The generator creates each root options instance with its parameterless
constructor, binds the image's `appsettings.json` section onto it with the same
`ConfigurationBinder` the product uses, and reads property values as the documented
defaults. Defaults that the product computes at runtime (e.g. `Storage:Local:Path` set in
`PostConfigure`) declare `DefaultText` on `[Setting]`; the check lists every such setting so
a reviewer sees them (edge case "computed default").

**Rationale**: `appsettings.json` can override an initialiser; documenting the initialiser
would then be wrong. Binding the same file the same way reproduces what an operator gets
when they set nothing. `appsettings.Development.json` is never read.

## OD-03 — "Every setting the product reads" is proven by observing a real boot

**Decision**: An integration test boots the host (WebApplicationFactory + Testcontainers
PostgreSQL, as every endpoint test does) with a **recording configuration provider** added
last. The provider answers nothing and records every key passed to `TryGet` and every
prefix passed to `GetChildKeys`. The test asserts, both ways:

1. every observed key is declared, or matches the reviewed **framework allow-list**
   (`Logging:*`, `AllowedHosts`, `Kestrel:*`, `urls`, `ASPNETCORE_*`, `DOTNET_*`, …, each with
   a reason), or is a `[NotASetting]` member;
2. every declared setting was observed (nothing documented is dead).

It runs once with defaults and once per storage provider configuration (S3, AzureBlob), so
provider-only reads are observed. List and map settings are matched by their prefix.

**Rationale**: FR-015 requires failure when the product reads an undeclared key. Reflection
over options classes cannot see `GetConnectionString`, `section["Provider"]` or a new
`configuration["X"]`; observing the binder and every direct read at boot can. ASP.NET Core's
binder resolves each property path through `TryGet`, so bound properties are observed too.

**Limitations (recorded)**: a key read only on a code path that boot does not reach (a
command, a background job's first run) is not observed. Found during implementation: neither are reads that
`Program.cs` makes before the host is built, because a test's extra configuration source is
attached only at build time. A second check covers them: the unit test
`LiteralConfigurationReadsTests` scans `src/` for literal keys (`configuration["A:B"]`,
`GetSection("A:B")`, `GetConnectionString("X")`, `GetValue<T>("A:B")`) and fails on any that is
not declared, a light form of the analyzer deferred below. Mitigation: the test also invokes
each host command's configuration path in dry-run, and `[Setting]`-declared keys that are
read lazily may carry `ObservedLazily = true` with a reason, which the check lists.

**Alternatives considered**: a Roslyn analyzer that flags `IConfiguration` indexers and
`GetSection` with literal strings — catches literals only, misses computed keys, and is a
bigger build-time investment. Rejected for now; listed as deferred.

## OD-04 — The generator and checker are a new tool, `tools/QuickBuilder.Docs` (`qb-docs`)

**Decision**: A console tool referencing `QuickBuilder.Host` (and through it every module)
with commands `settings generate`, `settings check`, `lint` and `snippets`
(see [contracts/qb-docs-cli.md](contracts/qb-docs-cli.md)). Its library code is also used
by the unit test that compares the committed reference with a fresh generation.

**Rationale**: the generator must load every module's options types. Putting it in the
product (`dotnet QuickBuilder.Host.dll settings …`) would ship documentation tooling in the
image; putting it in `qb-release` would make `promote.yml`, which builds `qb-release`,
compile the whole product. A separate tool follows the 019 pattern (`qb-release`) and the
existing `tests/QuickBuilder.UnitTests/Release` test layout.

## OD-05 — The reference is generated into a checked-in Markdown file

**Decision**: `qb-docs settings generate` writes
`docs/operators/reference/configuration.md` in full. Per area it emits a heading, an
optional hand-written note (from `docs/operators/reference/notes/<area>.md`, inlined
verbatim), and a table whose first column is the backticked key — the same row shape
`ConfigurationKeys` already parses. The file begins with a "generated, do not edit"
comment. The unit test `SettingsReferenceIsCurrent` regenerates in memory and fails with a
unified diff and the command to run (FR-015, US3 scenarios 2 and 3).

**Rationale**: checked-in output is readable on GitHub and offline without building
(FR-002), reviewable in PR diffs, and lets the 019 key comparison read any tag with
`git show`. Notes as separate files keep hand-written prose out of the generated region
(FR-013).

**Key spelling**: list settings render as `Section:Key:<n>`, maps as `Section:Key:<name>`,
matching today's `docs/configuration.md`, so the 0.1.0 → 0.2.0 key comparison does not
report spurious renames. Keys that were genuinely undocumented in 0.1.0 and now appear are
declared "added" in this feature's release-note fragment.

## OD-06 — The 019 key comparison reads the new path, falling back to the old one

**Decision**: `ConfigurationKeys.Guide` becomes an ordered list
`["docs/operators/reference/configuration.md", "docs/configuration.md"]`; for a reference,
the first path that exists at that ref is read. Links emitted into release notes point at
the published reference (OD-11) instead of `docs/configuration.md`.

**Rationale**: FR-017. `v0.1.0` only has the old path; every later tag has the new one.

## OD-07 — Documentation site: VitePress, in `docs/operators/`

**Decision**: The operator site is plain Markdown under `docs/operators/` built with
VitePress 1.x (pinned through `docs/operators/package-lock.json`), using its built-in local
search (index built at build time, served from the site, no third-party calls) and its
dead-link check (build fails on a broken internal link). Contributor and contract docs stay
in `docs/` and are linked, not included.

**Rationale**: the repository already runs Node 24 and Vite for the admin; VitePress is
Vite- and Vue-based, MIT-licensed, maintained by the Vue team, and renders the same Markdown
GitHub shows. Local search satisfies "no hosted search, no data about readers sent to third
parties". Node is used only to build the docs in CI; operators never run it (Technology
Constraints are about the admin and the product, which this does not change).

**Alternatives considered**:
- *MkDocs + Material*: excellent, but introduces a Python toolchain, and Material for MkDocs
  is in maintenance mode while its authors move to a successor. Rejected.
- *Docusaurus*: React-based, heavier, and its versioning copies the whole docs tree into the
  repository per version. Rejected.
- *No generator, GitHub-rendered Markdown only*: no versioned public site, no search.
  Rejected by the clarification (public static site).

**Dependency justification (Principle XI)**: VitePress — MIT; build-time only, not in the
image; active releases; alternatives above. Its transitive packages are not redistributed
(the built site is HTML/JS/CSS from VitePress's MIT theme), so `THIRD-PARTY-NOTICES.md`
gains one entry for the theme assets the published site serves.

## OD-08 — Version folders on a `gh-pages` branch, one per minor line

**Decision**: The published site lives on a `gh-pages` branch: `/<major>.<minor>/` per minor
line, `/versions.json` listing published lines with the exact release each was built from,
and `/index.html` redirecting to the latest stable line. VitePress builds each line with
`base: /<repo>/<major>.<minor>/`; a small theme component reads `/versions.json` to render the
version picker and shows "Built from 0.3.2". A patch release replaces only its folder and its
`versions.json` entry; the root redirect moves only when the release is the highest stable
(the same `highest` flag `promote.yml` already computes). Pre-releases are not published.

**Rationale**: FR-036, FR-037, FR-039 and the minor-line clarification. Folders on a branch
are the simplest durable store: older lines are untouched by construction because the
publish step writes one folder. No external hosting.

**Alternatives considered**: GitHub Pages "artifact" deployments (`actions/deploy-pages`)
replace the whole site on each deploy, so every old version would have to be rebuilt or
re-downloaded each time. Rejected.

## OD-09 — Build and check in `release.yml`, publish in `promote.yml`

**Decision**:
- `release.yml` `prepare` job (fails before anything is built): `qb-docs settings check`,
  `qb-docs lint`, and a VitePress build with the release's base path. The built site is
  uploaded as artifact `docs-site` (90-day retention, like the snapshot).
- The executed procedures (OD-12) run as release jobs against the candidate digest and are
  added to `candidate.needs` and to the failure-banner gate list, so `promote.yml` refuses a
  candidate whose procedures failed.
- `promote.yml`, after publishing the release, downloads `docs-site` from the tag's Release
  run and commits it into `gh-pages` (`/<minor>/`, `versions.json`, root redirect when
  `highest`). If the artifact is missing it rebuilds from the tag. Publishing docs is the
  last step, so documentation never exists for an unpublished release (FR-038).

**Rationale**: the gate must stop a release before anything is published (US9 scenario 3);
publishing must follow the release, never precede it. Reusing the tested build keeps what
was checked identical to what is published.

## OD-10 — Every proposed change runs the cheap checks; procedures run on path changes

**Decision**:
- `ci.yml` backend job: the unit test `SettingsReferenceIsCurrent` and the integration test
  `EveryReadSettingIsDeclared` run with the existing test step (no new job).
- `ci.yml` new job `docs`: `qb-docs lint` and the VitePress build, on every PR.
- New workflow `docs-procedures.yml`: `on: pull_request` with `paths:
  docs/operators/**, docker-compose.yml, Dockerfile, src/QuickBuilder.Host/appsettings.json`,
  and `on: workflow_call` so `release.yml` reuses it with an image digest. On a PR it builds
  the image from the branch (as the `smoke` job does).

**Rationale**: FR-043 and the clarification. The trigger paths are a superset of FR-043's
(they add `Dockerfile` and `appsettings.json`, which change what the documented install
runs). A path-filtered workflow is GitHub's own
mechanism; no third-party paths-filter action is needed.

**Risk**: if branch protection ever requires `docs-procedures` as a status check, PRs that
skip it stay pending. Recorded in the plan; the job is not to be made required.

## OD-11 — Links from the product and the release notes point at the published docs

**Decision**: A single docs base URL (`https://trungnd1985.github.io/quick-builder/`, held as
`QbDocsBaseUrl` in `Directory.Build.props` and written into the Host's assembly metadata,
like `QbUpgradeFloor`) is combined with the build's `major.minor`.
`InstallationGate.Guide` becomes `{base}{major.minor}/upgrade/#…`; `TEMPLATE.md` and the key
links in release notes use `{base}{major.minor}/…`; the compose file comment and `README.md`
point at the site. Only the URL in the message changes; the wording and the refusal behaviour
do not.

**Rationale**: today's links point at `main` of a private repository: unreachable for
operators, and describing an unreleased version. The spec's "no product behaviour change"
boundary is respected: a link target is documentation, and FR-004 moving the pages would
otherwise break it.

## OD-12 — Runnable documentation: marked blocks, extracted and executed

**Decision**: Procedures are executed from the Markdown itself. An HTML comment immediately
before a fenced block marks it (invisible on GitHub and on the site):

```markdown
<!-- qb:run procedure=install step=2 -->
```bash
docker compose up -d
```
```

`<!-- qb:file procedure=nginx path=nginx/quickbuilder.conf -->` marks a block whose content
is written to a file before the procedure's commands run. The runner (`qb-docs snippets`,
driven by a SmokeTests category `Procedures`) concatenates a procedure's steps in order and
executes them with bash in a temporary directory. A closed list of **substitutions**
([contracts/runnable-snippets.md](contracts/runnable-snippets.md)) is the only difference
from what an operator runs: the image under test, the release compose file instead of a
download, and test certificates instead of ACME. After the steps, assertions written in the
test (not in the docs) check the outcome.

**Rationale**: FR-008/FR-020/FR-026 require the documented commands themselves to work; a
separate script would drift. HTML comments keep the Markdown plain and readable (FR-002).

**Alternatives considered**: VitePress `<<< @/file` includes — not rendered on GitHub, so the
plain-text docs would lose the commands. Rejected.

## OD-13 — What each procedure proves

| Procedure | Runs | Then asserts |
|---|---|---|
| `install` | Prerequisite check commands, download (substituted), secret setup, `up`, setup-code retrieval | The reference journey from the smoke suite: setup wizard, create page, publish, view. |
| `backup-restore` | On an installation seeded with a published page, a draft, a collection item and an image: the backup steps; `down -v`; the restore steps in a fresh compose project | Same smoke journey on the restored project, plus the seeded page, draft, item and image are present and identical (bytes hash). |
| `nginx`, `caddy`, `traefik` | Writes the example files, starts the proxy beside the reference install, applies the documented `Hosting__KnownProxies` | Two sites on two host names answer over HTTPS (`--resolve` to the proxy, trusting the test CA); setup and sign-in succeed with `Identity__AllowInsecureHttp=false` (the product saw `https` from the proxy); the sign-in security event records the test client's address, not the proxy's. One variant checks `Hosting:BasePath=/cms` and `/robots.txt` forwarding. |

Test certificates: generated at test time with .NET `CertificateRequest` (a throwaway CA and
a leaf for the test host names); no new dependency. Proxy versions are pinned image tags in
the examples and stated in the docs (FR-021).

## OD-14 — The repository is private: publishing target is configurable

**Finding**: the repository is private, and `promote.yml` notes the account is on GitHub
Free. GitHub Pages is not available for private repositories on GitHub Free, and a private
repository's release downloads are not reachable by operators either.

**Decision**: The publish step pushes to the `gh-pages` branch of the repository named by
the variable `DOCS_PAGES_REPOSITORY` (default: this repository) using `DOCS_DEPLOY_KEY` when
set (a deploy key with write access to that repository), otherwise `GITHUB_TOKEN`. The site
base URL follows from the target. This keeps the clarified choice (public site on the code
host's pages service) and works either when this repository becomes public or by pointing
at a separate public docs repository. **Until one of those is true, publish has nowhere to
go**: `promote.yml` fails the docs step with a message naming the variable, after the
release itself has been published (so the release is not blocked by hosting setup), and
the plan's Risks section lists it for the maintainer.

## OD-15 — Information architecture

**Decision**: `docs/operators/`:

```text
index.md                      start page: one link per task (FR-001, SC-009)
install.md                    prerequisites → install → first admin → next steps
configure.md                  how configuration works (file, environment, secrets) + link
reverse-proxy/index.md        what the proxy must do, checks, common mistakes
reverse-proxy/nginx.md | caddy.md | traefik.md
backup-restore.md             full backup, verify, restore, backup vs site export
upgrade.md                    paths, before/during/after, rollback, refusals
upgrade-notes.md              generated: every published release's upgrade notes, newest first
troubleshooting.md            by symptom
security-checklist.md         items: check / verify / fix
operations/                   remaining feature topics moved from docs/operations.md
                              (media storage, sites and host names, publishing and caching,
                              collections, forms and email, identity and recovery, …)
reference/configuration.md    generated (OD-05)
reference/notes/<area>.md     hand-written notes inlined by the generator
reference/commands.md         host commands (migrate, identity setup-code, …)
reference/health.md           /health/live, /health/ready and their statuses
```

`docs/configuration.md` and `docs/operations.md` are removed once their content is moved
(FR-004, SC-010); `docs/contributing.md` gains a "Writing operator docs" section.

## OD-16 — Upgrade notes in the site

**Decision**: `qb-docs` adds `upgrade-notes generate`, which runs the 019 notes assembler for
each released version folder in `docs/releases/<version>/` (from git tags, oldest supported
first) and writes `docs/operators/upgrade-notes.md` with only the "Upgrade notes" and
"Supported upgrade path" sections of each, newest first, each under an anchor
`#<version>`. The release run regenerates it before the docs build, so the published site of
a line contains its own release's notes.

**Rationale**: FR-028. The release pages of a private repository are not reachable, and even
when public, an operator crossing several releases should read them in one place in order.

## OD-17 — Milestone upgrade sections

**Decision** (clarification 4): every "Upgrading from/to feature NNN" section of
`docs/operations.md` is classified in [data-model.md §5](data-model.md#5-milestone-section-disposition):
features in `docs/releases/0.1.0/` (shipped in 0.1.0, so no operator ever crossed that
milestone) are deleted; features in `docs/releases/unreleased/` (020–023) have any operator
step moved into their release-note fragment's `manual`, `background` or `caches` entries,
then the section is deleted. A lint rule (`lint` → `milestone-reference`) fails on
"Upgrading from feature" / "Upgrading to feature" in `docs/operators/`.

## OD-18 — Troubleshooting coverage is checked against things the product can say

**Decision**: `qb-docs lint` checks that:
- each `GateOutcome` refusal (`RefuseNewer`, `RefuseTooOld`) and each readiness status the
  health endpoint can report (`MigrationsPending`, `WaitingForDatabase`, …) has an entry in
  `troubleshooting.md`, matched by an explicit anchor list in the tool;
- every root settings section has a troubleshooting entry "Startup stops naming
  `<Section>:…`" or is covered by the general "startup stops on a configuration error"
  entry, which explains that the message names the key and links to the reference.

Messages are findable by the key they name (FR-031 allows "by the message text or the
setting it names"); startup validation messages already name the key (001 FR-028).

## OD-19 — Settings mentioned in prose must exist

**Decision**: `qb-docs lint` scans `docs/operators/**/*.md`, `docker-compose.yml` and the
proxy examples for backticked `Section:Key` tokens and `Section__Key` environment names
whose first segment is a declared section, and fails on any that is not a declared setting
(FR-003). It also fails on hand-written notes that contain a table row with a backticked
key (FR-013: notes must not restate the generated facts).

## Deferred (recorded, not built)

- A Roslyn analyzer for literal configuration reads (OD-03 alternative).
- Docs for orchestrators other than compose (spec Out of Scope).
- A "settings list" host command for operators who want the reference from the image
  itself.
