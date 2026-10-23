# Research: Forms and Submissions

**Feature**: `018-forms-submissions` | **Date**: 2026-10-21 | **Spec**: [spec.md](./spec.md)

Decisions are numbered `FS-xx`. Each has a decision, a rationale and the alternatives
considered. The spec's five clarifications (2026-10-21) are inputs, not open questions:

- every role on the site reads, marks and exports; only Designers and Administrators delete
  (FR-025, FR-028);
- retention is a per-form setting, off by default (FR-023a);
- a form lives inside its block (FR-004);
- notifications include values by default, with a per-form "link only" switch (FR-032);
- trapped submissions go to a Spam list kept for 30 days (FR-016a).

---

## FS-01 A new `Forms` module, with a constitution amendment

**Decision**:
- Add a module, **Forms**: `QuickBuilder.Forms` and `QuickBuilder.Forms.Contracts`, with its
  own `forms` schema, `FormsDbContext` and migrations history table.
- It owns submissions, the form registry, the spam counters, notification deliveries, the
  rate-limit windows and the submission log. It also owns the public submit endpoint.
- It reads Pages through `Pages.Contracts` and asks Rendering, through `Rendering.Contracts`,
  for the page shown when a submission without scripts is refused (FS-09).
- The constitution's module list (Principle I) gains `Forms`. This is a **MINOR amendment**
  (2.4.0 → 2.5.0), made in this feature's PR as Governance requires. It names this plan as
  the reason and lists no affected earlier specs.

**Rationale**:
- Submissions are personal data from anonymous visitors. Their rules (who reads, retention,
  spam, email, rate limits) belong to none of the existing modules.
- Nothing in Forms has to change in the same transaction as Pages. Form definitions are read
  from published documents and learned through Pages' existing publishing events (FS-05), so
  the module boundary costs nothing in consistency.
- The public write path stays out of Rendering, which owns no tables and must never write
  (Principle VII, 012 PR-01).
- Adding a module is one line in the host (001 SC-009), plus architecture tests that already
  enumerate modules.

**Alternatives considered**:
- **Inside Cms**: closest in spirit (stored records, search), but it would put anonymous
  public writes, email and personal-data retention into the content module. Cms items are
  drafted and published; submissions are not, so little is shared. Rejected.
- **Inside Pages**: Pages owns the documents that hold forms, but nothing about submissions.
  024 put menus there because a menu publish had to refresh pages in one transaction; forms
  have no such need. Rejected.
- **Inside Rendering**: Rendering owns no tables and its public path must not write.
  Rejected.

---

## FS-02 The `form` node type holds its fields as a property, not as child blocks

**Decision**:
- One additive node type in page-schema v1: `form`, allowed wherever `contentChild` is
  allowed, with **no children**.
- Its fields are an ordered array prop, `fields`, of field objects
  ([form-block.md](./contracts/form-block.md)). Each field has a stable `id` (12 lowercase
  hex characters, made by the admin when the field is added, never changed).
- Messages, the button label, the redirect, notifications and retention are props of the
  same node.

**Rationale**: the spec lists fields, rules and messages, not layout. A single block with a
field list is the smallest thing that meets it (Principle XI). It also keeps the "one block
type, one definition" rule simple: one renderer, one canvas component, one `$defs` entry.
Stored values are keyed by field id (Principle VI, FR-006).

**Alternatives considered**:
- **A block per field type, as children of the form**: free layout (two fields side by
  side), but eleven more node types, placement rules (`formChild`) and renderers, and
  submissions would depend on the tree shape. Deferred: it can be added later as new node
  types without breaking v1 documents.
- **Field ids derived from labels**: a rename would break stored submissions. Rejected.

---

## FS-03 A form's identity is its node id

**Decision**: the form id is the `form` node's `id`. The admin already gives every node of a
copied or pasted subtree a new id (`withNewIds`, 011 FR-005), which gives FR-004's "a copy is
a new form" for free. A form inside a component placed on several pages is one form; each
submission records the page it came from.

**Rationale**: node ids are 128-bit random values made in the admin and never reused. No
feature copies documents on the server. Using them avoids a second identifier that the admin
would have to keep unique.

