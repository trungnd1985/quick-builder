# Contract: `qb-docs` command line

**Feature**: 026 · **Research**: OD-04, OD-05, OD-16, OD-18, OD-19

`dotnet run --project tools/QuickBuilder.Docs -- <command>` (in CI: the built
`qb-docs.dll`). Exit code `0` = success, `1` = check failed (findings printed), `2` = usage
or environment error. Output is plain text; every finding is one line
`<file or key>: <problem> — <fix>`, so CI logs read without tooling.

| Command | Does | Fails when |
|---|---|---|
| `settings generate [--out <path>]` | Writes the reference (default `docs/operators/reference/configuration.md`). | A catalog rule (setting-attributes.md §3, rules 1–7) is broken. |
| `settings check` | Generates in memory and compares with the committed file; prints the exclusion list, computed defaults and lazily observed settings. | Any rule 1–8 is broken. Message ends with: `Run: dotnet run --project tools/QuickBuilder.Docs -- settings generate`. |
| `settings keys` | Prints every declared key, one per line (used by `lint` and by tests). | — |
| `upgrade-notes generate [--through <version>]` | Writes `docs/operators/upgrade-notes.md` from `docs/releases/<version>/` for every released version up to `--through` (default: `VersionPrefix`). | A version folder has no assembled notes (fragment errors from `qb-release` are passed through). |
| `lint` | All checks in the table below. | Any finding. |
| `snippets list [--procedure <name>]` | Prints the procedures and their ordered steps. | Marker errors (data-model §6). |
| `snippets write --procedure <name> --dir <path> [--substitute name=value …]` | Writes the procedure's files and one `run.sh` (with `set -euo pipefail`) into `<path>`, applying substitutions. | Unknown procedure or substitution; a substitution the procedure needs is not supplied. |

## `lint` checks

| Check | Rule | Requirement |
|---|---|---|
| `settings-exist` | Backticked `Section:Key` and `Section__Key` tokens whose first segment is a declared section must be declared keys (lists/maps matched by prefix). Scans `docs/operators/**/*.md`, `docker-compose.yml`, `README.md`. | FR-003, OD-19 |
| `notes-restate` | Files in `reference/notes/` contain no table row starting with a backticked key, and no "default" sentence naming a declared key with a literal value. | FR-013 |
| `nav-pages` | Every page in the VitePress sidebar exists; every page under `docs/operators/` is in the sidebar (except `reference/notes/`). | FR-001, FR-003 |
| `start-page` | `index.md` links to install, configure, reverse-proxy, backup-restore, upgrade, troubleshooting, security-checklist and reference. | FR-001, SC-009 |
| `troubleshooting-coverage` | Required anchors exist in `troubleshooting.md` (list in the tool: each gate refusal, each readiness status, the FR-030 symptoms). | FR-029 – FR-031, OD-18 |
| `security-coverage` | Required anchors exist in `security-checklist.md` (the FR-033 items) and each item has the three labelled parts **Check**, **Verify**, **Fix**. | FR-033, FR-034 |
| `milestone-reference` | No "Upgrading from feature" / "Upgrading to feature" / "from feature NNN" text in `docs/operators/`. | FR-044 |
| `snippet-markers` | data-model §6 marker rules. | FR-008, FR-020, FR-026 |
| `example-secrets` | Fenced blocks contain no value for a `Secret` setting other than the placeholders `change-me…`, `<…>` or `${…}`. | Edge case "secret values in examples" |
| `proxy-version` | Each `reverse-proxy/<proxy>.md` states "Checked with <proxy> <version>" and its compose snippet pins the same image tag. | FR-021 |
| `no-old-paths` | No link to `docs/operations.md` or `docs/configuration.md` anywhere in the repository outside `specs/`. | FR-004, SC-010 |
| `upgrade-notes-current` | `docs/operators/upgrade-notes.md` equals a fresh `upgrade-notes generate` through `VersionPrefix`, so a release never finds it stale. | FR-028 |
