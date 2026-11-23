# Quickstart results: Operator Documentation

**Feature**: 026 · **Run on**: 2026-11-23, Windows 11 with Docker Desktop (Engine 29.8.2),
.NET 10.0.401, Node 24.19.0 · **Image**: built from this branch (`quickbuilder:docs-test`)

## Q1 — The reference is complete and current

| Check | Result |
|---|---|
| `qb-docs settings check` | Pass: "The configuration reference is current (67 settings)". For review: 3 framework/not-a-setting/computed exclusion groups, 4 computed defaults (`Storage:Local:Path`, `Media:AllowedTypes`, `Media:Variants:Widths`, `Media:Variants:MaxConcurrent`). |
| Unit tests `QuickBuilder.UnitTests.Docs` (catalog rules, derivation, reference, computed defaults, lint, snippets, upgrade notes, publishing, literal reads) | 68 passed |
| Integration test `EveryReadSettingIsDeclared` (default, Azurite, S3 emulator) | Pass |
| Integration test `ValidationMessagesNameTheirKeyTests` (42 invalid values) | Pass, after naming the key in the Database, Outbox and Cache range messages (8 messages reworded; behaviour unchanged) |

The first run of `EveryReadSettingIsDeclared` found real gaps, all resolved:
- **Read but not declared**: OpenTelemetry and ASP.NET Core keys and Serilog's own sections
  were added to the framework allow-list with reasons. Computed or static properties the binder
  probes are now `computed` exclusions.
- **Declared but not read at startup**: email, forms and site-transfer options. Resolved by
  resolving every section's options as the product does, and by counting a section listing as
  a read.

## Q2 — An undocumented setting fails

| Step | Result |
|---|---|
| 1. `public int Probe` on `DatabaseOptions` without an attribute | `settings check` exit 1: `Database:Probe: no [Setting], [SettingsGroup] or [NotASetting] — describe it for operators or say why it is not a setting` |
| 2. Add `[Setting("A probe.")]` | Exit 1: `docs/operators/reference/configuration.md: out of date at line 28` with the committed and generated lines and `Run: dotnet run --project tools/QuickBuilder.Docs -- settings generate` |
| 3. `settings generate`, then `settings check` | "current (68 settings)"; reverted |
| 4. `builder.Configuration["Database:Hidden"]` in `Program.cs` | **The boot observation passed: a gap.** Reads made before the host is built are not observed. Closed by the unit test `LiteralConfigurationReadsTests`, which then failed with `src/QuickBuilder.Host/Program.cs:94: reads 'Database:Hidden', which is not a documented setting`; reverted (research OD-03, setting-attributes.md §6). |
| 5. Hand-edit the `Database:AutoMigrate` default to `false` | Exit 1 with the line, both versions and the generate command; reverted |

## Q3 — The release-note key comparison survives the move

`qb-release notes --version 0.2.0 --previous v0.1.0` assembles. Its Configuration section lists
exactly `Preview:LinksValidFrom`, `SiteTransfer:MaxUploadSizeBytes` and `Trash:RetentionDays`
as added (from fragments 020–022) and nothing removed. The unit test that every v0.1.0 key keeps
its spelling passes. `ConfigurationKeysTests` (old path at v0.1.0, new path after, deprecated
keys must be announced) pass.

## Q4 — Site builds, links hold, lint passes

| Check | Result |
|---|---|
| `qb-docs lint` | "Operator docs pass 12 check(s)" |
| `npm run build` | build complete; no dead links |
| No third-party scripts or styles in `.vitepress/dist` | OK |
| A broken link added to `install.md` | Build fails: `Found dead link ./nowhere in file install.md`; reverted |
| `docs/operations.md`, `docs/configuration.md` | Removed; `no-old-paths` passes |

**SC-009, click paths from the start page**:
- one click: install, configure, reverse proxy, back up and restore, upgrade, troubleshooting,
  security checklist and the configuration reference (each linked from `index.md`);
- two clicks: upgrade notes, the three proxy pages, commands, health endpoints and every
  operations page (sidebar or a link on the first page).

## Q5 — Documented procedures work

| Procedure | Result | How |
|---|---|---|
| `install` | Pass (16 s) | C# `Procedures` test, Git Bash |
| `nginx` | Pass (32 s) | C# test; also run by hand in a Linux container: HTTPS on both sites, setup over HTTPS through the proxy, admin API 404 on a site host, sign-in event from `172.30.0.1` (not the proxy `172.30.0.10`), 5 MB upload 201, HTTP 301 |
| `caddy` | Pass | C# test, and by hand (HTTP 308) |
| `traefik` | Pass after a fix | Traefik 3.3.2 could not reach Docker Engine 29 ("Error response from daemon"). The page now pins 3.6.2 and explains why. |
| `proxy-basepath` | Pass | C# test; by hand: site under `/cms/`, `/robots.txt` at the root answers `User-agent: *` |
| `backup` + `restore` | Pass in Linux; not in Git Bash | In a Linux container (same commands, Docker socket): published page back, image byte-identical, unpublished draft identical, sign-in works, ready. Under Git Bash on Windows, Docker Desktop maps `/tmp/...` bind mounts to its VM, so backup files are split across two places. The page already tells Windows operators to use WSL. CI runs on Linux. |

Two findings fixed in the pages along the way:
- `COMPOSE_FILE` uses `;` on Windows, so the proxy pages now also set `COMPOSE_PATH_SEPARATOR=:`.
- A bare `<image>` placeholder broke the VitePress build; it is now in inline code.

Not covered by the tests:
- **Seeded content** is a published page, a draft and an image. Collection items are not
  seeded.
- **Restoring into an older release** is not tested. It needs the previous release's image,
  which the release pipeline has and a branch does not.

## Q6 — Path filter

Not run: it needs a pull request on GitHub. `docs-procedures.yml` triggers on
`docs/operators/**`, `docker-compose.yml`, `Dockerfile`, `src/QuickBuilder.Host/appsettings.json`
and itself.

## Q7 — Publishing

Not run: it needs tags, the Release and Promote workflows, and a public Pages target
(T087, T095). `publish prepare` is covered by unit tests:
- it replaces only its own line;
- the root redirect moves only for the highest release;
- it creates `.nojekyll`;
- lines sort numerically (`0.10` above `0.9`);
- pre-releases are refused.

The repository is private on GitHub Free; before 0.2.0 the maintainer must make it public or
set `DOCS_PAGES_REPOSITORY` and `DOCS_DEPLOY_KEY` (plan Risks, OD-14).

## Q8 — Product links

`InstallationGateTests` pass. The refusal log line links to
`{QbDocsBaseUrl}{major}.{minor}/upgrade#database-newer` or `#database-too-old`, never to
github.com, and both anchors exist in `upgrade.md` and `troubleshooting.md`.

## Q9 — Fresh reader

Not run: it needs a person new to the project (T093).

## Whole-repository checks

| Check | Result |
|---|---|
| `dotnet build QuickBuilder.slnx -c Release` | No warnings or errors |
| `dotnet format --verify-no-changes` | Clean |
| Unit tests | 2,721 passed (before `LiteralConfigurationReadsTests`; 2 more since) |
| Architecture tests | 66 passed |
| Integration: startup, versioning, configuration | 100 passed, 3 skipped (existing skips) |
