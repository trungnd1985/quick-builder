Outgoing email sends form notifications and the test email. Every email setting is
optional: leave `Email:Smtp:Host` empty and the installation sends no email at all.

Startup stops, naming the setting, when `Email:Smtp:Host` is set without a valid
`Email:Smtp:From`, or when a value is out of range. It also stops when `Email:Smtp:Security`
is `None` and a user name is set for a mail server on another machine: the password would
cross the network unencrypted. On the same machine (`localhost`, `127.0.0.1`, `::1`) that is
allowed.

Links in notifications point at the submission in the admin and are built from
`Hosting:PublicUrl` and `Admin:PathPrefix`, never from a request. With email on and
`Hosting:PublicUrl` empty, emails say "Open Submissions in the admin" without a link, and
startup logs a warning.
