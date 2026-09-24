# Operations

Running an installation: claiming it the first time, getting back in when nobody can sign
in, and what the application keeps. Every setting mentioned here is described in
[configuration](configuration.md).

## First-run setup

A fresh installation has no accounts and no default password. Until someone claims it,
every admin API call answers `428 Precondition Required`, and the admin interface shows
only the setup wizard.

To claim it you need the **setup code**, which the application prints in its logs at every
start while setup is pending:

```bash
docker compose logs app | grep "SETUP CODE"
```

The code looks like `ABCD-EFGH-JKMN`. Open `http://127.0.0.1:8080/admin`, enter the code,
and create the first administrator. Setup succeeds exactly once. After that the code stops
working and is no longer printed.

The code is in the logs because the only people who can read them are the people who run
the installation. That also makes the logs sensitive until setup is done:

- **Finish setup before you turn on log forwarding.** A collector that ships logs
  elsewhere would ship the code too, and whoever reads it first can claim the installation.
- **Finish setup before you widen the port binding.** The reference compose file publishes
  the app on `127.0.0.1` only, so nobody else can reach the wizard.

If you have access to the container but not the logs, `identity setup-code` prints the
same code (see below).

## Getting back in

These commands run inside the application container and are not reachable over the
network. They use the same configuration and database as the running app.

```bash
docker compose exec app dotnet QuickBuilder.Host.dll identity <command>
```

Add `-T` after `exec` when you run them from a script.

### `identity setup-code`

Prints the setup code while the installation is unclaimed.

| Result | Exit code |
|---|---|
| `Setup code: ABCD-EFGH-JKMN` | 0 |
| `Setup is complete; there is no setup code.` | 2 |
| `The database has pending migrations. Run the migrate command first.` | 3 |

### `identity recovery-link --email <address>`

Use this when no administrator can sign in, for example after a forgotten password. It
issues a set-password link for an existing, **active administrator**, and prints it:

```text
Set-password link (expires 2026-09-25T10:00:00Z):
https://cms.example.com/admin/set-password#token=…
```

If `Hosting:PublicUrl` is not set, the command prints only the path
(`/admin/set-password#token=…`). Put the address you use for the admin in front of it, for
example `http://127.0.0.1:8080`.

Open the link, choose a new password, and sign in. The link:

- works once, and expires after `Identity:SetPasswordLinks:Lifetime` (24 hours by default);
- replaces any earlier link for that account;
- signs the account out everywhere when it is used.

| Result | Exit code |
|---|---|
| The link | 0 |
| `Setup is not complete. Use 'identity setup-code' instead.` | 2 |
| Pending migrations | 3 |
| `No active administrator has that email address.` | 4 |
| Missing `--email`, or an unknown subcommand | 64 |

The link is printed on standard output only and never written to the logs. Treat it like
a password: whoever opens it first can set the administrator's password. Both commands
are recorded as security events (`operator_recovery_used`), including refused attempts.

To recover an account that is not an administrator, sign in as an administrator and issue
a link from **Users**.

## Passwords

The same rules apply wherever a password is chosen: the setup wizard, a set-password
link, and changing your own password.

- **Length**: 12 to 128 characters. Every character counts, spaces included, and a
  password is never trimmed or silently shortened: a longer one is refused, not cut.
- **Common passwords**: a password on the bundled list of common and breached passwords
  is refused, whatever its letter case. The list ships with the application, so the check
  works offline.
- **Not your email address or name**: a password equal to either is refused.
- **No composition rules**: there is no requirement for digits, symbols or mixed case.
  A few unrelated words make a strong password that is easy to remember.

Passwords are stored only as salted PBKDF2 hashes. The cost is
`Identity:Passwords:Pbkdf2Iterations`; see [configuration](configuration.md).

## Security events

Sign-ins, sign-outs, account changes, set-password links and the commands above are
recorded in the `identity.security_events` table. The table is append-only: the
application never changes or deletes an event, except for the retention sweep.

An hourly sweep deletes events older than `Identity:SecurityEvents:RetentionPeriod`
(365 days by default). The same sweep removes:

- sign-in throttle records that have been quiet for 24 hours;
- sessions that ended more than `Identity:Session:AbsoluteLifetime` ago;
- set-password links that expired more than 30 days ago;
- remembered browsers that were revoked or not used within
  `Identity:SignIn:KnownDeviceLifetime`.

If you need events for longer, for an audit for example, export them before the sweep
reaches them, or raise the retention period (up to 3650 days).

## Upgrading from the foundation release (feature 001)

Feature 001 had no authentication. After the upgrade:

- The installation starts **unclaimed**, even though its sites are kept. Claim it with the
  setup code as described above. Nothing is lost, and the first administrator sees every
  existing site.
- **Scripts that called the API anonymously stop working.** Every admin endpoint now
  needs a signed-in session, and every state-changing call needs the
  `X-QuickBuilder-Request: 1` header. Scripts must sign in through
  `POST /api/v1/identity/sign-in` and send the `qb_session` cookie on later calls.
- Public site traffic (`GET /` on a site's host name) and the health probes are
  unaffected.
