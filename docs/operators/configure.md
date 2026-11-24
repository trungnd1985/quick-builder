# Configure

Every QuickBuilder setting has a safe default, and the [configuration
reference](reference/configuration.md) lists all of them. This page explains how to set them.

## Where settings come from

QuickBuilder reads its settings from `appsettings.json` inside the image, and lets environment
variables override any of them. You never edit the image: you set environment variables, in
the `environment:` section of `docker-compose.yml` or in the `.env` file next to it.

The environment variable form of a setting replaces each `:` with two underscores:

| Setting | Environment variable |
|---|---|
| `ConnectionStrings:Default` | `ConnectionStrings__Default` |
| `Email:Smtp:Host` | `Email__Smtp__Host` |
| `Hosting:KnownProxies:<n>` (a list) | `Hosting__KnownProxies__0`, `Hosting__KnownProxies__1`, … |
| `Serilog:MinimumLevel:Override:<Namespace>` (a map) | `Serilog__MinimumLevel__Override__QuickBuilder` |

For example, to name a reverse proxy and turn email on:

```yaml
services:
  app:
    environment:
      Hosting__KnownProxies__0: "10.0.0.5"
      Email__Smtp__Host: "smtp.example.com"
      Email__Smtp__From: "website@example.com"
      Email__Smtp__UserName: "website@example.com"
      Email__Smtp__Password: "${SMTP_PASSWORD}"
```

Apply changes with `docker compose up -d`, which recreates the app container. Settings are
read at startup; a running instance never changes them.

## Secrets

Settings marked **Secret** in the reference (the database connection string, the SMTP
password, storage keys) must never be written into a file you commit or share:

- Put them in the `.env` file next to `docker-compose.yml` and refer to them as `${NAME}`, as
  the example above does for `SMTP_PASSWORD` and the reference compose file does for
  `POSTGRES_PASSWORD`. Make `.env` readable only by you (`chmod 600 .env`).
- Or use your orchestrator's secret store and pass the value as an environment variable.

QuickBuilder never writes secret values to its logs or shows them in the admin.

## When a setting is wrong

Every setting is checked when QuickBuilder starts. A value that is missing, out of range or
unusable stops startup before anything is served, and the log says which setting is wrong
and why, for example:

```text
Email:Smtp:Port must be between 1 and 65535; it is 0.
```

Find the setting in the [configuration reference](reference/configuration.md), fix it, and
start again. QuickBuilder never starts half-configured. See
[Troubleshooting](troubleshooting.md#configuration-error) if the message is not clear.

## Settings for the platform, not for QuickBuilder

Some environment variables are read by .NET and ASP.NET Core rather than by QuickBuilder
(`ASPNETCORE_*`, `DOTNET_*`, `Logging__*`, `Kestrel__*`). The image sets what it needs; you
should not need to change them, and they are not in the reference.