**Alternatives considered**:
- **A separate `formId` prop**: one more thing to regenerate on copy, with the same
  guarantee. Rejected.
- **A publish-time guard against the same form id on two subjects**: protects against a
  case no feature can create. Not built; the site import (#20) is given the obligation to
  keep or consistently remap node ids (module-surface.md §6).

---

## FS-04 Setup rules split between the schema and the publish guard

**Decision**:
- **Schema (every save)**: types, lengths and counts: at most 50 fields, label ≤ 200,
  options 1–100 of ≤ 200 characters, up to 10 recipient addresses of valid shape, subject ≤
  200, retention in `{30, 90, 180, 365}`, redirect URL scheme `https` or `http`. A draft
  breaking these is refused like any other invalid document (011).
- **Publish guard (Pages)**: completeness and consistency, which a draft may be missing
  while someone works:
  1. at least one field;
  2. labels unique ignoring case;
  3. choice fields have options, with unique values;
  4. minimum ≤ maximum for lengths, numbers, dates and selections;
  5. the reply-address field names an email field of the form, and only when values are
     included (FR-033);
  6. a redirect to a page, and a consent statement's link, name live pages (FR-039).
- The guard runs in `PublishingWrites` beside 024's unpublished-menu guard, for pages and
  components, by hand and by schedule. A refusal names the form and its first problem
  (machine reason `form_invalid` or `form_redirect_not_live`).
- The admin runs the same rules on every change and lists every problem in the property
  panel (FR-008). A shared fixture keeps the C# and TypeScript rules equal.

**Rationale**: matches 024 (a dangling `menuId` is saveable, unpublishable) and the spec
("saving shows every problem; publishing is refused").

**Alternatives considered**: refusing incomplete drafts on save. It would block saving a form
whose second field has just been added without a label. Rejected.

---

## FS-05 Forms learns which forms are live from Pages' publishing events

**Decision**:
- Forms subscribes to `PagePublished`, `PageUnpublished`, `ComponentPublished` and
  `ComponentUnpublished` (001 outbox, at-least-once).
- On a publish or rollback, the handler reads the subject's live document through
  `IPublishedContentReader`, extracts its `form` nodes, and in one Forms transaction:
  - upserts each form in `forms` (name, retention, notification settings, last published
    time);
  - replaces the subject's rows in `form_placements`.
- On an unpublish, it deletes the subject's placements. A form with no placement is "no
  longer published" (spec edge case) but keeps its last settings, which retention uses
  (FR-023a).
- The handler is idempotent: it reads the current live state, not the event's payload, so a
  repeated or late event writes the same rows.

**Rationale**:
- The submission list, filters, retention and notification settings need the forms
  without parsing every published document on each request.
- Retention must work after a form is unpublished, so Forms must keep the last settings it
  saw.
- Events already exist and carry everything needed (site, subject, kind). No change to Pages.

**Alternatives considered**:
- **A `live_forms` index in Pages, read through a new Contracts method**: Pages would own a
  table that only Forms reads, and Forms still needs its own copy for unpublished forms.
  Rejected.
- **Recording forms only when a submission arrives**: a retention change with no new
  submission would never be seen. Rejected.

**Lag**: events are dispatched within the outbox poll interval (default 1 s). The submit
endpoint does not depend on the registry (FS-07), so a submission right after a publish is
validated against the published form even before the event arrives.

---

## FS-06 A reserved public endpoint: `POST /_forms/{formId}`

**Decision**:
- A new public reserved prefix, `/_forms`, in `RoutePrefixes.PublicReserved`. The slug
  grammar cannot produce `_`, so no page address changes and no reserved-segment entry is
  needed (page-schema.md §5).
- The controller lives in Forms, is `[AllowAnonymous]` (added to 002's anonymous
  allow-list, §4) and runs after site resolution, so an unknown or disabled host never
  reaches it (009).
- The body is `application/x-www-form-urlencoded` only. It is limited to 256 KB
  (`[RequestSizeLimit]`, FR-015). Field values are `f.<fieldId>`; checkbox groups repeat
  the key. Platform fields start with `_`: `_page` (the page id), `_path` (the address the
  page was served at), `_hp` (the trap field, FS-12).
- The response depends on `Accept`: `application/json` (sent only by the form script)
  gets a JSON outcome; anything else gets the no-script responses of FS-09.
- Other methods on `/_forms/*` get `405`.

**Rationale**: a fixed, platform-owned address keeps POST off the page route, which answers
`405` for anything but GET and HEAD (012 public-rendering §1). A urlencoded body needs no
multipart parsing (file uploads are out of scope).

**Alternatives considered**:
- **POST to the page's own address**: mixes the cached GET path with writes, and the page
  route belongs to Rendering, which must not write. Rejected.
- **An `/api` endpoint**: `/api` belongs to the admin surface and may be confined to admin
  host names (009 FR-018a). Rejected.

---

## FS-07 The submit endpoint validates against the live document, not the registry

**Decision**:
1. Read the live page `_page` with `GetLivePageAsync`. Templates (016) are pages too, so
   item pages work.
2. Find the `form` node with the id in the page's document. If it is not there, find it in
   the live components the page references (`GetLiveComponentsAsync`).
3. If not found, the form is not published on that page: the outcome is `gone`, shown as
   the form's error message (FR-013), or, without a form to take it from, the default
   message.
4. `_path` is kept as the source address only if it is the page's own address or an item
   address under its template pattern; otherwise the page's own address is recorded.

**Rationale**: "accept only forms that are part of a live page of this site" (FR-015) is
checked against exactly what visitors are served, with the reader the renderer uses, at the
cost of one or two batched reads per submission. The registry may lag by an outbox tick
(FS-05); this path does not.

**Alternatives considered**: looking the form up in the registry. It is eventually consistent
and would accept submissions for a few hundred milliseconds after an unpublish. Rejected.

---

## FS-08 One set of value rules, in C# and TypeScript, held equal by a fixture

**Decision**:
- `FormValueRules` (Forms domain, pure) checks submitted values against a field: required,
  length, email shape, phone characters, number range and whole numbers, date range, choice
  membership, selection counts.
- Its TypeScript twin, `formRules.ts`, is used by the admin (preview). The published form
  script (FS-10) carries its own minified copy, written by hand like 024's `NavScript`.
- All three run the shared fixture `tests/fixtures/forms/value-rules.json` (input → error
  code): `FormValueRulesTests`, `formRules.spec.ts` and `formScript.spec.ts`, which loads the
  script's exact bytes from `FormScript.cs`.
- Results are **codes** (`required`, `too_long`, `too_short`, `email`, `phone`, `number`,
  `whole`, `too_small`, `too_large`, `date`, `too_early`, `too_late`, `choice`,
  `too_few`, `too_many`). Text comes from the field's own message or the default text for
  the site's language (FS-11).
- Normalisation before checks: trim text values (multi-line: trailing whitespace only);
  normalise line breaks to `\n`; keep letter case. No Unicode normalisation: the product runs
  with invariant globalization, where `string.Normalize` does nothing (the kernel's
  `SearchText` says the same), and browsers submit composed text. *(Changed during
  implementation.)*
- Email shape: one `@`, a non-empty local part, a domain with a dot, no spaces, at most
  254 characters. No DNS lookup (works offline, Principle II).

**Rationale**: the server is the authority (FR-015); the browser check is a convenience that
must agree with it (FR-010). A shared fixture is the pattern 024 used for menu rules.

**Alternatives considered**: browser-native constraint validation only. Messages and rules
would differ by browser, and checkbox counts and phone characters are not expressible.
Rejected as the only check; native attributes are still written (FS-10).

---

## FS-09 Without scripts: redirect after success, a re-rendered page after a refusal

**Decision**:
- **Success**: `303 See Other` to:
  - the redirect target (a live page's address, or the external URL), or
  - the source address with the fragment `#qb-sent-<formId>`.
- The rendered form carries a hidden success panel with that id. The block stylesheet shows
  it and hides the form when it is the target (`:target`), so the success message needs no
  script and the page stays the same cached bytes. Reloading reloads a GET, so nothing is
  sent twice (FR-012).
- **Refusals** (`invalid`, `limited`, `gone`, `error`): Forms asks Rendering for the source
  page rendered with the form's state, through a new `Rendering.Contracts` service,
  `IFormResponsePage`. The page is rendered **outside the cache** with the submitted values
  and per-field error codes for that one form, and returned with:
  - `422` (invalid), `429` with `Retry-After` (limited), or `503` (error and gone);
  - `Cache-Control: no-store`.
- The error summary is focused on load with `autofocus` on its heading container
  (`tabindex="-1"`), which works without scripts.

**Rationale**: post/redirect/get keeps the success path cacheable and safe to reload.
Refusals are rare and personal, so they are never cached (FR-014). Rendering keeps sole
ownership of page HTML; Forms only supplies the state.

**Alternatives considered**:
- **A success query parameter rendered by the server**: either pollutes the cache key or
  makes the page uncacheable. Rejected for `:target`.
- **A generic "thank you" page served by Forms**: loses the site's header, footer and design.
  Rejected.

---

## FS-10 With scripts: one small inline script

**Decision**:
- `FormScript`: one inline script, written once per page that has a form, admitted by its
  SHA-256 in the CSP like 024's `NavScript`. Its text is `Resources/form-script.js`, embedded
  and loaded by `FormScript.cs` (changed during implementation: a C# string constant made the
  escapes in its patterns fragile), so no
  build step links the admin and the server. At most 8 KB, hand-written without a minifier
  (FR-010 allows 15 KB).
- It:
  - sets `novalidate` and checks fields on submit (and on blur after a first attempt) with
    `formRules`, showing the same messages and summary as the server;
  - posts with `fetch` and `Accept: application/json`, disabling the button while in
    flight;
  - on `ok`, replaces the form with the success panel and moves focus to it, or follows
    `redirect` with `location.assign`;
  - on `invalid`, shows the server's codes;
  - on `limited` or `error`, shows the matching message.
- `RenderedPage.ScriptHash` becomes a list, so a page with a menu and a form admits both.
- Messages for every rule are written into the form as `data-` attributes in the page's
  language, so the script holds no text.
- Native attributes (`required`, `maxlength`, `minlength`, `type`, `min`, `max`, `step`)
  are still written, so a browser without scripts checks what it can before sending.

**Rationale**: the same progressive-enhancement model and CSP pattern as 024, with no
external file and no framework (Principle VIII).

**Alternatives considered**: an external file under `/_assets`. It needs a cache-busted URL,
another request and `script-src 'self'` on every page. Rejected, as in 024 NV-14.

---

## FS-11 CSP, texts and accessibility of the rendered form

**Decision**:
- **CSP** of a page with a form:
  - `form-action 'self'`, plus the origin of each external redirect target (Chrome applies
    `form-action` to the redirect after a submission);
  - `connect-src 'self'` for the script's `fetch`.
  Other pages keep `form-action 'none'` and no `connect-src`.
- **Default texts** (submit label, success, error, rate limit, every rule message, the
  required marker, the summary heading) are added to `PublicTexts` in each supported site
  language, as the not-found page and menu texts are.
- **Markup**: labels tied with `for`; help and error text tied with `aria-describedby`;
  `aria-invalid` on failing inputs; radio and checkbox groups in `fieldset`/`legend`; the
  required marker visible and stated in the label text for assistive technology; the
  summary as a list of links to the fields; the success panel with `role="status"`. The
  pattern follows the accessibility baseline of 011 and the widely used GOV.UK error
  summary.
- **Ids** are prefixed with the render scope, so a component that holds a form can appear
  twice on one page without duplicate ids.

---

## FS-12 The trap field

**Decision**: one text input named `_hp`, inside a wrapper that is:
- off-screen through a block-stylesheet class (not `display:none`, which some bots skip);
- `aria-hidden="true"`, with the input at `tabindex="-1"` and `autocomplete="off"`;
- labelled "Leave this field empty" in the site's language, in case it is ever exposed.

Outcome rules (FR-016, FR-016a):
- trap filled and the values pass the form's rules → stored as spam, not notified;
- trap filled and the values fail → dropped and counted;
- both answer exactly like a success.

**Rationale**: the standard honeypot. The Spam list catches the autofill false positives
the clarification worried about.

---

## FS-13 Rate limits in the database, per site and visitor address

**Decision**:
- A table `rate_windows (site_id, client_key, window_kind, window_start, count)`. Each
  submit request runs one atomic `INSERT … ON CONFLICT … DO UPDATE SET count = count + 1
  RETURNING count` for the current minute and hour windows (parameterised SQL through EF
  `SqlQuery`, with the site filter explicit and a test proving it, as the Technology
  Constraints require). This is the same technique as Identity's sign-in throttle
  (`SignInThrottleService`).
- **Client key**: SHA-256 of the client address, IPv6 aggregated to /64 as sign-in
  throttling does, behind the trusted-proxy rules of `Hosting:KnownProxies`.
- **What counts**: every request that reaches the endpoint for a resolved site, before any
  validation, so invalid floods are limited too.
- **Limits**: `Forms:RateLimit:PerMinute` (default 5, range 1–1000) and
  `Forms:RateLimit:PerHour` (default 30, range 1–10000), checked at startup (FR-017).
- **Retention**: windows older than one hour are deleted by the sweep (FS-17). No address,
  hashed or not, is written anywhere else (FR-021).

**Rationale**: works across instances with no shared cache (Principle IV) and keeps no data
longer than the window. The client form script's checks mean a person rarely sends an
invalid request, so counting every request does not limit people at a normal pace (SC-004).

**Alternatives considered**:
- **ASP.NET Core's rate limiter middleware**: in-memory, per instance, so limits multiply
  with instances. Rejected.
- **Counting only stored submissions**: invalid floods would cost validation and page reads
  with no limit. Rejected.

---

## FS-14 Duplicate suppression by content fingerprint

**Decision**:
- `fingerprint` = SHA-256 of the form id and the normalised values in field order. No
  client data is part of it (FR-021).
- Inside the insert transaction, `pg_advisory_xact_lock` on the fingerprint's first 8
  bytes, then: if a submission of the same form with the same fingerprint was received in
  the last 10 seconds, the request answers as that submission's success and stores nothing
  (FR-018).

**Rationale**: two different people sending byte-identical answers to the same form within
10 seconds is negligible for enquiry forms. The lock makes two instances agree.

---

## FS-15 Submission storage and search

**Decision**:
- `submissions` holds values as `jsonb`: an ordered array of `{ fieldId, label, type,
  value }`, where `value` is a string or an array of strings. Label and type are copied at
  submission time (FR-020).
- `search_text`: the values joined and normalised the way Media's `SearchText` normalises
  (lower case, NFKD without marks, collapsed spaces). Search is `LIKE` with escaped
  patterns, one term per word, all terms required (FR-027).
- Indexes:
  - `(site_id, is_spam, received_at DESC, id)` for the default list and unread counts;
  - `(site_id, form_id, received_at DESC)` for form filters and retention;
  - `(site_id, form_id, fingerprint, received_at)` for FS-14.
- Paging: keyset on `(received_at, id)`, 50 per page.

**Rationale**: a scan of `search_text` for 100,000 rows of a site is well under SC-006's
2 seconds on the reference machine, and the site and spam predicates narrow it first.
Trigram indexes would need `pg_trgm`, which the Media backlog already defers until a
measured need. `SubmissionListPerformanceTests` measures it.

---

## FS-16 CSV export: streamed, UTF-8 with BOM, formula-safe

**Decision**:
- `GET …/submissions/export` streams the result. It reads in one `REPEATABLE READ`,
  read-only transaction, so the file is a snapshot of when it started (edge case).
- Format: RFC 4180 (quotes doubled, fields with comma, quote or line break quoted), `\r\n`
  rows, a UTF-8 BOM so spreadsheet applications detect the encoding (FR-029).
- Columns: `Received (UTC)`, `Form`, `Page`, then one column per distinct field label in
  order of first appearance in the newest-first rows. Checkbox values joined with `; `.
- **Formula neutralisation**: a value whose first character is `=`, `+`, `-`, `@`, tab or
  carriage return is prefixed with `'` (OWASP CSV injection guidance).
- Exports cover non-spam submissions only (FR-016a). The header row is computed from a first
  pass over labels in the same transaction, then rows are streamed.

---

## FS-17 Background work: one notification worker, one sweep

**Decision**:
- **`NotificationWorker`** (Forms, `BackgroundService`, every 5 s):
  1. claims one due delivery at a time with `FOR UPDATE SKIP LOCKED`;
  2. marks it `sending` with a lease of 2 minutes and commits;
  3. sends through `IEmailSender`;
  4. records `sent`, or schedules the next attempt.
  - Retries follow 1, 5, 15, 30 and 60 minutes (6 attempts over 111 minutes, FR-034), then
    `failed` with the last SMTP reason (reply code and text, no content).
  - A delivery still `sending` after its lease (the instance died mid-send) becomes
    `failed` with reason `interrupted`, not retried. Sending is therefore never repeated
    (FR-034 "at most once").
  - With no SMTP configured, a delivery is created as `not_sent` and never claimed
    (FR-035).
- **`FormsSweepService`** (hourly, batches of 10,000, like Identity's retention service):
  - deletes rate windows older than one hour;
  - deletes spam received more than 30 days ago;
  - deletes non-spam submissions of each form with a retention period that were received
    longer ago than it;
  - writes one `submission_log` row per form and kind with the count.
  "At least once a day" (FR-023a) is met with margin.

**Rationale**: the outbox holds a transaction for each handler's whole run (001 D-02), which
is wrong for a network call that may take 30 seconds, and it is at-least-once. A lease plus a
"sending" state gives at-most-once without a broker.

**Alternatives considered**: sending in the request. It would delay or fail the visitor's
submission when the mail server is slow (FR-034). Rejected.

---

## FS-18 Email: an `IEmailSender` abstraction over SMTP with MailKit

**Decision**:
- **Abstraction**: `IEmailSender` in `Shared.Infrastructure/Email`, as Principle IV lists
  email among the pluggable providers. It has two implementations:
  - `SmtpEmailSender`, built on **MailKit** (MIT);
  - `DisabledEmailSender`, used when `Email:Smtp:Host` is empty.
- **Configuration** (all optional; checked at startup when `Host` is set):

  | Key | Default | Rule |
  |---|---|---|
  | `Email:Smtp:Host` | *(empty)* | empty disables email |
  | `Email:Smtp:Port` | `587` | 1–65535 |
  | `Email:Smtp:Security` | `StartTls` | `None`, `StartTls`, `SslOnConnect` |
  | `Email:Smtp:UserName` | *(empty)* | |
  | `Email:Smtp:Password` | *(empty)* | from a secret (environment or file), never the database |
  | `Email:Smtp:From` | *(required with Host)* | an email address |
  | `Email:Smtp:FromName` | `QuickBuilder` | ≤ 100 characters |
  | `Email:Smtp:Timeout` | `00:00:30` | 5 s – 5 min |

  `None` with a user name refuses to start unless the host is loopback, so a password is
  never sent in clear text by accident (Principle IX).
- **Messages**: plain text only. Headers are built through MimeKit's typed API; the subject
  is also stripped of CR and LF. Values are never placed in headers, except the reply
  address, which must pass the email rule (FS-08) and is parsed by `MailboxAddress`.

**Rationale**: MailKit is the library Microsoft's documentation recommends in place of
`System.Net.Mail.SmtpClient`. It is maintained, MIT licensed and dependency-light (MimeKit,
BouncyCastle for S/MIME, which is not used). Listed in `THIRD-PARTY-NOTICES.md`.

**Alternatives considered**:
- **`System.Net.Mail.SmtpClient`**: marked as not recommended for new development, with
  weak STARTTLS control. Rejected.
- **An HTTP mail API provider**: a third-party service, against "SMTP" in the constitution
  and offline operation. Rejected.

---

## FS-19 Notification content and the admin link

**Decision**:
- **Subject**: the form's subject (default "New submission: {form name}").
- **Body** (plain text, site language for fixed words):
  - form name, page address, time received (UTC and ISO);
  - with values on: each `label: value`, multi-line values indented;
  - a link to the submission in the admin;
  - a footer naming the site.
- **Link**: built from `Hosting:PublicUrl` + `Admin:PathPrefix` + the submission's route. It
  is never derived from the request (002 FR-011b). When `PublicUrl` is empty, the email says
  "Open Submissions in the admin" without a link, and startup logs a warning if SMTP is
  configured.
- **Reply address**: FR-033. Never with values off.

---

## FS-20 Capabilities and audit

**Decision**:

| Capability | Scope | Admin | Designer | Editor | Used by |
|---|---|---|---|---|---|
| `forms.submissions.view` | Site | ✓ | ✓ | ✓ | list, open, mark, export, spam list, restore, counters, unread count, retention impact |
| `forms.submissions.delete` | Site | ✓ | ✓ | — | delete submissions and spam |
| `forms.email.test` | Installation | ✓ | — | — | test email |

- `GET /api/v1/forms/email-status` is `[RequireSignedIn]`: it says only whether email is
  configured, for the editor's warning (FR-037).
- `submission_log` (append-only) records exports, deletions, restores and sweeps: actor
  (or `system`), action, form id, count, and for exports the filters used. The search
  words are recorded only as "search used", because they may be personal data (FR-030,
  FR-022).

---

## FS-21 Admin screens

**Decision**:
- **Form block**: `editor/blocks/form/` with `definition.ts` and `FormBlock.vue` (the canvas
  shows the form as published, disabled). New panel controls:
  - `FormFieldsControl` (list with add, edit, move up/down, remove, and keyboard
    equivalents);
  - `FormFieldDialog` (type-specific rules);
  - `FormNotifyControl` (recipients, subject, values on/off, reply field; email warning);
  - `FormRetentionControl` (with the impact count, FR-023a edge case);
  - `FormRedirectControl` (the shared `PagePicker` from 024, or a URL).
  The panel lists every setup problem (FS-04).
- **Submissions**: `features/forms/SubmissionsView.vue` (filters, search, list, selection,
  export, delete), `SubmissionDetail.vue`, `SpamView.vue`, an unread badge in the site
  navigation, refreshed on navigation and every 60 s while the admin is open.
- **Test email**: a "Send test email" action in installation settings (Administrators).

**Rationale**: reuses 024's picker and 011's panel patterns. Field reordering uses move
buttons rather than drag and drop: a field list is short, and buttons are the keyboard
equivalent 011 asks for anyway.

---

## FS-22 Tests

| Suite | Proves |
|---|---|
| `FormSchemaGoldenTests` + `formSchema.spec.ts` | the `form` node on both validators (Principle VI) |
| `FormPublishRulesTests` + `formSetup.spec.ts` | FS-04 rules, shared fixture |
| `FormValueRulesTests` + `formRules.spec.ts` | FS-08, shared fixture (SC-003) |
| `FormGoldenTests` | rendered HTML in every field type and state; encoding (SC-008) |
| `FormScriptTests` | size, hash, CSP admission |
| `SubmitEndpointTests` | FS-06, FS-07, FS-09, every outcome, scripts on and off |
| `SpamAndRateLimitTests` | FS-12 to FS-14 on two instances (SC-004) |
| `SubmissionEndpointsTests` | list, filters, search, mark, delete, roles (FR-025) |
| `SubmissionExportTests` | FS-16, formula cases, characters (SC-007) |
| `SmtpEmailSenderTests` | the SMTP provider on its own: security modes, sign-in, timeout, retryable vs permanent replies (Principle X) |
| `NotificationTests` | FS-17 to FS-19 against a Mailpit container: retries, interruption, link-only, reply address, header injection (SC-005) |
| `FormRegistryTests` | FS-05 events, idempotency, unpublish |
| `FormsSweepTests` | retention, spam purge, window cleanup |
| `FormsIsolationProbe` | site isolation (FR-024) |
| `UpgradeFrom024Tests` | nothing changes for existing pages; CSP unchanged without forms |
| `SubmissionListPerformanceTests` | SC-006 with 100,000 submissions |
| `FormRenderPerformanceTests` | SC-009 |
| admin specs with axe | SC-008 for the editor and Submissions |

The release smoke test gains: add a form to the contact page, publish, submit, see it in
Submissions.

---

## FS-23 Dependencies

- **New**: `MailKit` (MIT), server only. Justified in FS-18 and added to
  `Directory.Packages.props` and `THIRD-PARTY-NOTICES.md`.
- **Test only**: the `axllent/mailpit` container image (MIT) through the existing
  Testcontainers setup.
- **Admin**: none.
