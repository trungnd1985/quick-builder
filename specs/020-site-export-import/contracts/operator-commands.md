# Contract: `sites` Operator Command

**Feature**: 020 · **Requirements**: FR-032 … FR-034 · **Research**: ST-18

Extends [002 operator-commands.md](../../002-identity-setup/contracts/operator-commands.md).
The command runs inside the container and is not reachable over the network:

```bash
docker compose exec -T app dotnet QuickBuilder.Host.dll sites <subcommand> [options]
```

Paths are paths inside the container. Mount a volume for backups, for example
`./backups:/backups`.

## Subcommands

```text
sites export --site <id|host> --out <file>
sites export --all --out <dir>
sites check <file> [--into <id|host> | --restore <id|host>]
sites import <file> --as-new --name <name> [--host-name <host>] [--start-fresh] [--accept-warnings]
sites import <file> --into <id|host> --on-conflict replace|skip|rename [--accept-warnings]
sites import <file> --restore <id|host> --confirm <site name> [--pre-restore-out <file>] [--accept-warnings]
```

- `--site`, `--into` and `--restore` accept a site id (any uuid form) or one of the site's host
  names, disabled sites included.
- `--host-name` gives a new site its host name when every host name of the archive is used by
  another site here (FR-020); without it such an import stops with `64`.
- `export --all` writes `<dir>/<primary-host-or-id>-<yyyyMMddTHHmmssZ>.qbsite` per site, in
  site order. It continues after a failed site.
- `check` prints the check result (data-model §3) and changes nothing. With `--into` or
  `--restore`, it also prints conflicts or removals against that site.
- `import` runs the check first and stops with exit code `6` when it has warnings and
  `--accept-warnings` is missing.
- `--restore` requires `--confirm` equal to the site's current name (FR-027b). The
  pre-restore export is always kept in storage (7 days), and is also written to
  `--pre-restore-out` when given.
- `--out` refuses to overwrite an existing file.

## Output

Progress lines on standard output every 5 seconds (`[pages] 41%`), then the summary or
report as plain text. With `--json`, a single JSON object (the transfer, as in
`transfer-api.yaml`) is printed as the last line instead; log lines may come before it.
Neither output nor logs contain titles or content, only ids, counts and codes (Principle IX).

## Exit codes

| Code | Meaning |
|---|---|
| 0 | success (for `export --all`: every site) |
| 1 | the transfer failed or was interrupted; for `export --all`: at least one site failed |
| 2 | the archive was refused at check |
| 3 | the database has pending migrations (existing convention) |
| 4 | unknown site |
| 5 | the site already has an active transfer (`transfer.busy`) |
| 6 | confirmation required: warnings without `--accept-warnings`, or `--confirm` missing or different |
| 64 | usage: unknown subcommand, missing or conflicting options |

## Audit

Every run is a transfer job with `trigger = cli` and `started_by = null`. It is recorded as a
security event with `operator: true` (FR-031).
