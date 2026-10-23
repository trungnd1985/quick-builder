# Implementation Plan: Forms and Submissions

**Branch**: `018-forms-submissions` | **Date**: 2026-10-21 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/018-forms-submissions/spec.md`

**Issue**: #18.
- **Open exceptions from earlier plans**: E-5 (unchanged).
- **New exceptions**: none.
- **Constitution amendment in this PR**: 2.4.0 → 2.5.0, adding `Forms` to Principle I's
  module list (FS-01). Approved by the maintainer on 2026-10-22 (analysis finding C1); it
  lands first, as T002.

## Summary

Designers (and Editors, who may edit drafts) add a **Form** block to a page or component and
give it fields with rules, messages, an optional redirect, email notifications and an
optional retention period. Visitors submit it with or without scripts. The server checks
every value against the published form, holds spam back with a trap field, a per-address rate
limit and duplicate suppression, and stores accepted submissions per site. Members read,
search, filter and export them; Designers and Administrators delete them. Recipients get an
email through the installation's SMTP server, sent in the background with retries.

The approach, detailed in [research.md](./research.md):
- **A new module, Forms** (FS-01), with a MINOR constitution amendment. It owns submissions,
  the form registry, deliveries, spam counters, rate windows and the submission log, and the
  public submit endpoint. Pages and Rendering are only read or asked.
- **The `form` node** (FS-02 to FS-04): one additive page-schema node with its fields as a
  prop array. The form's id is its node id. Structural rules apply on save; completeness and
  a live redirect target are checked by a new Pages publish guard.
- **Registry from events** (FS-05): Forms keeps the last published settings of every form,
  updated from Pages' existing publishing events, so retention and filters work after a form
  is unpublished.
- **Submitting** (FS-06 to FS-09, FS-12 to FS-14):
  - `POST /_forms/{formId}`, a new reserved public prefix, urlencoded, at most 256 KB;
  - the form is looked up in the live page (and its components) on every submission;
  - one set of value rules in C# and TypeScript, held equal by a fixture;
  - a database-backed rate limit per site and hashed address;
  - a trap field whose valid catches go to Spam;
  - a 10-second content fingerprint against double submits.
- **Published output** (FS-09 to FS-11):
  - server-rendered, accessible markup;
  - success without scripts by redirect and `:target`, so pages stay cached;
  - refusals re-rendered by Rendering outside the cache through a new `IFormResponsePage`;
  - one inline script (≤ 8 KB) admitted by hash;
  - CSP `form-action` and `connect-src` only on pages with a form.
- **Email** (FS-17 to FS-19): an `IEmailSender` abstraction in Shared.Infrastructure over
  MailKit, with a leased, at-most-once worker, retries for an hour and more, and plain-text
  messages with an admin link built from `Hosting:PublicUrl`.
- **Admin** (FS-20, FS-21): the Form block with field, notification, redirect and retention
  controls; Submissions with inbox, spam, filters, search, detail, delete, CSV export and an
  unread badge; a test email for Administrators.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript (strict) for the admin. Unchanged.

**Primary Dependencies**:
- **Server**: ASP.NET Core MVC, EF Core 10 + Npgsql, the in-house outbox, dispatcher and
  `SchemaSubsetValidator`, `System.Text.Json`. **New: MailKit (MIT)** for SMTP (FS-18, FS-23).
- **Admin**: Vue 3, Pinia, vue-router, vue-i18n. Nothing new.
- **Tests**: the `axllent/mailpit` image through Testcontainers.

**Storage**: PostgreSQL 16+.
- New schema `forms`, migration `Initial`: **+** `forms`, `form_placements`, `submissions`,
  `notification_deliveries`, `spam_counters`, `rate_windows`, `submission_log`, and the
  module's processed-events and migrations-history tables.
- Pages, Rendering and other schemas: no table change. Page documents gain the optional
  `form` node (page-schema v1, additive).

**Testing**: xUnit, `WebApplicationFactory`, Testcontainers PostgreSQL and Mailpit; vitest with
Testing Library and axe. Suites in FS-22, mainly:
- `FormSchemaGoldenTests`, `FormPublishRulesTests` and `FormValueRulesTests`, with their
  TypeScript twins on shared fixtures;
- `FormGoldenTests` and `FormScriptTests`;
- `SubmitEndpointTests`, `SpamAndRateLimitTests` (two instances) and
  `SubmissionEndpointsTests`;
- `SubmissionExportTests`, `SmtpEmailSenderTests` (the provider suite), `NotificationTests`, `FormRegistryTests` and `FormsSweepTests`;
- `FormsIsolationProbe` and `UpgradeFrom024Tests`;
- `SubmissionListPerformanceTests` (SC-006) and `FormRenderPerformanceTests` (SC-009).

**Target Platform**: Linux containers (Alpine, amd64 and arm64). Unchanged.

**Project Type**: Web application: the modular-monolith backend, the Vue admin SPA and the
server-rendered public site.

**Performance Goals** (reference compose setup, 2 vCPU and 4 GB):
- **Submit**: p95 under 300 ms from request to response with notifications queued
  (SC-002 allows 2 s end to end).
- **List and search**: under 2 s on 100,000 submissions; export of 10,000 in under 30 s
  (SC-006).
- **Render**: pages with forms stay within 012's budgets (SC-009); the form adds no read to a
  page render.
- **Email**: 95 % handed to a reachable server within 1 minute (SC-005); the worker polls every
  5 s.
- **Published JS**: at most 8 KB inline, only on pages with a form (FR-010 allows 15 KB).

**Constraints**:
- **Personal data**: no submitted value, recipient, search word or client address in logs,
  audit rows or events; hashed addresses only in rate windows, for at most an hour.
- **Published only**: submissions are accepted only for forms in live documents (FS-07);
  Rendering keeps read-only access.
- **Offline**: no outbound request except to the operator's SMTP server, and none at all
  when email is not configured.
- **No script required**: every outcome works without scripts.

**Scale/Scope**:
- **Code**: 2 new projects (Forms module). 1 new shared abstraction (email).
- **Migrations**: 1 (Forms `Initial`). Tables: 7 new, 0 changed.
- **Documents**: page schema gains 1 node type and rules 26–28; no new contract file.
- **Endpoints**: 1 public (submit) and 12 admin operations (forms-api.yaml); publish, rollback
  and scheduled runs gain 2 refusals.
- **Contracts members**: `IFormResponsePage` (Rendering), `IEmailSender` (Shared).
- **Events**: 0 new; 1 new handler for 4 existing events.
- **Capabilities**: 3 new. **Configuration keys**: 10 new.
- **Admin**: about 12 components and 5 TypeScript modules.

## Constitution Check

*GATE: passed before Phase 0 and re-checked after Phase 1 design (result at the end of
this section).*

| # | Principle | Compliance |
|---|---|---|
| I | Modular Monolith with Vertical Slices | **Complies, with an amendment.** <br>• **New module** `Forms` (two projects, `forms` schema, own `DbContext` and history table). The module list is amended in this PR (FS-01): a MINOR change, with the reason and "no affected specs" in the Sync Impact Report. <br>• **Slices**: `Features/Submit/`, `ListSubmissions/`, `GetSubmission/`, `MarkSubmission/`, `DeleteSubmissions/`, `RestoreSubmission/`, `ExportSubmissions/`, `UnreadCount/`, `ListForms/`, `RetentionImpact/`, `SpamCounters/`, `EmailStatus/`, `TestEmail/`, `RefreshFormRegistry/` (event handler). Thin controllers, in-house handlers. <br>• **Domain**: `Submission` (spam, read, restore rules), pure `FormValueRules`, `FormDefinition` (parsed from a node), `SubmitPolicy` (the outcome order of data-model §4); unit-tested. <br>• **Cross-module**: Forms → Pages/Rendering/Sites/Identity Contracts only (module-surface §1). Registry by events; no shared transaction. <br>• **Enforcement**: `ModuleBoundaryTests` and `AuthorizationDeclarationTests` extended. |
| II | Easy to Install, Operate and Upgrade | **Complies.** Every new key is optional with a safe default, validated at startup (an invalid SMTP block stops startup and names the key). With no SMTP, the product works and says so. One automatic, forward-only migration. Health checks unchanged; the mail server is not a readiness dependency. |
| III | Upgrades Never Lose Data | **Complies.** `UpgradeFrom024Tests`: additive schema, documents untouched. Export obligations for #20 recorded (module-surface §6); submissions are deliberately not in site exports (FR-040). |
| IV | Pluggable Infrastructure, Minimal Required Dependencies | **Complies.** Email goes through `IEmailSender` with SMTP as the provider, selected by configuration; disabled by default. Rate limits and leases live in PostgreSQL, so several instances agree without Redis. |
| V | Multi-Site Within One Installation | **Complies.** <br>• All seven tables carry `site_id` with the global filter. <br>• **Filter bypasses**: the notification worker and the sweep run across sites, each with a code comment and a dedicated test (as `ThumbnailWorker`). <br>• **Raw SQL**: the rate-window upsert only, parameterised, with the site explicit and a test proving it. <br>• Submissions are accepted only on the form's own site's host names (site resolution + the live-page check). <br>• **Isolation suite**: + `FormsIsolationProbe`. |
| VI | Content Schemas Are Versioned Contracts | **Complies.** <br>• `form` is an additive node in page-schema v1 with golden examples run by both validators. <br>• Field ids are stable and key stored values. <br>• One `$defs` entry, one canvas component, one renderer; no other block changes. <br>• Value and setup rules shared through fixtures. |
| VII | Draft and Published Are Separate Worlds | **Complies.** Form changes reach visitors only through page or component publish; the endpoint validates against live documents only (FS-07). Rendering stays read-only; submissions are written by Forms, not by the rendering path. |
| VIII | Rendering Is Server-Side, Cached and Correctly Invalidated | **Complies.** <br>• The form is rendered on the server inside the cached page; no new dependency edge is needed, because the form is part of the document. <br>• Visitor-specific output (refusals) is never cached (FS-09); success uses a cached page plus a fragment. <br>• Script ≤ 8 KB, inline by hash, enhancement only. <br>• `FormRenderPerformanceTests`. |
| IX | Secure by Default | **Complies.** <br>• **Encoding**: every label, message and value through `HtmlWriter`; emails plain text; CSV formula neutralisation. <br>• **Input bounds**: 256 KB body, field counts and lengths, urlencoded only. <br>• **Abuse**: rate limit, trap, duplicate suppression. <br>• **CSP**: `form-action` and `connect-src` widened only on pages with a form; the script by hash. <br>• **Email**: header injection impossible by construction; no clear-text password to a non-loopback server; password from secrets only. <br>• **Authorization**: capabilities on every admin operation; the public endpoint on the anonymous allow-list with its justification. <br>• **Privacy**: no addresses stored with submissions; no values, recipients or search words in logs or audit; retention and spam purge. |
| X | Test-First for Behaviour, Tests Always for Contracts | **Complies.** Integration tests for every endpoint (Testcontainers PostgreSQL and Mailpit); golden files for the `form` node and its HTML; unit tests for rules, outcome order and state transitions; 80 % line coverage on submit, rules, worker and sweep. The release smoke test gains a form submission. |
| XI | Simplicity and Incremental Delivery | **Complies.** <br>• **One dependency** (MailKit), justified (FS-18). <br>• **Not built**: field child blocks, conditional logic, file uploads, a form library, per-site SMTP, CAPTCHA, webhooks, trigram search, an external script file. <br>• **Reuse**: publishing events, readers, `PagePicker`, `PublicTexts`, the throttle upsert technique, the retention-service pattern. <br>• **Delivery**: one PR. If it must split: (1) the module, the block, submit, Submissions and retention (US1–US3); (2) notifications and email (US4); (3) the trap, Spam list, rate limit and duplicate suppression (US5). Each is usable alone, but forms should not face the public internet before (3). |

**Technology Constraints**:
- MVC controllers only; EF Core only, with raw SQL limited to FS-13 through `SqlQuery`.
- No Node process at run time; the form script is a constant in `FormScript.cs`, tested from
  vitest against the shared rule fixture.
- No new admin libraries.
- Editor canvas: in-house Vue components; field reordering by buttons with keyboard
  equivalents.

**Development Workflow**:
- Issue #18 → `specs/018-forms-submissions` → branch `018-forms-submissions` → one PR that
  closes #18.
- Clarify has run (5 questions, all answered). Analyze follows tasks.
- **Contract and registry changes in the same PR**:
  - `.specify/memory/constitution.md` (2.5.0, Forms in Principle I);
  - `docs/contracts/page-schema.md` (§2, §3 rules 26–28, §4, §6, §9) and
    `page-schema/v1.schema.json`, with golden examples (from
    [contracts/form-block.md](./contracts/form-block.md));
  - `specs/001-foundation-skeleton/contracts/integration-events.md` (new consumer);
  - `specs/002-identity-setup/contracts/authorization.md` §2 and §4 (capabilities, the
    anonymous submit endpoint);
  - `specs/012-publishing-rendering/contracts/public-rendering.md` (CSP for forms,
    `/_forms`, script hashes as a list) and `publishing-api.yaml` (the two refusals);
  - `docs/configuration.md` (Email, Forms), `docs/operations.md` (email set-up, test email),
    `docs/backlog.md` (deferred items, #20 and site-deletion obligations),
    `docs/contributing.md` (module list), `THIRD-PARTY-NOTICES.md` (MailKit, MimeKit).

**Operator impact**:
- **New configuration keys**: `Email:Smtp:*` (8) and `Forms:RateLimit:*` (2), all optional
  (module-surface §5).
- **Migrations**: 1, additive and automatic: Forms `Initial`.
- **New dependencies**: MailKit (and MimeKit, BouncyCastle as its dependencies).
- **Upgrade notes**:
  1. Sites can collect enquiries with the new Form block; submissions are under
     **Submissions**.
  2. To send notification emails, set `Email:Smtp:Host`, `Email:Smtp:From` and, if needed,
     user name, password and security, and set `Hosting:PublicUrl` so emails can link to the
     admin. Without them, forms work and notifications are off.
  3. Pages with a form allow `form-action 'self'` and `connect-src 'self'` in their CSP and
     carry a small inline script. A reverse proxy that rewrites the CSP must keep these, or
     the form falls back to working without scripts.
  4. Name your proxies in `Hosting:KnownProxies`, or every visitor shares one rate limit
     (the proxy's address).

**Post-Phase-1 re-check**:
- forms-api.yaml declares a capability on every operation; the submit endpoint is on the
  anonymous allow-list;
- module-surface.md adds only edges to Contracts projects;
- the seven tables have `site_id` and the filter; the two cross-site jobs are commented and
  tested;
- no submitted value, recipient, search word or address reaches logs, audit rows or events;
- the page-schema change is additive under §6, with examples for every rule;
- raw SQL only in FS-13;
- the CSP changes only on pages with a form;
- Rendering gains no write access and no table.

**Gate: PASS**, conditional on the constitution amendment (2.5.0) landing in this PR. No
exception is recorded.

## Project Structure

### Documentation (this feature)

```text
specs/018-forms-submissions/
├── plan.md                 # This file
├── spec.md                 # Feature specification (clarified: 5 questions)
├── research.md             # Phase 0: decisions FS-01 … FS-23
├── data-model.md           # Phase 1: tables, form definition, states, outcomes
├── quickstart.md           # Phase 1: validation guide per story
├── contracts/
│   ├── form-block.md       # Draft of the page-schema addition
│   ├── public-forms.md     # Submit endpoint, HTML, JSON, codes, CSP
│   ├── forms-api.yaml      # Admin API
│   └── module-surface.md   # References, Contracts members, events, capabilities, config
├── checklists/
│   └── requirements.md
└── tasks.md                # Created by /speckit-tasks, not by this command
```

### Source Code (repository root)

New items are marked `+`, changed items `~`.

```text
.specify/memory/constitution.md                  # ~ 2.5.0: Forms module
docs/
~ contracts/page-schema.md  contracts/page-schema/v1.schema.json  contracts/page-schema/examples/
~ configuration.md  operations.md  backlog.md  contributing.md
~ THIRD-PARTY-NOTICES.md  Directory.Packages.props  QuickBuilder.slnx

