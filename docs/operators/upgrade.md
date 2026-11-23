# Upgrade

QuickBuilder is upgraded by replacing the image: the new release migrates the database when it
starts, and nothing else is needed unless a release's [upgrade notes](upgrade-notes.md) say
so. Every release is tested this way before it is published, from the release before it and
from the first release of its major version, on a database that earlier release wrote.

## Supported upgrade paths {#supported-upgrade-paths}

- Any release upgrades directly from **any earlier release of the same major version**, and
  from the last minor release of the previous major version.
- Throughout 0.x, every 0.x release upgrades from every earlier one.
- To cross more than that, upgrade in steps: first to the last minor release of your major
  version, then to the next major.
- There is no downgrade. Going back means restoring a backup (see [Rolling back](#rolling-back)).

Releases follow semantic versioning: a patch release (0.3.1 → 0.3.2) only fixes bugs and never
changes stored data; a minor release (0.3 → 0.4) may add features and migrations; breaking
changes to settings or behaviour come only in a major release and are listed in its notes.

## Before you upgrade

1. **Find the release you run**: **Settings → About this installation** in the admin
   (Administrators), or the first line the application logs at every start, for example
   `QuickBuilder 0.2.0 (5f549ae1c2d3)`.
2. **Read the [upgrade notes](upgrade-notes.md) of every release after yours**, up to the one
   you are moving to. They list manual steps, configuration changes, and work done in the
   background after the upgrade. A section that does not apply says "None."
3. **Review configuration changes.** A setting the notes list as removed or renamed must be
   changed in your `docker-compose.yml` or `.env` before you start the new release; a removed
   setting that is still set is ignored, and a renamed one keeps its default until you rename
   it.
4. **Back up the database and the media together**, following [Back up](backup-restore.md#back-up).
   That backup is the only way back: migrations only move forward.

## Upgrade

1. In the `docker-compose.yml` you installed from, change the image to the exact release you
   want, for example `ghcr.io/trungnd1985/quickbuilder:0.3.0`. Prefer exact versions to moving
   tags (`0`, `0.3`, `latest`), so a `docker compose pull` never upgrades you by surprise.
2. Pull and restart:

   ```bash
   docker compose pull app
   docker compose up -d --wait
   ```

3. Watch it come back: `docker compose logs -f app` shows `QuickBuilder 0.3.0`, the
   migrations applied, then `Database at 0.3.0`.

Run every instance of an installation on the same release. Stop all of them before
upgrading: rolling upgrades without downtime are not supported.

If you apply migrations yourself (`Database:AutoMigrate` set to `false`), run
`docker compose run --rm app dotnet QuickBuilder.Host.dll migrate` after pulling and before
`up`; until you do, readiness reports `MigrationsPending`.

## After you upgrade

- `/health/ready` answers `200`, and **Settings → About this installation** shows the new
  release.
- Some releases rewrite data in the background once the application is ready (their notes
  say so under "Background work after upgrading"). Sites keep working while it runs.
- Caches refill on their own; the first visit of each page after an upgrade may be slower.
- Reload admin tabs that were open during the upgrade.

## When QuickBuilder refuses to start {#when-quickbuilder-refuses-to-start}

If the database belongs to a newer release, or is too old for this release to upgrade
directly, QuickBuilder changes nothing and refuses to start. `/health/ready` answers `503` with
`"state": "Incompatible"`, every other address answers `503`, and the log has one line saying
which release migrated the database, which release is running, and what to do. The container
stays up so you can read it; it does not restart in a loop.

### "Database was migrated by QuickBuilder X; this is Y" {#database-newer}

An older image was started after a newer one had upgraded the database, for example by
changing the image back without restoring a backup. Start release X (or newer) again, or roll
back properly as below. An older patch of the same minor (0.3.1 on a database 0.3.2
upgraded) is allowed and starts normally.

### "… upgrades from F or newer" {#database-too-old}

The database is too old for a direct upgrade. Start release F first, let it migrate, then
the release you want.

## Rolling back

Rolling back means restoring the backup you took before upgrading. Anything written after
the upgrade is lost, and **without that backup there is no way back**.

1. Stop the installation: `docker compose down`.
2. Restore the backup by following [Restore](backup-restore.md#restore). The restored
   `docker-compose.yml` names the release you ran before.
3. Check `/health/ready` and **Settings → About this installation**.

The release pipeline runs a restore on every release, from a real backup, so these steps are
the ones that are tested.
