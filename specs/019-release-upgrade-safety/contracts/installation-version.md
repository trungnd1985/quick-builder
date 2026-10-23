# Contract: Installation Version and the Startup Check

**Feature**: 019 · **Requirements**: FR-001, FR-006, FR-027 – FR-031 · **Research**: RL-01,
RL-07, RL-08, RL-21 · **Data**: [data-model.md](../data-model.md) §1–§2

## 1. Startup order

`Program.cs` and `ModuleMigrationRunner.RunAsync` run, in this order, before the HTTP pipeline
is built and before any hosted service starts:

1. Log `QuickBuilder {informationalVersion}` (first line of every start).
2. Wait for the database (unchanged, `Database:ConnectionRetryTimeout`).
3. Migrate `platform` under advisory lock `platform` (always, even with
   `Database:AutoMigrate=false`: it is one additive table and the check needs it).
4. **Check** (data-model §2) under the same lock. On refusal → §3.
5. If `AutoMigrate` is false and module migrations are pending → `MigrationsPending`
   (unchanged). The record is not touched.
6. Write `upgrading_to` when data-model §2 says so; release the `platform` lock.
7. Migrate each module (unchanged: per-module advisory locks).
8. Under lock `platform`: write `recorded_version`, clear `upgrading_to`; log
   `Database at {R}; upgraded to {V}` or `Database at {V}`.
9. `Ready`.

The `migrate` command runs steps 2–8 with `force: true` and exits 0, or 3 on refusal, or 1 on
any other failure (unchanged).

**As built**: steps 2–4 also run once **before the host is built**
(`InstallationPreflight`, called from `Program.cs`). That is what lets a refusal start
nothing: the host is then built without QuickBuilder's own hosted services. The runner
repeats the check after the host is built; it serves the `migrate` command and covers a
race between two starts. The preflight answers "no refusal" whenever it cannot decide (no
connection string, a database that never answers) and leaves those to the runner.

## 2. Interfaces (Shared.Infrastructure)

```text
ApplicationVersion            singleton, read once from Host assembly metadata
  Version : SemanticVersion   e.g. 0.2.0, 0.3.0-dev
  Revision : string?          git sha, null in local builds without it
  UpgradeFloor : SemanticVersion

IInstallationVersion          read side for modules (Identity's admin endpoint)
  Task<InstallationVersionInfo> GetAsync(CancellationToken)
    → (Version, Revision, DatabaseVersion: string?, UpgradingTo: string?)
    DatabaseVersion is null while the row does not exist or R = 0.0.0.

InstallationGate              pure decision of data-model §2: GateDecision(Outcome, MarkBefore, InsertRow, Refusal?)
PlatformRecord                migrates platform, reads, decides, under the platform advisory lock
InstallationPreflight         the same check before the host exists (public, called by Program)
```

No module may write `platform.database_version`; `ModuleBoundaryTests` gains the rule that only
`QuickBuilder.Shared.Infrastructure.Migrations` references `PlatformDbContext`.

## 3. Refusal

- `StartupPhase.Incompatible` (new).
- The Critical log line of data-model §2 is written once.
- `Program.cs` removes QuickBuilder's own hosted services before building the host (the web
  server's stays), and gives the built app one terminal middleware (`RefusalHost.Configure`:
  `app.Run`, matching the path by hand, no `Map*` endpoints, so no Minimal API is introduced).
  The authentication and authorization middleware ASP.NET Core would insert by itself are
  switched off for this pipeline, or Identity's fallback policy would answer 401 first. The
  same app is what `WebApplicationFactory` tests exercise:

| Request | Response |
|---|---|
| `GET /health/live` | `200 {"status":"Healthy"}` |
| `GET /health/ready` | `503` with the health report shape: `{"status":"Unhealthy","state":"Incompatible","detail":"<anonymous detail of data-model §2>","checks":[]}` |
| anything else | `503`, `text/plain`, `QuickBuilder cannot start on this database. The operator can find the reason in the logs.`, `Retry-After` absent, `Cache-Control: no-store` |

  With `Hosting:BasePath` set, paths are matched under it, as in the full host.
- Nothing is written to the database. The process stays up until stopped (the compose
  healthcheck reports unhealthy; no restart loop).

## 4. Admin API

```yaml
/api/v1/identity/installation:
  get:
    summary: The running release and the version recorded in the database (FR-006).
    x-capability: identity.installation.view     # Administrator only, installation scope
    responses:
      '200':
        content:
          application/json:
            schema:
              type: object
              required: [version, databaseVersion]
              properties:
                version:         { type: string, example: "0.2.0" }
                revision:        { type: string, nullable: true, example: "5f549ae1c2d3" }
                databaseVersion: { type: string, nullable: true, example: "0.2.0" }
                upgradingTo:     { type: string, nullable: true }
                releaseNotesUrl: { type: string, nullable: true,
                                   example: "https://github.com/trungnd1985/quick-builder/releases/tag/v0.2.0" }
      '401': { description: Not signed in }
      '403': { description: Not an Administrator }
```

- `releaseNotesUrl` is null for `-dev` builds. It is a constant link pattern built from the
  version; the server makes no outbound request (Principle II, offline).
- Capability registration in `specs/002-identity-setup/contracts/authorization.md` §2:
  `identity.installation.view`, scope Installation, Administrator ✓, Designer ✗, Editor ✗.

## 5. Admin UI

Settings → **About this installation** (Administrators only): "QuickBuilder 0.2.0
(5f549ae)", "Database: 0.2.0", and "Release notes" linking to `releaseNotesUrl`. If
`upgradingTo` is non-null (only reachable while a migration is running elsewhere), it shows
"Upgrade to {upgradingTo} in progress".

## 6. Tests

| Test | Proves |
|---|---|
| `InstallationGateTests` (unit) | every row of data-model §2, including `-dev` and `-rc` versions, floors, `M` handling |
| `InstallationStartupTests` (integration) | fresh, pre-release (baseline-shaped DB), same, patch-down, newer-minor refusal, too-old refusal, interrupted upgrade resumed; refusal writes nothing (row counts and `pg_stat` counters) |
| `RefusalHostTests` (integration) | the three responses of §3, base path, no hosted service registered |
| `MigrateCommandTests` (integration) | exit code 3 on refusal, 0 on a second run with zero writes |
| `InstallationEndpointTests` (integration) | 200 for Administrators, 403 for Designer and Editor, 401 anonymous; listed in `EndpointCatalog` |
| `ApplicationVersionTests` (unit) | parsing informational versions, ordering, floor |
