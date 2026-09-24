# Contract: Operator Commands

**Feature**: `002-identity-setup` | **Audience**: operators, and anyone extending the command mechanism

These commands run inside the application image with command-line access to the
installation. None of them is reachable over the network (FR-028). Each one uses the
same configuration as the running application, connects to the same database, runs,
and exits.

Reference deployment form:

```bash
docker compose exec app dotnet QuickBuilder.Host.dll <command> [arguments]
```

`migrate` (from 001) keeps its existing behaviour and becomes the first command
registered through the mechanism below.

---

## `identity setup-code`

Prints the current setup code while the installation is in setup mode.

| Situation | Output (stdout) | Exit code |
|---|---|---|
| Installation unclaimed | `Setup code: ABCD-EFGH-JKMN` | 0 |
| Installation already claimed | `Setup is complete; there is no setup code.` | 2 |
| Migrations not applied | `The database has pending migrations. Run the migrate command first.` | 3 |

Every run records `operator_recovery_used` with `{ "command": "setup-code" }`.

## `identity recovery-link --email <address>`

Issues a set-password link for an **existing, active administrator**. The link is
single-use and expires after `Identity:SetPasswordLinks:Lifetime`, which is 24 hours by
default.

| Situation | Output | Exit code |
|---|---|---|
| Success, `Hosting:PublicUrl` configured | `Set-password link (expires 2026-09-24T10:00:00Z):` followed by `https://cms.example.com/admin/set-password#token=…` | 0 |
| Success, no public URL configured | The path `/admin/set-password#token=…` and the instruction `Prepend the address you use to reach the admin, e.g. http://127.0.0.1:8080` | 0 |
| No active administrator with that address | `No active administrator has that email address.` | 4 |
| Installation not set up | `Setup is not complete. Use 'identity setup-code' instead.` | 2 |
| Migrations not applied | as above | 3 |

**Side effects**, in one transaction:
- Any earlier live link for the account is invalidated (`replaced`).
- The account's existing sessions stay valid until the new link is used, because using
  it revokes them (FR-011a).
- `set_password_link_issued` (`issuedBy: operator`) and `operator_recovery_used` are
  recorded.

The token appears **only** on standard output, never in the log stream. The command
writes its result with `Console.Out` directly, not through Serilog.

---

## Extension mechanism (for module authors)

`QuickBuilder.Shared.Infrastructure` defines:

```csharp
public interface IHostCommand
{
    string Name { get; }              // first argument, e.g. "identity"
    Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, CancellationToken ct);
}
```

- A module registers implementations from `Add<Module>Module()`.
- The host matches the first command-line argument against the registered names and
  runs that command instead of starting the web server. An unknown name exits with
  code 64 and prints the list of known commands.
- The host itself contains no command logic (001 FR-001).
