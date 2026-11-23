# Accounts and sign-in

Claiming a new installation, password rules, and the security events QuickBuilder keeps. To
get back in when nobody can sign in, see [Commands](../reference/commands.md#identity-recovery-link).

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
`Identity:Passwords:Pbkdf2Iterations`; see [configuration](../reference/configuration.md).

## Security events

Sign-ins, sign-outs, account changes, set-password links, the commands above, and site
changes that alter what the public can reach (`site_host_name_added`,
`site_host_name_removed`, `site_disabled`, `site_enabled`) are recorded in the
`identity.security_events` table. The table is append-only: the
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
