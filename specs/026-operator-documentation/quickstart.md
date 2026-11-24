# Quickstart: Validating Operator Documentation

**Feature**: 026 · **Spec**: [spec.md](spec.md) · **Contracts**: [contracts/](contracts/)

Run from the repository root on Linux, macOS or WSL with Docker, .NET 10 and Node 24.
Each scenario names the requirement or success criterion it proves. Record results in
`quickstart-results.md`.

## Prerequisites

```bash
dotnet build QuickBuilder.slnx --configuration Release
dotnet build tools/QuickBuilder.Docs --configuration Release
(cd docs/operators && npm ci)
alias qb-docs='dotnet tools/QuickBuilder.Docs/bin/Release/net10.0/qb-docs.dll'
```

## Q1 — The reference is complete and current (US2, US3; FR-009 – FR-016; SC-002, SC-003)

```bash
qb-docs settings check
```

Expected: exit 0; prints the exclusion list, computed defaults (`Storage:Local:Path`, …) and
lazily observed settings, each with a reason.

```bash
dotnet test tests/QuickBuilder.UnitTests --filter "FullyQualifiedName~SettingsReference"
dotnet test tests/QuickBuilder.IntegrationTests --filter "FullyQualifiedName~EveryReadSettingIsDeclared"
```

Expected: both pass.

## Q2 — An undocumented setting fails (US3 scenarios 1–3; SC-004)

1. Add `public int Probe { get; set; } = 3;` to `DatabaseOptions` without `[Setting]`.
   `qb-docs settings check` → exit 1, `Database:Probe: no [Setting], [SettingsGroup] or [NotASetting]`.
2. Add `[Setting("Probe.")]`. Check → exit 1, reference out of date, with the generate command.
3. Run `settings generate`; check → exit 0. Revert.
4. Add `var x = configuration["Database:Hidden"];` in `DatabaseOptions`' registration.
   The integration test fails naming `Database:Hidden`. Revert.
5. Edit a default by hand in `docs/operators/reference/configuration.md`. Check → exit 1
   with the diff. Revert.

## Q3 — Release-note key comparison survives the move (FR-017)

```bash
dotnet test tests/QuickBuilder.UnitTests --filter "FullyQualifiedName~ConfigurationKeys"
dotnet tools/QuickBuilder.Release/bin/Release/net10.0/qb-release.dll notes --version 0.2.0 \
  --previous v0.1.0 --sources '[]' --kind minor --out /tmp/notes.md
```

Expected: tests pass (including "reads the old path at v0.1.0"); notes build; the
"Configuration" section lists exactly the keys this feature's fragment declares added, and no
removals.

## Q4 — Site builds, links hold, lint passes (FR-001 – FR-003, FR-013, FR-031, FR-033 – FR-035, FR-044; SC-007, SC-009, SC-010)

```bash
qb-docs lint
(cd docs/operators && npm run build)
grep -rEl '<script src="http|<link[^>]+href="http' docs/operators/.vitepress/dist && echo FAIL || echo OK
```

Expected: lint exit 0; build succeeds; `OK`. Then break a link in `install.md` and rebuild:
the build fails naming the link. Revert.

Manually: from `index.md`, reach each US1–US8 task in ≤ 3 clicks (SC-009); `docs/operations.md`
and `docs/configuration.md` no longer exist (SC-010).

## Q5 — Documented procedures work (FR-008, FR-020, FR-026, FR-043; SC-001, SC-005, SC-006)

```bash
docker build -t quickbuilder:docs-test .
QB_SMOKE_IMAGE=quickbuilder:docs-test dotnet test tests/QuickBuilder.SmokeTests \
  --filter "Category=Procedures" --logger "console;verbosity=normal"
```

Expected: `install`, `backup-restore`, `nginx`, `caddy`, `traefik`, `proxy-basepath` pass.
Inspect one: `qb-docs snippets write --procedure nginx --dir /tmp/nginx …` produces files and a
`run.sh` whose commands match the page character for character outside the listed
substitutions.

## Q6 — Path filter (FR-043)

Open a draft PR changing only `src/Modules/Pages/…`: `docs-procedures` does not run; `docs`
and the settings tests do. Push a change to `docs/operators/install.md`: `docs-procedures`
runs.

## Q7 — Publishing (US9; FR-036 – FR-041; SC-008)

Against a scratch target (`DOCS_PAGES_REPOSITORY` = a throwaway public repository with Pages
on `gh-pages`):

1. Tag `v0.2.0-rc.1`: release run passes the docs steps; promote publishes the release and
   **skips** docs (pre-release).
2. Tag and promote `v0.2.0`: `gh-pages` gains `/0.2/`, `versions.json` lists `0.2 → 0.2.0`,
   root redirects to `/0.2/`. Header shows "Built from 0.2.0".
3. Merge a docs change to `main`: `/0.2/` is unchanged.
4. Promote `v0.2.1`: `/0.2/` replaced, header "Built from 0.2.1"; nothing else changed
   (`git diff --stat` on `gh-pages` touches only `0.2/` and `versions.json`).
5. Break a link and tag `v0.2.2`: `prepare` fails; no draft release, nothing published.
6. Unset `DOCS_PAGES_REPOSITORY` on a private repository without Pages and promote: release
   published, docs step fails with the message naming the variable.
7. Release notes link to `/<line>/upgrade` and to reference anchors that resolve.

## Q8 — Product links (OD-11)

Start the image against a database marked by a newer minor (the 019 upgrade test fixture):
the Critical log line links to `{QbDocsBaseUrl}<line>/upgrade#when-quickbuilder-refuses-to-start`,
and that anchor exists in `upgrade.md` (lint `troubleshooting-coverage`).

## Q9 — Fresh-reader check (SC-001)

Give the published `install.md` to someone new to the project with a clean VM. Time from
first command to signed-in administrator; record every guess they had to make. Target ≤ 15
minutes, zero outside help.
