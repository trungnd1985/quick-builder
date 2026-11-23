# Implementation Plan: Operator Documentation

**Branch**: `026-operator-documentation` | **Date**: 2026-11-22 | **Spec**: [spec.md](spec.md) | **Issue**: #26

**Input**: Feature specification from `specs/026-operator-documentation/spec.md`

## Summary

Turn the feature-by-feature operator material (`docs/configuration.md`, `docs/operations.md`)
into a task-oriented operator documentation site in `docs/operators/`, built with VitePress
and published per minor line to a `gh-pages` branch when a release is promoted.

The configuration reference stops being hand-written: each options class is annotated with
small operator attributes (`[SettingsSection]`, `[Setting]`, …, in `Shared.Kernel`), and a new
tool `qb-docs` generates the reference by reflection, taking defaults by binding the shipped
`appsettings.json` exactly as the product does. Two tests keep it true on every change: one
compares the committed reference with a fresh generation, the other boots the host with a
recording configuration provider and fails when the product reads a key nobody declared, or
declares one it never reads.

Install, backup-restore and the Nginx/Caddy/Traefik examples are written as Markdown with
invisible `qb:run` / `qb:file` markers; a smoke-test category extracts and executes them
against the image under test, on every release and on pull requests that touch the docs,
the compose file, the Dockerfile or `appsettings.json`. Upgrade notes for every release are
generated into the site from the 019 notes assembler; product and release-note links move
from `main` of the repository to the published site of the running minor line.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (attributes, `qb-docs`, tests); Markdown; TypeScript
for the small VitePress theme extension (version picker); bash for documented procedures.

**Primary Dependencies**: existing — ASP.NET Core options/binder, xUnit,
WebApplicationFactory, Testcontainers, the 019 `qb-release` notes assembler. New
(build-time only, not in the image) — VitePress 1.x with its default theme and local search
(OD-07). No new NuGet packages. Proxy images used by tests and examples: `nginx`, `caddy`,
`traefik` at pinned tags.

**Storage**: none in the product. Generated files are committed
(`docs/operators/reference/configuration.md`, `docs/operators/upgrade-notes.md`); the
published site lives on a `gh-pages` branch (data-model §4).

**Testing**: unit (`SettingsReferenceIsCurrent`, catalog rules, `ConfigurationKeys` dual
path, `qb-docs lint` rules, snippet extraction, `versions.json` upsert); integration
(`EveryReadSettingIsDeclared`, three configurations); smoke category `Procedures`
(install, backup-restore, nginx, caddy, traefik, proxy-basepath) against a real image with
compose.

**Target Platform**: GitHub Actions Ubuntu runners for builds and checks; GitHub Pages for
the published site; operators' Linux hosts with Docker for the documented procedures.

**Project Type**: documentation + build tooling around the existing web-service monolith.

**Performance Goals**: `settings check` and `lint` under 30 s in CI; the `docs` CI job under
3 min; each procedure under 10 min; no change to product performance budgets.

**Constraints**: docs readable as plain Markdown without building (FR-002); the published
site loads nothing from third parties (contracts/docs-publishing.md §1); product behaviour
and messages unchanged except doc link targets (OD-11); the 0.1.0 → 0.2.0 key comparison
must report only real differences (OD-05).

**Scale/Scope**: ~20 settings sections, ~110 settings (67 rows today plus undocumented
ones found by the boot check); ~25 operator pages; 6 executed procedures; 1,400 lines of
existing material to move.

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 design (below, unchanged).*

