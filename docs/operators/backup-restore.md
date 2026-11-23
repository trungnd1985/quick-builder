# Back up and restore

A QuickBuilder installation is three things, and a backup needs all three, taken together:

| What | Where | Why it matters |
|---|---|---|
| **The database** | the `db-data` volume (PostgreSQL) | Sites, pages, collections, accounts, settings, history: everything except file contents. |
| **Media** | the `media-data` volume, or your S3 bucket / Azure container | The bytes of every uploaded file. The database only records which files exist. |
| **Configuration and secrets** | `docker-compose.yml` and `.env` next to it | Which release you run, your settings, and the database password the volume was created with. |

The database and media must come from **the same moment**. Restored from different moments,
nothing breaks, but files uploaded or deleted between the two copies are inconsistent: a
library entry whose bytes are missing shows as unavailable (its address answers `404`), and
bytes with no entry are removed by the orphan sweep after `Media:OrphanGracePeriod`.

These are shell commands for Linux and macOS. On Windows run them in WSL, or in Git Bash with
`MSYS_NO_PATHCONV=1` set, so paths like `/out` reach Docker unchanged.

## Back up

Run these in the installation directory (the one holding `docker-compose.yml`). They stop
the application for as long as the copy takes, usually under a minute, so the database and
media match exactly; the database stays up. Sites are unavailable while the application is
stopped, so schedule backups for a quiet time.

<!-- qb:run procedure=backup step=1 -->
```bash
BACKUP="$(cd .. && pwd)/quickbuilder-backup/$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$BACKUP"
PROJECT=$(docker compose config | sed -n 's/^name: //p' | head -n 1)
```

`PROJECT` is the Compose project name, which prefixes the volume names (`<project>_media-data`).

<!-- qb:run procedure=backup step=2 -->
```bash
docker compose stop app
docker compose exec -T db pg_dump -U quickbuilder -d quickbuilder --format=custom > "$BACKUP/quickbuilder.dump"
docker run --rm -v "${PROJECT}_media-data:/data:ro" -v "$BACKUP:/out" postgres:16-alpine \
  tar -czf /out/media.tar.gz -C /data .
cp docker-compose.yml .env "$BACKUP/"
docker compose start app
```

The backup directory now holds `quickbuilder.dump`, `media.tar.gz`, `docker-compose.yml` and
`.env`. Copy it off the server.

### With S3 or Azure Blob storage

The media lives in your bucket or container, not in `media-data`. Skip the `tar` line and
back up the bucket or container with your provider's tools (S3 versioning or replication,
Azure Blob soft delete or point-in-time restore) at the same time as the database dump. A
`media-variants/` folder in each site holds resized images; they need no backup, because
lost copies are made again on request.

### How often, and where

- **At least daily**, and always **before an upgrade**: migrations only move forward, so the
  backup you took before upgrading is the only way back.
- Keep several generations (for example 14 daily backups) somewhere other than the server.
- **Protect backups like the server itself.** They contain every account's email address,
  form submissions and the database password. Encrypt them at rest and limit who can read
  them.

### Verify a backup

A backup you have never restored is a guess. Check each dump can be read:

<!-- qb:run procedure=backup step=3 -->
```bash
docker run --rm -v "$BACKUP:/in:ro" postgres:16-alpine pg_restore --list /in/quickbuilder.dump > /dev/null
tar -tzf "$BACKUP/media.tar.gz" > /dev/null
echo "Backup $BACKUP can be read."
```

and restore one into a scratch directory every few months, following the steps below.

## Restore

Restore onto a server with Docker (see [Install](install.md#prerequisites)), in a new, empty
directory. The commands below restore the newest backup in `../quickbuilder-backup`; set
`BACKUP` to another backup directory to restore that one instead.

<!-- qb:run procedure=restore step=1 -->
```bash
BACKUP=$(ls -d "$(cd .. && pwd)"/quickbuilder-backup/* | sort | tail -n 1)
mkdir -p ../quickbuilder-restore
cd ../quickbuilder-restore
cp "$BACKUP/docker-compose.yml" "$BACKUP/.env" .
PROJECT=$(docker compose config | sed -n 's/^name: //p' | head -n 1)
```

The restored `docker-compose.yml` names the release the backup was taken with. Restore with
that release, then upgrade if you want a newer one.

Start the database alone, load the dump into it, and put the media back into a fresh volume:

<!-- qb:run procedure=restore step=2 -->
```bash
docker compose up -d --wait db
docker compose exec -T db pg_restore -U quickbuilder -d quickbuilder --no-owner --role=quickbuilder < "$BACKUP/quickbuilder.dump"
docker compose create app
docker run --rm -v "${PROJECT}_media-data:/data" -v "$BACKUP:/in:ro" postgres:16-alpine \
  sh -c "tar -xzf /in/media.tar.gz -C /data && chown -R 1654:1654 /data"
docker compose up -d --wait
```

`1654` is the `app` user of the QuickBuilder image;
`docker run --rm --entrypoint id <image> -u` prints it.

Then check: the same sites answer on the same host names once your reverse proxy points at
this server, **Settings → About this installation** shows the release in the backup, and the
media libraries show no unavailable files.

### Restoring into a different release

- **A newer release** than the backup: restoring is then also an upgrade. Restore with the
  backup's release as above, check it, then follow [Upgrade](upgrade.md).
- **An older release** than the backup: QuickBuilder refuses to start on a database a newer
  release has migrated, and changes nothing (see
  [Troubleshooting](troubleshooting.md#database-newer)). Use the backup's release or newer.

## Full backup or site export?

QuickBuilder can also export a single site to one `.qbsite` archive (see [Site export and
import](operations/transfer.md)). They answer different questions:

| | Full backup (this page) | Site export |
|---|---|---|
| **Covers** | The whole installation: every site, accounts, form submissions, settings, history | One site's content, design, media and schedules |
| **Accounts and submissions** | Yes | No: authors appear by name only, submissions are not included |
| **Restores** | The installation exactly as it was, on the same release | One site, into any installation of the same or a newer release |
| **Use it for** | Disaster recovery, upgrades, moving the whole installation | Copying or moving one site, undoing changes to one site, per-site archives |

Take full backups in any case; add site exports when you need to restore one site without
touching the others.