src/QuickBuilder.Host/
~ Program.cs                                     # AddFormsModule, application part, Email options
~ Routing/RoutePrefixes.cs                       # /_forms public reserved

src/Shared/QuickBuilder.Shared.Infrastructure/
+ Email/IEmailSender.cs  EmailMessage.cs  SmtpOptions.cs  SmtpOptionsValidator.cs
+ Email/SmtpEmailSender.cs  DisabledEmailSender.cs  EmailRegistration.cs

src/Modules/Forms/
├── QuickBuilder.Forms.Contracts/
│   + FormsCapabilities.cs  FormsRoutes.cs
└── QuickBuilder.Forms/
    + FormsModule.cs  FormsOptions.cs  FormsErrors.cs
    + Domain/Submission.cs  SubmissionValue.cs  NotificationDelivery.cs  FormRecord.cs  FormPlacement.cs
    + Domain/FormDefinition.cs  FormValueRules.cs  ValueCodes.cs  SubmitPolicy.cs  Fingerprint.cs  SearchText.cs
    + Infrastructure/FormsDbContext.cs  FormsDbContextFactory.cs  RateWindows.cs  LiveFormLocator.cs
    + Infrastructure/NotificationWorker.cs  NotificationComposer.cs  FormsSweepService.cs  SubmissionLog.cs
    + Infrastructure/CsvWriter.cs  Migrations/<ts>_Initial.cs
    + Features/Submit/SubmitController.cs  Submit.cs  SubmitResponses.cs
    + Features/ListSubmissions/  GetSubmission/  MarkSubmission/  DeleteSubmissions/  RestoreSubmission/
    + Features/ExportSubmissions/  UnreadCount/  ListForms/  RetentionImpact/  SpamCounters/
    + Features/EmailStatus/  TestEmail/  RefreshFormRegistry/RefreshFormRegistryHandler.cs