| Principle | How this plan complies |
|---|---|
| I. Modular monolith | Attributes are generic, behaviour-free types in `Shared.Kernel` (`Settings` namespace); no module-specific concept enters Shared. Each module annotates its own options classes; no module references another. `qb-docs` is a tool, referencing `QuickBuilder.Host` the way tests do; it is not a module and the architecture tests' module rules are unaffected. |
| II. Easy to install, operate, upgrade | This is the feature that makes "documented keys" enforceable and the 5-minute install documented and executed. No new configuration keys, no migrations. |
| III. Upgrades never lose data | Backup/restore is documented and executed end to end; upgrade notes for every release are in the site; milestone upgrade steps for 020–023 move into release fragments rather than being lost (OD-17). |
| IV. Pluggable infrastructure | Reference groups provider settings under their provider; backup guide covers S3/Azure by pointing at provider tools. No new runtime dependency. |
| V. Multi-site | Proxy examples cover several sites on several domains, admin host names, base path; tests assert the product sees forwarded host, scheme and address only from a named proxy. |
| VI. Content schemas | Not affected. `docs/contracts/` stays where it is (spec Out of Scope). |
| VII. Draft/published | Not affected. Backup-restore test asserts drafts and published content both survive. |
| VIII. Rendering/caching | Not affected; troubleshooting explains stale-content symptoms using existing behaviour. |
| IX. Secure by default | Security checklist (FR-033–035) with Check/Verify/Fix; secret settings marked and enforced by name rule (setting-attributes.md §3 rule 6); lint refuses real-looking secrets in examples; published site sends no reader data anywhere. A `SECURITY.md` is added if absent (spec assumption). |
| X. Tests | Integration test uses WebApplicationFactory + Testcontainers (no in-memory DB). Documented procedures run against the real image with the reference compose file. Each lint rule has unit tests. No critical-path code changes, so coverage thresholds are untouched. |
| XI. Simplicity | One new build-time dependency (VitePress), justified in OD-07 with alternatives. No plugin system; procedure substitutions are a closed list. Runtime product unchanged. Steps in the delivery order are merged without a feature flag. The flag rule covers incomplete runtime behaviour, and this feature changes none: each step changes only docs, build tooling and tests, plus one link constant (T060) and message wording (T097). Every intermediate state is complete in itself, because the docs are correct in the repository at each step and publishing begins only in step 7. |
| Technology constraints | No change to backend, data access, admin frontend or distribution. Node is used only in CI to build the docs; operators never run it. |
| Development workflow | Issue #26 → `specs/026-operator-documentation/` → branch `026-operator-documentation`. Operator impact below. |

**Operator impact**

- New configuration keys: **none**. Keys that existed but were undocumented become
  documented; they are listed as "added" in this feature's release-note fragment so the
  0.2.0 notes are truthful about the reference changing (OD-05).
- Migrations: **none**.
- New runtime dependencies: **none** (VitePress is build-time only).
- Changed: the link in the startup-refusal log message and in release notes now points at
  the published docs for the running minor line (OD-11). `docs/operations.md` and
  `docs/configuration.md` are replaced by `docs/operators/`.
- Upgrade notes: one fragment, `docs/releases/unreleased/26-operator-documentation.md`
  (kind `added`, no manual steps).

## Project Structure

### Documentation (this feature)

```text
specs/026-operator-documentation/
├── plan.md                         # this file
├── research.md                     # OD-01 … OD-19
├── data-model.md                   # catalog, generated files, site layout, milestone table
├── quickstart.md                   # Q1 … Q9
├── contracts/
│   ├── setting-attributes.md
│   ├── qb-docs-cli.md
│   ├── runnable-snippets.md
│   └── docs-publishing.md
├── checklists/requirements.md
└── tasks.md                        # /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── Shared/QuickBuilder.Shared.Kernel/Settings/
│   └── SettingAttributes.cs                  # new: the five attributes
├── QuickBuilder.Host/Configuration/
│   ├── *Options.cs                           # annotated
│   └── ExternalSettings.cs                   # new: Serilog keys
├── Shared/QuickBuilder.Shared.Infrastructure/
│   ├── **/ *Options.cs, StorageOptions + provider settings   # annotated
│   └── Migrations/Platform/InstallationGate.cs  # Guide link → published docs (OD-11)
└── Modules/<Module>/QuickBuilder.<Module>/*Options.cs        # annotated (Editor, Forms, Identity, Media, Sites)

tools/
├── QuickBuilder.Docs/                        # new: qb-docs
│   ├── Program.cs
│   ├── Settings/  (Catalog, DefaultsBinder, ReferenceWriter, FrameworkAllowList)
│   ├── Lint/      (one class per check in contracts/qb-docs-cli.md)
│   ├── Snippets/  (MarkerParser, ProcedureWriter, Substitutions)
│   └── UpgradeNotes/ (uses QuickBuilder.Release's assembler)
└── QuickBuilder.Release/
    ├── ConfigurationKeys.cs                  # dual path (OD-06)
    └── NotesAssembler.cs                     # {{docs}} variable, reference links

docs/
├── operators/                                # new site (OD-15)
│   ├── .vitepress/config.ts, theme/          # sidebar, version picker, "Built from"
│   ├── package.json, package-lock.json
│   ├── index.md, install.md, configure.md, backup-restore.md, upgrade.md,
│   │   upgrade-notes.md (generated), troubleshooting.md, security-checklist.md
│   ├── reverse-proxy/ index.md, nginx.md, caddy.md, traefik.md
│   ├── operations/ (moved topic pages)
│   └── reference/ configuration.md (generated), notes/*.md, commands.md, health.md
├── releases/TEMPLATE.md                      # {{docs}} link
├── releases/unreleased/26-operator-documentation.md
├── contributing.md                           # "Writing operator docs"
├── configuration.md                          # removed
└── operations.md                             # removed

tests/
├── QuickBuilder.UnitTests/Docs/              # catalog rules, reference current, lint, snippets, versions.json
├── QuickBuilder.UnitTests/Release/           # ConfigurationKeys dual-path test added
├── QuickBuilder.IntegrationTests/Configuration/EveryReadSettingIsDeclared.cs
└── QuickBuilder.SmokeTests/Procedures/       # Category=Procedures; test CA helper

.github/workflows/
├── ci.yml                                    # + job docs
├── docs-procedures.yml                       # new: path-filtered PR + workflow_call
├── release.yml                               # prepare: docs checks + build; + job procedures
└── promote.yml                               # + Publish the documentation

Directory.Build.props                         # + QbDocsBaseUrl
SECURITY.md                                   # new if absent
docker-compose.yml, README.md                 # links
```

