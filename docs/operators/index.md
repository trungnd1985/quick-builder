# QuickBuilder operator documentation

QuickBuilder is a web builder with a built-in CMS that you run on your own server. These pages
are for the people who install and run it: they cover everything from a clean server to a
backed-up, upgraded and secured installation, without reading source code. They assume you
can use a shell, Docker and a text editor; nothing else.

| I want to… | Read |
|---|---|
| Install QuickBuilder and create the first administrator | [Install](install.md) |
| Change a setting, or keep a password out of files | [Configure](configure.md) and the [configuration reference](reference/configuration.md) |
| Put it on the internet with HTTPS (Nginx, Caddy or Traefik) | [Reverse proxy and TLS](reverse-proxy/index.md) |
| Back up an installation, or restore it on another server | [Back up and restore](backup-restore.md) |
| Move to a newer release, or go back | [Upgrade](upgrade.md) and the [upgrade notes](upgrade-notes.md) |
| Fix something that is not working | [Troubleshooting](troubleshooting.md) |
| Check an installation is safe before going live | [Security checklist](security-checklist.md) |

The [Operations](operations/sites.md) pages explain how individual parts behave (sites and host
names, media storage, publishing, forms and email, site export and import), and the
[Reference](reference/configuration.md) lists every setting, command and health status.

## Offline copy

These pages are plain Markdown files in the `docs/operators/` folder of every QuickBuilder
release's source download, so they can be read without internet access. The published site
shows the documentation of each release line; the footer says which release a page was built
from.
