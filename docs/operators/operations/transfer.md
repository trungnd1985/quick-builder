# Site export and import

Export one site to a `.qbsite` archive and import it into the same or another installation.
For backing up a whole installation, see [Back up and restore](../backup-restore.md).

An Administrator exports a site from **Sites → (site) → Export and import**, and imports an
archive from **Sites → Import site** (as a new site), or from the same tab (into the site, or to
restore it). An archive is one `.qbsite` file holding the site's settings, pages, components,
menus, redirects, design, collections, items and media, with their drafts, published versions
and pending schedules. Exports are kept for 7 days in the configured storage, then deleted.

The command line does the same without the admin, for scripted backups and moves. It runs
inside the container; paths are the container's, so mount a directory for archives, such as
`./backups:/backups`:

```sh
docker compose exec -T app dotnet QuickBuilder.Host.dll sites export --site acme.example --out /backups/acme.qbsite
docker compose exec -T app dotnet QuickBuilder.Host.dll sites export --all --out /backups
docker compose exec -T app dotnet QuickBuilder.Host.dll sites check /backups/acme.qbsite --into acme.example
docker compose exec -T app dotnet QuickBuilder.Host.dll sites import /backups/acme.qbsite --as-new --name "Acme copy" --host-name copy.example
docker compose exec -T app dotnet QuickBuilder.Host.dll sites import /backups/acme.qbsite --into acme.example --on-conflict skip
docker compose exec -T app dotnet QuickBuilder.Host.dll sites import /backups/acme.qbsite --restore acme.example --confirm "Acme" --pre-restore-out /backups/acme-before.qbsite
```

`--site`, `--into` and `--restore` take a site id or one of its host names, disabled sites
included. `--out` never overwrites a file. `import` checks the archive first and stops when the
check has warnings, unless `--accept-warnings` is given. `--json` prints the transfer as one JSON
object on the last line. Exit codes: `0` done, `1` the transfer failed (for `export --all`: at
least one site did), `2` the archive was refused, `3` pending migrations, `4` unknown site,
`5` the site already has an export or import running, `6` confirmation needed (warnings without
`--accept-warnings`, or `--confirm` missing or different), `64` usage.

## Nightly backups

`export --all` writes one archive per site, named `<primary host>-<UTC time>.qbsite`, and keeps
going when one site fails. A cron entry on the Docker host that keeps the last 14 archives of
each site:

```sh
# /etc/cron.d/quickbuilder-backups
15 2 * * * root cd /srv/quickbuilder && docker compose exec -T app dotnet QuickBuilder.Host.dll sites export --all --out /backups >> /var/log/quickbuilder-backups.log 2>&1; for host in $(ls /srv/quickbuilder/backups/*.qbsite | sed -E 's#.*/(.*)-[0-9]{8}T[0-9]{6}Z\.qbsite#\1#' | sort -u); do ls -1t /srv/quickbuilder/backups/"$host"-*.qbsite | tail -n +15 | xargs -r rm --; done
```

An archive is a full copy of the site's content: keep backups where only operators can read
them. It holds no passwords, sessions, submissions or member accounts; authors appear by name.

## Restoring a site

A restore makes a site match an archive exported from it: what the archive does not hold is
removed, and what changed since comes back. Host names and the enabled state are kept. Before
it changes anything, the site as it is now is exported and kept for 7 days, so the restore
itself can be undone by restoring that export. In the admin, the restore lists what it removes,
brings back and changes, and asks for the site's name; on the command line, `--confirm` takes
it. While an import or restore applies, the site's content changes are paused for every member
(the admin shows why, and the API answers `423 site.import_in_progress`); visitors keep seeing
the site as it was until the import commits.

## Moving a site to another installation

Export it on the old installation, copy the archive, and import it on the new one with
`--as-new`. Host names that are free on the new installation come along; give `--host-name`
when none is. Members are not carried: authors of the archive's content appear by name, as
historical authors, and the new installation's Administrators grant access to the new site.
Point DNS at the new installation once the import has finished.