**Structure Decision**: Product code changes are limited to attributes on existing options
classes, one new kernel file, one external-settings list and one link constant. Everything
else is a new tool (`tools/QuickBuilder.Docs`, following `tools/QuickBuilder.Release`), the
`docs/operators/` site, tests in the existing test projects, and workflow changes.

## Delivery order (each step independently mergeable behind no flag — docs and tooling only)

1. **Attributes + catalog + generator + `SettingsReferenceIsCurrent`** (US2, US3 core). Move
   `docs/configuration.md` prose into `reference/notes/`; generate; `ConfigurationKeys` dual
   path. The old file is removed in this step.
2. **Boot observation test** (US3, FR-015 second half). Fix every undeclared key it finds.
3. **Site skeleton + `docs` CI job + lint** (FR-001–003). Move `docs/operations.md` into
   `operations/` and task pages; milestone sections per data-model §5; remove old file; fix
   links (README, compose, template, `InstallationGate`).
4. **Install + backup-restore pages with markers + `Procedures` tests + `docs-procedures.yml`** (US1, US4).
5. **Reverse proxy pages and tests** (US6).
6. **Upgrade, upgrade-notes generation, troubleshooting, security checklist** (US5, US7, US8).
7. **Release and promote integration** (US9).

## Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Repository is private on GitHub Free: no Pages, release downloads unreachable by operators (OD-14) | Docs cannot be published; install guide's download URL fails for outsiders | Publish target configurable (`DOCS_PAGES_REPOSITORY`, `DOCS_DEPLOY_KEY`); docs step fails loudly after the release is published. **Maintainer decision needed before 0.2.0**: make the repository public or create a public docs repository. |
| Boot observation misses keys read only on unexercised paths | An undocumented key ships | Host commands exercised in the test; `ObservedLazily` is explicit and listed; deferred analyzer. |
| Framework allow-list hides a product key with a framework-looking prefix | Undocumented key | List is exact names or narrow prefixes, each with a reason; reviewed in PRs touching it. |
| Proxy behaviour differs across versions | Example breaks for an operator on another version | Pinned versions, stated per FR-021; examples re-run every release. |
| A proxy release stops working with a newer Docker Engine | Example fails for operators on current Docker | Found during implementation: Traefik before 3.6.1 cannot use Docker Engine 29's API. The Traefik page pins 3.6.2 and says why; the procedures run on every release, so the next such break shows up before publishing. |
| ACME cannot be exercised in CI | Certificate issuance part of examples untested | Substitution `test-certificates` is the only difference and is limited to certificate lines; documented on each page. |
| Path-filtered workflow as a required check would block PRs | Stuck PRs | Do not make `docs-procedures` a required check (OD-10). |
| Spurious added/removed keys in 0.2.0 notes from spelling changes | Release blocked or misleading notes | Generator reproduces today's spelling; Q3 verifies before merge. |
| CI time grows | Slower feedback | Procedures only on path changes and releases; matrix in parallel. |

## Complexity Tracking

No constitution violations. The changes to `Shared.Kernel` (five behaviour-free attributes)
are generic, not module-specific, and are within "Shared … MUST stay small".

## Deferred (recorded, not built)

- Roslyn analyzer for literal configuration reads (research OD-03).
- `settings list` host command so operators can read the reference from the image.
- Guides for Kubernetes and other orchestrators (spec Out of Scope).