src/Modules/Pages/QuickBuilder.Pages/
~ Infrastructure/Schema/PageDocumentValidator.cs   # rules 26, 27
+ Domain/Forms/FormPublishRules.cs                  # rule 28 (FS-04)
~ Infrastructure/PublishingWrites.cs                # form guards on publish, rollback, schedules
~ Domain/DependencyExtractor.cs                     # consent link as a page dependency
~ PagesErrors.cs  PagesErrorResults.cs
~ QuickBuilder.Pages.Contracts/PageSchema.cs        # embeds the updated schema

src/Modules/Rendering/
├── QuickBuilder.Rendering.Contracts/
│   + IFormResponsePage.cs  FormState.cs
└── QuickBuilder.Rendering/
    + Rendering/Blocks/FormRenderer.cs
    + Rendering/Forms/FormScript.cs  FormMarkup.cs
    + Features/FormResponse/FormResponsePage.cs
    ~ Domain/RenderContext.cs  RenderInputs.cs  RenderedPage.cs   # form state; script hashes; form-action origins
    ~ Domain/RenderReferences.cs                     # consent link pages
    ~ Features/RenderPage/PageResponder.cs           # CSP: form-action, connect-src, script list
    ~ Rendering/BlockStylesheet.cs                   # .qb-form styles, :target, trap
    ~ Features/NotFound/PublicTexts.cs               # form texts, per language
    ~ RenderingModule.cs

