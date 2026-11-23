# Forms and email

Forms are blocks on pages. What visitors send is kept per site and read under **Submissions**
in the site's admin; every site role can read, mark and export them, and Designers and
Administrators can delete them.

## Setting up email

1. Set the `Email:Smtp:*` keys ([configuration](../reference/configuration.md#email)), with the password
   from a secret, and `Hosting:PublicUrl` so notifications link to the submission.
2. Restart. Startup stops with the key's name if a value cannot work.
3. Sign in as an Administrator, open **Settings** and choose **Send test email**. It goes
   to your own address. When the mail server refuses, its reply code and text are shown
   (never the password); `5xx` replies are permanent, `4xx` temporary.

## How notifications are sent

A submission with recipients is stored with its notification waiting, in the same
transaction. Each instance runs one worker that looks for due notifications every 5 seconds
and sends one email to all of a form's recipients. A temporary refusal or an unreachable
server is tried again after 1, 5, 15, 30 and 60 minutes; after the sixth attempt, or at a
permanent refusal, the notification shows as failed with the server's reply on the
submission. The submission itself is never lost.

An email is sent at most once. Before it leaves, the notification is marked as sending for
two minutes; if the instance stops in that time, the notification shows as failed
(interrupted) instead of being sent again, because the server may already have it. Several
instances can run side by side: each notification is taken by one of them.

Logs name the submission, form and site, never a recipient, a value or the server's reply.

## Retention and spam

A form can delete its submissions after 30, 90, 180 or 365 days; the sweep runs hourly on
each instance and records each deletion's count in the submission log. Forms without a
period keep submissions until someone deletes them.

Every form carries a hidden trap field. A submission that fills it is kept in **Spam** for
30 days and never notified; an editor can restore it to the inbox, which sends its
notification then. A submission that fills the trap and fails the form's rules is dropped.
The same submission sent twice within 10 seconds is stored once. **Submissions** shows,
per form, how many were caught, dropped or over the rate limit in the last 30 days; a
growing "over the rate limit" count on a busy site usually means the reverse proxy is not
named in `Hosting:KnownProxies` (see [configuration](../reference/configuration.md#forms)). No visitor
address is stored, except a hash in the rate-limit counts, which are deleted after an hour.
