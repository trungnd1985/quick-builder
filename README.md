# quick-builder

A self-hosted website builder: several sites in one installation, a visual page editor,
a media library, and publishing.

## Install a release

Each [release](https://github.com/trungnd1985/quick-builder/releases) has a
`docker-compose.yml` that names its exact version. Download it into an empty folder, change the
database password in it, and run:

```bash
docker compose up -d
docker compose logs app    # the one-time setup code
```

Then open http://127.0.0.1:8080/admin and create the first administrator. To upgrade, read the
release notes, back up, and change the version in that file:
[Upgrading and rolling back](docs/operations.md#upgrading-and-rolling-back).

Until the first release (0.1.0) is published, there is no image to download yet: run it from
this checkout instead, as below.

## Run it from this checkout

You need [Docker Desktop](https://www.docker.com/products/docker-desktop/) and
[PowerShell 7](https://aka.ms/powershell) (on Windows, open "PowerShell 7", not "Windows
PowerShell").

```powershell
./qb.ps1
```

That builds and starts QuickBuilder, waits until it is ready, prints the one-time **setup
code**, and opens the admin at http://127.0.0.1:8080/admin. Enter the code and create the
first administrator.

Give your sites host names ending in `.localhost`, such as `shop.localhost`: browsers send
those to your own computer, so http://shop.localhost:8080 shows the site's published pages
with nothing else to set up.

| Command | What it does |
|---|---|
| `./qb.ps1` | Build, start and open the admin |
| `./qb.ps1 stop` | Stop. Your data is kept. |
| `./qb.ps1 code` | Show the setup code again |
| `./qb.ps1 logs` | Follow the log |
| `./qb.ps1 dev` | Work on the code: backend and admin with hot reload (needs the .NET 10 SDK and Node 24) |
| `./qb.ps1 cloud` | Two instances sharing S3 storage (`-Azure` for Azure Blob) |
| `./qb.ps1 test` | Run every test (`-Quick` skips the slow integration tests) |
| `./qb.ps1 seed` | Fill a site's media library with 1,000 generated images (`-Count` to change it) |
| `./qb.ps1 reset` | Delete everything and start over |

On macOS and Linux, run `pwsh ./qb.ps1`. Without PowerShell,
`docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build` builds and
starts it too; the setup code is in `docker compose logs app`. (`docker-compose.yml` alone runs
the published image.)

## More

- [Operations](docs/operations.md): running, upgrading, backups, storage providers
- [Configuration](docs/configuration.md): every setting
- [Contributing](docs/contributing.md): the code, tests and conventions
