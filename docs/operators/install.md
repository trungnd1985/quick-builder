# Install

This page takes a clean server to a signed-in administrator with the reference
`docker-compose.yml`: QuickBuilder and PostgreSQL, nothing else. Plan on about fifteen
minutes, most of it downloading images.

## Prerequisites

| | |
|---|---|
| **Machine** | Linux on x86-64 (`linux/amd64`) or ARM64 (`linux/arm64`). macOS and Windows with Docker Desktop work for trying QuickBuilder out. |
| **Resources** | 2 CPU cores and 4 GB of memory for the reference setup, plus disk for the database and uploaded media (start with 10 GB). |
| **Software** | Docker Engine 24 or newer with the Docker Compose v2 plugin (`docker compose`, not `docker-compose`), and `curl`. |
| **Network** | Outbound HTTPS to `ghcr.io` and Docker Hub to pull images. Once running, QuickBuilder makes no outbound connections unless you configure a service that needs them. |
| **Ports** | QuickBuilder listens on `127.0.0.1:8080` on the server itself. Nothing is reachable from other machines until you put a [reverse proxy](reverse-proxy/index.md) in front. |

Check Docker and Compose are there:

<!-- qb:run procedure=install step=1 -->
```bash
docker version --format 'Docker {{.Server.Version}}'
docker compose version
```

## Install

Create a directory for the installation and work inside it. Everything QuickBuilder needs on
the server lives in this directory and in two Docker volumes.

<!-- qb:run procedure=install step=2 -->
```bash
mkdir -p quickbuilder
cd quickbuilder
```

Download the `docker-compose.yml` of the release you want. It names that exact release, so a
later `docker compose pull` never upgrades you by surprise. Replace `0.2.0` with the release
you are installing (the [upgrade notes](upgrade-notes.md) list them).

<!-- qb:run procedure=install step=3 substitute=release-compose -->
```bash
curl -fsSLO https://github.com/trungnd1985/quick-builder/releases/download/v0.2.0/docker-compose.yml
```

Generate a database password and keep it in a `.env` file next to the compose file. Compose
reads it from there; the compose file itself never holds a real password, so you can keep it
in version control. Never commit or share `.env`.

<!-- qb:run procedure=install step=4 -->
```bash
echo "POSTGRES_PASSWORD=$(openssl rand -hex 24)" > .env
chmod 600 .env
```

Without `openssl`, write `POSTGRES_PASSWORD=` followed by a long random password of your own
into `.env`. Choose it before the first start: PostgreSQL takes the password when it creates
the database, and changing `.env` later does not change it.

Start it. The first start pulls the images, creates the database and applies the migrations;
`--wait` returns once the health check reports ready.

<!-- qb:run procedure=install step=5 -->
```bash
docker compose up -d --wait
```

## Create the first administrator

A fresh installation has no accounts and no default password. It prints a one-time **setup
code** in its logs:

<!-- qb:run procedure=install step=6 -->
```bash
docker compose logs app | grep "SETUP CODE"
```

Open `http://127.0.0.1:8080/admin` in a browser on the server (or through an SSH tunnel:
`ssh -L 8080:127.0.0.1:8080 <you>@<server>`, then the same address on your machine). Enter
the code, then the first administrator's name, email address and password. The code works
once; after that it is no longer printed.

Finish setup before you forward logs anywhere or make the port reachable from other machines:
until setup is done, whoever reads the code first can claim the installation. If you can run
commands in the container but cannot read its logs, `identity setup-code` prints the same
code (see [Commands](reference/commands.md)).

## Check it

- `docker compose ps` shows `app` and `db` as `healthy`.
- `curl -s http://127.0.0.1:8080/health/ready` answers `"status":"Healthy"`.
- **Settings → About this installation** in the admin shows the release you installed.

If something is not right, see [Troubleshooting](troubleshooting.md).

## Next steps

1. **Put it on the internet**: a reverse proxy terminates TLS and forwards each site's host
   name to QuickBuilder. Follow [Reverse proxy and TLS](reverse-proxy/index.md) for Nginx,
   Caddy or Traefik.
2. **Set up backups** before anyone adds content: [Back up and restore](backup-restore.md).
3. **Work through the [security checklist](security-checklist.md)** before going live.
4. Optionally, [configure](configure.md) email for form notifications, a different media
   storage provider, or anything else in the [configuration reference](reference/configuration.md).
