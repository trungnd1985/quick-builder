# Commands

QuickBuilder's commands run inside the application container, with the same configuration and
database as the running application. They are not reachable over the network.

```bash
docker compose exec app dotnet QuickBuilder.Host.dll <command> [arguments]
```

Add `-T` after `exec` when you run a command from a script or cron. `migrate` is the
exception: it runs in a new container (`docker compose run --rm app …`), because the
application may not be running yet.

## `migrate` {#migrate}

Applies the database migrations of the release in the image, then exits. Needed only when
[`Database:AutoMigrate`](configuration.md#database-automigrate) is `false`; run it after
pulling a new release and before starting it.

```bash
docker compose run --rm app dotnet QuickBuilder.Host.dll migrate
```

| Result | Exit code |
|---|---|
| Migrations applied (or none were pending) | 0 |
| A migration failed; the log says which | 1 |
| The database belongs to a newer release, or is too old to upgrade directly; nothing was changed (see [Upgrade](../upgrade.md#when-quickbuilder-refuses-to-start)) | 3 |

## `identity setup-code` {#identity-setup-code}

Prints the setup code while the installation is unclaimed, for when you can run commands but
cannot read the logs.

| Result | Exit code |
|---|---|
| `Setup code: ABCD-EFGH-JKMN` | 0 |
| `Setup is complete; there is no setup code.` | 2 |
| `The database has pending migrations. Run the migrate command first.` | 3 |

## `identity recovery-link --email <address>` {#identity-recovery-link}

Use this when no administrator can sign in, for example after a forgotten password. It
issues a set-password link for an existing, **active administrator**, and prints it:

```text
Set-password link (expires 2026-09-25T10:00:00Z):
https://cms.example.com/admin/set-password#token=…
```

If [`Hosting:PublicUrl`](configuration.md#hosting-publicurl) is not set, the command prints
only the path (`/admin/set-password#token=…`). Put the address you use for the admin in front
of it, for example `http://127.0.0.1:8080`.

Open the link, choose a new password, and sign in. The link:

- works once, and expires after
  [`Identity:SetPasswordLinks:Lifetime`](configuration.md#identity-setpasswordlinks-lifetime);
- replaces any earlier link for that account;
- signs the account out everywhere when it is used.

| Result | Exit code |
|---|---|
| The link | 0 |
| `Setup is not complete. Use 'identity setup-code' instead.` | 2 |
| Pending migrations | 3 |
| `No active administrator has that email address.` | 4 |
| Missing `--email`, or an unknown subcommand | 64 |

The link is printed on standard output only and never written to the logs. Treat it like a
password: whoever opens it first can set the administrator's password. Both `identity`
commands are recorded as security events (`operator_recovery_used`), including refused
attempts.

To recover an account that is not an administrator, sign in as an administrator and issue a
link from **Users**.

## `media check-storage` {#media-check-storage}

Compares every site's media library with what the storage provider holds and lists the files
whose original is missing. See [Storage consistency
check](../operations/media.md#storage-consistency-check).

```bash
docker compose exec -T app dotnet QuickBuilder.Host.dll media check-storage
```

| Result | Exit code |
|---|---|
| Nothing missing | 0 |
| The check could not run (another is running, or storage failed) | 1 |
| Some files are missing; up to 100 `site/file` ids are printed | 3 |

## `sites export`, `sites check`, `sites import` {#sites}

Export a site to a `.qbsite` archive, check an archive, and import or restore one, without
the admin. Paths are the container's, so mount a directory for archives. See [Site export and
import](../operations/transfer.md) for every option and exit code.
