# Health endpoints

Two endpoints answer on every host name, without signing in. Point your orchestrator, load
balancer or monitoring at them.

| Endpoint | Answers `200` when | Use it for |
|---|---|---|
| `/health/live` | the process can serve requests at all | restarting a hung container (liveness) |
| `/health/ready` | the installation should receive traffic: configuration valid, migrations applied, database and media storage reachable | sending traffic, and monitoring (readiness) |

They answer different questions on purpose: a database outage makes the installation not
ready, but restarting it would not help, so it stays live.

## What `/health/ready` returns

```json
{
  "status": "Healthy",
  "state": "Ready",
  "checks": [
    { "name": "postgres:pages", "status": "Healthy" },
    { "name": "storage", "status": "Healthy" }
  ]
}
```

`503` with `"status": "Unhealthy"` otherwise. `checks` lists one database check per module
and the media storage check. `detail`, when present, says what to do next; it never contains
version numbers or secrets.

## States {#states}

| `state` | Meaning | What to do |
|---|---|---|
| `Ready` | Everything is in order. | Nothing. |
| `WaitingForDatabase` | PostgreSQL is not reachable yet; QuickBuilder keeps retrying for [`Database:ConnectionRetryTimeout`](configuration.md#database-connectionretrytimeout). | [Troubleshooting](../troubleshooting.md#waiting-for-database) |
| `Migrating` | Migrations are being applied. | Wait; see [Troubleshooting](../troubleshooting.md#migrating). |
| `MigrationsPending` | [`Database:AutoMigrate`](configuration.md#database-automigrate) is `false` and this release's migrations have not been applied. | Run [`migrate`](commands.md#migrate). |
| `Failed` | Startup could not finish; `detail` and the log say why. | [Troubleshooting](../troubleshooting.md#failed) |
| `Incompatible` | The database belongs to a different release; nothing was changed, and every other address answers `503`. | [Upgrade](../upgrade.md#when-quickbuilder-refuses-to-start) |

The reference compose file's health check calls `/health/ready` every 10 seconds, so
`docker compose ps` shows `healthy` exactly when the installation is ready.
