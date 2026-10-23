# Contract: module surface of Forms

## 1. Modules and references

```text
QuickBuilder.Forms ──► Forms.Contracts
                   ──► Pages.Contracts      (IPublishedContentReader, publishing events, page links)
                   ──► Rendering.Contracts  (IFormResponsePage)
                   ──► Sites.Contracts      (ICurrentSite, SiteDto, site language and slug)
                   ──► Identity.Contracts   (IUserDirectory: the Administrator's own address)
                   ──► Shared.Infrastructure (IEmailSender, outbox consumer, site filter)
```

- **New reference edges**: Forms → each Contracts project above. No module references
  `Forms.Contracts` yet: Pages runs the form publish rules itself (it owns the documents), and
  Rendering renders the `form` node from the page document.
- `ModuleBoundaryTests` gains Forms; the module list in the constitution (Principle I) gains
  `Forms` (2.5.0, FS-01).
- No transaction spans modules: the registry is updated by Forms' own event handler.

## 2. New Contracts members

### Rendering.Contracts

```csharp
public interface IFormResponsePage
{
    /// The page at `path` on `site`, rendered outside the cache with `state` applied to the
    /// one form it names; null when the address no longer resolves to live content.
    Task<PublicResponse?> RenderAsync(SiteDto site, string path, string basePath,
        FormState state, int statusCode, CancellationToken cancellationToken);
}

public sealed record FormState(
    string FormId,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Values,   // field id → values
    IReadOnlyDictionary<string, string> Errors,                  // field id → value code
    FormBanner Banner);                                          // None, Error, Limited

public enum FormBanner { None, Error, Limited }
```

### Shared.Infrastructure

```csharp
public interface IEmailSender
{
    bool IsConfigured { get; }
    Task<EmailResult> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed record EmailMessage(
    IReadOnlyList<string> To, string Subject, string TextBody, string? ReplyTo);

public sealed record EmailResult(bool Accepted, bool Retryable, string? ServerReply);
```

`Retryable` is false for permanent SMTP replies (5xx) and true for 4xx and connection
failures.

### Forms.Contracts

Capability names (`FormsCapabilities`) and the `/_forms` route prefix
(`FormsRoutes.SubmitPrefix`), referenced by the host's `RoutePrefixes`.

## 3. Events

- **Consumed**: `Pages.PagePublished`, `Pages.PageUnpublished`, `Pages.ComponentPublished`,
  `Pages.ComponentUnpublished` → `RefreshFormRegistry` handler (FS-05), idempotent,
  recorded in the Forms processed-events table.
- **Published**: none. Nothing consumes submission events yet (webhooks are out of scope);
  `integration-events.md` lists the new consumer only.

## 4. Capabilities and anonymous endpoints

- Registered by Forms (002 authorization.md §2, "Registered by 018"):
  `forms.submissions.view` (A, D, E), `forms.submissions.delete` (A, D),
  `forms.email.test` (A, installation).
- Anonymous allow-list (§4) gains:
  `POST /_forms/{formId}` and any other method on it (`405`): public surface, form
  submissions from site visitors; justified by 018 FR-015.

## 5. Configuration keys

| Key | Default | Range / rule |
|---|---|---|
| `Email:Smtp:Host` | *(empty: email off)* | host name or address |
| `Email:Smtp:Port` | `587` | 1–65535 |
| `Email:Smtp:Security` | `StartTls` | `None`, `StartTls`, `SslOnConnect` |
| `Email:Smtp:UserName` | *(empty)* | |
| `Email:Smtp:Password` | *(empty)* | secret; with `None`, allowed only for a loopback host |
| `Email:Smtp:From` | — | required when `Host` is set; an email address |
| `Email:Smtp:FromName` | `QuickBuilder` | ≤ 100 |
| `Email:Smtp:Timeout` | `00:00:30` | 5 s – 5 min |
| `Forms:RateLimit:PerMinute` | `5` | 1–1000 |
| `Forms:RateLimit:PerHour` | `30` | 1–10000, and at least `PerMinute` |

All are documented in `docs/configuration.md` (new "Email" and "Forms" sections) and validated
at startup (Principle II).

## 6. Obligations on other features

- **Site export and import (#20)**: forms travel inside page and component documents; node
  ids (form ids and field ids) must be kept, or remapped consistently with any references
  (`redirect.pageId`, `consent.linkPageId` are page ids and map as other page links do).
  Submissions, the registry, counters and logs are **not** exported (FR-040).
- **Site deletion**: sites cannot be deleted yet (Sites publishes no `SiteDeleted` event).
  The future site-deletion feature must delete the site's Forms rows; the obligation is
  recorded in `docs/backlog.md`.
- **Draft previews (#22)**: a preview must render forms disabled (no submit), as the canvas
  does.