src/admin/src/
+ editor/blocks/form/definition.ts  FormBlock.vue
+ editor/panel/controls/FormFieldsControl.vue  FormFieldDialog.vue  FormNotifyControl.vue
+ editor/panel/controls/FormRedirectControl.vue  FormRetentionControl.vue
+ editor/forms/formRules.ts  formSetup.ts                     # admin twins of FormValueRules and FormPublishRules
~ editor/blocks/registry.ts  editor/blocks/blocks.css  editor/schema/schema.ts
+ features/forms/SubmissionsView.vue  SubmissionDetail.vue  SpamView.vue  SubmissionFilters.vue
+ features/forms/useUnreadCount.ts
~ features/settings/  (test email)  app/router.ts  app/ (site navigation badge)  i18n/en.json
+ api/forms.ts
src/admin/tests/
+ editor/formSchema.spec.ts  formSetup.spec.ts  formRules.spec.ts  FormBlock.spec.ts
+ features/forms/*.spec.ts

tests/
+ fixtures/forms/value-rules.json  setup-rules.json  seed-old-submissions.sql
~ fixtures/rendering/golden/                         # form HTML
~ QuickBuilder.ArchitectureTests/                    # Forms in module and authorization tests
+ QuickBuilder.UnitTests/Forms/                      # FormValueRulesTests, SubmitPolicyTests, FingerprintTests, CsvWriterTests, NotificationComposerTests
+ QuickBuilder.UnitTests/Pages/Forms/                # FormPublishRulesTests, FormSchemaGoldenTests
+ QuickBuilder.UnitTests/Rendering/Forms/            # FormGoldenTests, FormScriptTests
~ QuickBuilder.IntegrationTests/
│   + Forms/SubmitEndpointTests.cs  SpamAndRateLimitTests.cs  SubmissionEndpointsTests.cs
│   + Forms/SubmissionExportTests.cs  NotificationTests.cs  FormRegistryTests.cs  FormsSweepTests.cs
│   + Publishing/FormPublishGuardTests.cs  Rendering/UpgradeFrom024Tests.cs
│   ~ Isolation/Probes/ (+ FormsIsolationProbe)  Infrastructure/EndpointCatalog.cs  (+ Mailpit fixture)
~ QuickBuilder.SmokeTests/                           # + SubmissionListPerformanceTests, FormRenderPerformanceTests; journey: submit a form
specs/001-foundation-skeleton/contracts/integration-events.md   # ~
specs/002-identity-setup/contracts/authorization.md             # ~
specs/012-publishing-rendering/contracts/public-rendering.md  publishing-api.yaml   # ~
```

**Structure decision**:
- **Forms**: a new module in the standard two-project shape, slices under `Features/`.
- **Pages**: only the schema rules and the publish guard, beside 024's menu guard.
- **Rendering**: one block renderer, the script and the uncached response page, like
  Navigation.
- **Shared**: the email abstraction, since Principle IV names email as shared
  infrastructure.
- **Admin**: the block in `editor/blocks/form/`, the screens in `features/forms/`.

## Complexity Tracking

Status of exceptions carried over from earlier plans:

| Exception | Status after this feature |
|---|---|
| **E-1** … **E-4**, **E-6** | Closed earlier. |
| **E-5** (no browser in the release smoke test) | Unchanged. The screen-reader and no-script checks of the published form are manual (quickstart §7) until #25. |

**New exceptions**: none. The new module is made legal by the amendment rather than recorded
as an exception.

## Risks

- **Browser autofill fills the trap field**: real enquiries land in Spam.
  - *Mitigation*: the Spam list with restore (clarification 5); `autocomplete="off"` and an
    unattractive name.
- **Shared addresses** (offices, carrier NAT) hit the rate limit.
  - *Mitigation*: per-site limits, configurable (upgrade note 4); the limited visitor keeps
    their values and sees when to retry.
- **A proxy that rewrites the CSP** drops `form-action`: the browser blocks submission.
  - *Mitigation*: upgrade note 3; the release smoke test submits through the reference
    setup.
- **Retention is set by Editors in drafts**: it takes effect only after a Designer or
  Administrator publishes, and the panel shows how many submissions it will delete.
- **Mail server accepts but never delivers**: outside our control; the test email and the
  delivery state show what we know.
- **Registry lag after a publish** (one outbox tick): only the admin filter list is
  briefly stale; submitting does not depend on it (FS-07).

## Deferred (recorded, not built)

- Fields as child blocks with free layout; conditional and calculated fields; multi-step forms.
- File uploads; payment fields; confirmation emails to visitors.
- CAPTCHA and third-party spam services; webhooks and CRM integrations.
- Per-site SMTP servers and sender addresses.
- Trigram search for very large submission stores.
- Replying from the admin; assigning or tagging submissions.
