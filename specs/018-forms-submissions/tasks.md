---

description: "Task list for 018-forms-submissions"
---

# Tasks: Forms and Submissions

**Input**: Design documents from `/specs/018-forms-submissions/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included.
- Constitution Principle X requires:
  - integration tests for every endpoint;
  - unit tests for domain logic;
  - golden files for contracts and for the rendering of every block type;
  - 80 % coverage on critical paths (here: submit, value rules, the notification worker, the
    sweep).
- The spec measures behaviour through automated checks: SC-003 … SC-009.
- Within each story, test tasks come first and must fail before implementation starts.

**Changed after analysis (2026-10-22)**: T002, T025, T031, T040 and T050 were extended, and
T098 (US1) and T099 (US4) were added in their phases. No task was renumbered.

**Organization**: tasks are grouped by user story (US1–US5 from spec.md), so each story can
be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: the user story the task belongs to (US1–US5)
- Every task names an exact file path

## Path Conventions

Shorthand used below:

- `FORMS` = `src/Modules/Forms/QuickBuilder.Forms`
- `FORMSC` = `src/Modules/Forms/QuickBuilder.Forms.Contracts`
- `PAGES` = `src/Modules/Pages/QuickBuilder.Pages`
- `PAGESC` = `src/Modules/Pages/QuickBuilder.Pages.Contracts`
- `RENDER` = `src/Modules/Rendering/QuickBuilder.Rendering`
- `RENDERC` = `src/Modules/Rendering/QuickBuilder.Rendering.Contracts`
- `SHARED` = `src/Shared/QuickBuilder.Shared.Infrastructure`
- `HOST` = `src/QuickBuilder.Host`
- `CONTRACTS` = `docs/contracts`
- `ADMIN` = `src/admin/src`
- `EDITOR` = `src/admin/src/editor`
- `ADMINTESTS` = `src/admin/tests`
- `ITESTS` = `tests/QuickBuilder.IntegrationTests`
- `UTESTS` = `tests/QuickBuilder.UnitTests`
- `ATESTS` = `tests/QuickBuilder.ArchitectureTests`
- `STESTS` = `tests/QuickBuilder.SmokeTests`
- `FIXTURES` = `tests/fixtures`

**Conventions**:
- **Backend slices** live under `FORMS/Features/<Slice>/<Slice>.cs` (controller action,
  request and response records, handler, validator together), modelled on Pages' menu slices
  (`PAGES/Features/Menus/`). Every admin action:
  - is routed as in [contracts/forms-api.yaml](./contracts/forms-api.yaml);
  - declares exactly one `[RequireCapability(...)]` or `[RequireSignedIn]` (research FS-20);
  - dispatches through `IDispatcher`;
  - refuses with the `forms.*` codes in forms-api.yaml as `application/problem+json`;
  - is registered in `FORMS/FormsModule.cs` and listed in `ITESTS/Infrastructure/EndpointCatalog.cs`.
- **Site filter**: every Forms entity carries `SiteId` with the global filter. The only
  bypasses are the notification worker and the sweep, each with `AcrossAllSites("<reason>")`
  as `ThumbnailWorker` does, and a dedicated test.
- **Raw SQL**: only the rate-window upsert (FS-13), parameterised, through EF `SqlQuery`,
  with the site explicit.
- **The public path**: Rendering stays read-only; block renderers stay synchronous and do no
  I/O. Forms reads Pages only through `PAGESC` readers.
- **Admin text**: every visible string goes through vue-i18n keys added to
  `ADMIN/i18n/en.json` in the same task. Public texts go through
  `RENDER/Features/NotFound/PublicTexts.cs`, in every language it already supports.
- **Logs, audit rows and events** carry ids, counts and codes only, never submitted values,
  recipients, search words or client addresses (FR-022).

**References**:
- decisions: `FS-xx` in [research.md](./research.md);
- tables, states and the submit outcome order: [data-model.md](./data-model.md);
- the `form` node: [contracts/form-block.md](./contracts/form-block.md);
- the submit endpoint and published HTML: [contracts/public-forms.md](./contracts/public-forms.md);
- the admin API: [contracts/forms-api.yaml](./contracts/forms-api.yaml);
- references, Contracts members, events, capabilities, configuration: [contracts/module-surface.md](./contracts/module-surface.md).

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: the feature's documents, the constitution amendment and the empty module.

- [X] T001 Commit `specs/018-forms-submissions/` (spec, plan, research, data model, contracts, quickstart, checklist, tasks) as the first commit on branch `018-forms-submissions`, referencing #18.
- [X] T002 Amend `.specify/memory/constitution.md` to 2.5.0: add `Forms` to Principle I's module list, update the Sync Impact Report (MINOR; reason: specs/018-forms-submissions research FS-01; affected specs: none) and **Last Amended**. Add `Forms` wherever `docs/contributing.md` lists the modules. Precondition: the maintainer approved adding `Forms` to Principle I (given 2026-10-22 after `/speckit-analyze` finding C1; record it in the PR description). T003 must not start before T002 is committed.
- [X] T003 Create `FORMSC/QuickBuilder.Forms.Contracts.csproj` and `FORMS/QuickBuilder.Forms.csproj` with the same properties and references pattern as the Pages projects (`FORMS` references `FORMSC`, `PAGESC`, `RENDERC`, `src/Modules/Sites/QuickBuilder.Sites.Contracts`, `src/Modules/Identity/QuickBuilder.Identity.Contracts`, `SHARED`). Add both to `QuickBuilder.slnx`, add `FORMS/FormsModule.cs` with an empty `AddFormsModule(IServiceCollection, IConfiguration)`, and call it plus `AddApplicationPart(typeof(FormsModule).Assembly)` in `HOST/Program.cs`. Reference `FORMS` from the test projects as the other modules are.
- [X] T004 Add Forms to `ATESTS/ModuleBoundaryTests.cs` (allowed references exactly as module-surface.md §1) and confirm the suite passes with the empty module.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: everything every story builds on:
- the `form` node in the page schema, validated on both sides;
- the Forms tables, entities and migration;
- capabilities, errors and the `/_forms` prefix;
- the form definition parser and the form registry fed by publishing events.

**⚠️ CRITICAL**: no user story work can begin until this phase is complete.

### The `form` node (FR-001, FR-005 to FR-008, FS-02, FS-04)

- [X] T005 Extend `CONTRACTS/page-schema/v1.schema.json`, staying at version 1: `$defs/form`, `$defs/formField` (a `oneOf` per type, so type-specific props are allowed only on their type), `$defs/formNotify` and `$defs/formRedirect` exactly as contracts/form-block.md §2, and `form` added to `contentChild` but not `listChild`. `PageSchemaSubsetTests` must pass.
- [X] T006 Update `CONTRACTS/page-schema.md` from form-block.md: the §2 row, rules 26–28 in §3 (28 marked as a publish rule), the 20-forms limit in §4, an "Additions in 018 (still version 1)" paragraph in §6 and the §9 rendering note. Run `ITESTS/Pages/PageSchemaContractTests.cs`; it must pass.
- [X] T007 [P] Create the golden examples of form-block.md §6 in `CONTRACTS/page-schema/examples/` (`valid/`, and `invalid/<rule>--<case>.json` with entries in `invalid/expected.json` naming `rule` and `pointer`). Rule 28's cases live in T021's fixture, not here.
- [X] T008 [P] Create `UTESTS/Pages/Forms/FormSchemaGoldenTests.cs`, running T007's `valid/` and `invalid/` examples through `PAGES/Infrastructure/Schema/PageDocumentValidator.cs`. It must fail first.
- [X] T009 [P] Extend `ADMINTESTS/editor/documentValidator.spec.ts` with T007's `valid/` and `invalid/` examples through `EDITOR/schema/documentValidator.ts`, loading files as `ADMINTESTS/editor/contractFiles.ts` does. It must fail first.
- [X] T010 Accept the `form` node in `PAGES/Infrastructure/Schema/PageDocumentValidator.cs`, adding rule 26 (no `form` under `collection-list`), rule 27 (unique field ids) and the 20-forms limit, and refresh the embedded schema in `PAGESC/PageSchema.cs`. Make T008 pass.
- [X] T011 [P] Accept the `form` node in `EDITOR/schema/schema.ts` (`blockTypes`, `canDrop` refusing list cards), `EDITOR/schema/documentValidator.ts` (rules 26, 27) and `EDITOR/schema/limits.ts` (20 forms). Make T009 pass.

### Forms storage (data-model §1)

- [X] T012 Create the entities in `FORMS/Domain/`: `FormRecord.cs`, `FormPlacement.cs`, `Submission.cs` (with `SubmissionValue` records and the `NotificationStates` constants of data-model §3), `NotificationDelivery.cs` (states `pending`, `sending`, `sent`, `failed`), `SpamCounter.cs`, `RateWindow.cs` and `SubmissionLogEntry.cs` (actions as constants). `Submission` exposes `MarkRead`, `MarkUnread` and `Restore` (spam → inbox, unread; refuses when not spam).
  *Done as*: `SpamCounter` and `RateWindow` share `FORMS/Domain/SpamProtection.cs`; `FormLimits.cs` holds the column bounds. `Submission.Receive` takes the site apart from its `SubmissionReceipt`, because the architecture test treats any type with a `SiteId` as a site-owned entity.
- [X] T013 Create `FORMS/Infrastructure/FormsDbContext.cs` (schema `forms`, own migrations history table, the site filter on all seven tables, `values` and `fields` as `jsonb`, `recipients` as `text[]`, the indexes of data-model §1, cascade from `submissions` to `notification_deliveries`, the processed-events table as other modules map it) and `FORMS/Infrastructure/FormsDbContextFactory.cs`. Register it in `FORMS/FormsModule.cs` so `StartupTasks.RunMigrationsAsync` migrates it, and generate `FORMS/Infrastructure/Migrations/<ts>_Initial.cs` (forward-only, additive).
  *Done as*: the migration is `20261021174231_InitialForms` (the `Initial<Module>` name contributing.md gives).
- [X] T014 [P] Create `FORMSC/FormsCapabilities.cs` (`forms.submissions.view`, `forms.submissions.delete`, `forms.email.test`) and register them in `FORMS/FormsModule.cs` with the role grants of research FS-20. Record them in `specs/002-identity-setup/contracts/authorization.md` §2 under "Registered by 018".
  *Done as*: `FORMS/FormsCapabilities.cs`, internal, as `CmsCapabilities` is: nothing outside the module names them. The Contracts project holds only `FormsRoutes`.
- [X] T015 [P] Create `FORMS/FormsErrors.cs` and `FORMS/FormsErrorResults.cs` with every `forms.*` code in forms-api.yaml (`invalid_filter`, `submission_not_found`, `not_spam`, `email_not_configured`, `email_failed`) and their HTTP statuses, as `PAGES/PagesErrors.cs` and `PagesErrorResults.cs` do.
- [X] T016 [P] Create `FORMSC/FormsRoutes.cs` (`SubmitPrefix = "/_forms"`) and add it to `PublicReserved` in `HOST/Routing/RoutePrefixes.cs` with a remark like the `/_media` one. Run `ReservedAddressCoverageTests`; it must pass without a page-schema §5 entry (the slug grammar cannot produce `_`).

### Form definitions and the registry (FS-03, FS-05)

- [X] T017 [P] Create `UTESTS/Forms/FormDefinitionTests.cs` for parsing a `form` node (from T007's valid examples) into `FormDefinition`: name, fields in order with their type-specific rules, messages, redirect, notify defaults (`includeValues` true, default subject) and retention; and for finding every `form` node of a document, including nested in sections and containers. It must fail first.
- [X] T018 Create `FORMS/Domain/FormDefinition.cs` (immutable; `Parse(JsonElement node)` and `FindAll(JsonElement document)`, with the field records and the default texts' keys, not their text). Make T017 pass.
- [X] T019 [P] Create `ITESTS/Forms/FormRegistryTests.cs`: publishing a page and a component with forms fills `forms` and `form_placements`; a republish without a form removes its placement but keeps its `forms` row and last settings; rollback applies the rolled-back version; unpublish deletes placements; the same event delivered twice writes the same rows; another site's events never touch this site's rows. It must fail first.
- [X] T020 Create `FORMS/Features/RefreshFormRegistry/RefreshFormRegistryHandler.cs`, consuming `Pages.PagePublished`, `Pages.PageUnpublished`, `Pages.ComponentPublished` and `Pages.ComponentUnpublished` as other modules' outbox consumers do: read the subject's live document with `IPublishedContentReader` (`GetLivePageAsync` or `GetLiveComponentsAsync`), parse its forms with `FormDefinition.FindAll`, and in one transaction upsert `forms` and replace the subject's `form_placements` (FS-05). Register it in `FORMS/FormsModule.cs`. Add the new consumer to `specs/001-foundation-skeleton/contracts/integration-events.md`. Make T019 pass.
  *Done as*: the handler takes a transaction lock per subject and reads the live document inside it, so it needs no processed-events row; it sets its scope's site to the event's, because `IPublishedContentReader` filters by the resolved site. `ITESTS/Forms/FormsWorld.cs` is the shared test world for Forms.

**Checkpoint**: the `form` node validates on both sides; the Forms tables exist; the registry follows publishes. User stories can start.

---

## Phase 3: User Story 1 - Building a contact form on a page (Priority: P1) 🎯 MVP

**Goal**: members add a Form block, configure its fields, rules and messages, see problems
while they work, and publish a page whose form is rendered on the server.

**Independent Test**: quickstart §1. A form with every field type round-trips through the
editor; an incomplete form is refused at publish with its problem; the published page shows
every field with label, help, required marker and choices, in order, with and without scripts.

### Tests for User Story 1 ⚠️

- [X] T021 [P] [US1] Create `FIXTURES/forms/setup-rules.json` (a form node → the list of rule-28 problems: `rule`, `fieldId`) covering every item of form-block.md rule 28 except 28.6, and `UTESTS/Pages/Forms/FormPublishRulesTests.cs` running it through `FormPublishRules`. It must fail first.
- [X] T022 [P] [US1] Create `ADMINTESTS/editor/formSetup.spec.ts`, running the same fixture through `EDITOR/forms/formSetup.ts`. It must fail first.
- [X] T023 [P] [US1] Create `ITESTS/Publishing/FormPublishGuardTests.cs`: publishing a page or a component whose form breaks rule 28 is refused with `409 pages.form_invalid` (with `formId`, `fieldId`, `rule`), and with `409 pages.form_redirect_not_live` when `redirect.pageId` or a consent `linkPageId` is not live; the same refusals for rollback to such a version and for a scheduled publish (the schedule fails as 012 FR-021 describes); a valid form publishes. It must fail first.
- [ ] T024 [P] [US1] Create `UTESTS/Rendering/Forms/FormGoldenTests.cs` with golden HTML in `FIXTURES/rendering/golden/forms/` for: every field type, required and optional, help and placeholder, a consent with a page link, a form inside a component shown twice on one page (scoped ids), and labels and messages containing `<`, `"`, `&` and script-like text (SC-008). Run axe-equivalent checks the existing golden tests use. It must fail first.
- [ ] T025 [P] [US1] Create `ADMINTESTS/editor/FormBlock.spec.ts`: inserting a Form block gives a named form with one email field; copying and pasting a Form block gives it a new node id (a new form) and keeps its field ids; adding, editing, moving (buttons and keyboard) and removing fields; type-specific inputs per type; the panel lists every setup problem; the canvas markup matches the golden markup for the same node (as `CanvasMarkupParity.spec.ts` does for other blocks); axe passes. It must fail first.

### Implementation for User Story 1

- [X] T026 [US1] Create `PAGES/Domain/Forms/FormPublishRules.cs` (pure: rule 28.1–28.5 for one `form` node, returning every problem in node order; and a list of the page ids that 28.6 must check). Make T021 pass.
- [X] T027 [US1] Run the form guard in `PAGES/Infrastructure/PublishingWrites.cs` for page and component publish, rollback and scheduled runs, beside 024's unpublished-menu guard: every `form` node through `FormPublishRules`, then the 28.6 page ids through the existing link-target liveness read. Add `form_invalid` and `form_redirect_not_live` to `PAGES/PagesErrors.cs` and `PAGES/PagesErrorResults.cs`, document both in `specs/012-publishing-rendering/contracts/publishing-api.yaml`, and add their messages to `ADMIN/features/publishing/errorMessages.ts` with `ADMIN/i18n/en.json`. Make T023 pass.
- [X] T028 [P] [US1] Create `EDITOR/forms/formSetup.ts`, mirroring T026 with the same rule order and codes. Make T022 pass.
- [ ] T029 [US1] Create `EDITOR/blocks/form/definition.ts` (groups: content — name, fields, submit label; messages — success, error; after submit — redirect; the retention and notify groups are added by T063 and T075) with `initialProps` giving a localised name and one email field with a new 12-hex id, and `EDITOR/blocks/form/FormBlock.vue` drawing the markup of public-forms.md §2.1, disabled, never submitting. Register it in `EDITOR/blocks/registry.ts` and add `.qb-form` styles to `EDITOR/blocks/blocks.css` (shared with published pages).
- [ ] T030 [P] [US1] Create `EDITOR/panel/controls/FormFieldsControl.vue` (the ordered list: add by type, edit, move up/down with buttons and with keys, remove with confirmation, each move announced) and `EDITOR/panel/controls/FormFieldDialog.vue` (common props and the type-specific props of form-block.md §2, options editor for choice types, a page picker for the consent link using `ADMIN/components/PagePicker.vue`). New field ids come from a 12-hex generator in `EDITOR/forms/fieldIds.ts`.
- [ ] T031 [P] [US1] Create `EDITOR/panel/controls/FormRedirectControl.vue`: none, a page (`PagePicker`, live state shown), or an `https`/`http` URL validated as the schema does. Show the redirect as broken (with the reason) when its page is not live.
- [ ] T032 [US1] Show `formSetup.ts` problems for the selected Form block in `EDITOR/panel/PropertyPanel.vue`, each linking to its field, and mark the block in the canvas while it has problems. Add every new string to `ADMIN/i18n/en.json`. Make T025 pass.
- [ ] T033 [US1] Add the form texts of public-forms.md §2.3 (submit, success, error, rate limit, summary heading, required marker, trap label, and one message per value code with placeholders) to `RENDER/Features/NotFound/PublicTexts.cs` in every language it supports, with the existing completeness test extended to them.
- [ ] T034 [US1] Create `RENDER/Rendering/Forms/FormMarkup.cs` (writes public-forms.md §2.1 through `HtmlWriter`: success panel, form element with `action` `{base}/_forms/{formId}`, hidden `_page` and `_path`, fields with native constraint attributes, `fieldset`/`legend` for groups, `aria-describedby`, the `data-msg-*` texts in the page's language; an optional `FormState` adds values, errors, `aria-invalid`, the summary and banners) and `RENDER/Rendering/Blocks/FormRenderer.cs` (the `IBlockRenderer` for `form`, ids prefixed with the scope). Register the renderer in `RENDER/RenderingModule.cs`. The trap field is added by T081. Make T024 pass and keep `BlockRendererCoverageTests` passing.
- [ ] T035 [US1] Add the `.qb-form` rules to `RENDER/Rendering/BlockStylesheet.cs` from the same source as `EDITOR/blocks/blocks.css` (the existing parity test must pass): layout, errors, the summary, the success panel hidden by default and shown with the form hidden under `.qb-form__sent:target`.
- [ ] T098 [US1] Record a `form` node's `fields[].linkPageId` (consent fields) as a page dependency in `PAGES/Domain/DependencyExtractor.cs` and as a page reference in `RENDER/Domain/RenderReferences.cs`, as button and heading links are. Extend `ITESTS/Rendering/CacheInvalidationTests.cs`: moving or unpublishing the linked page refreshes the published page that shows the form, and pages without the form are not refreshed. *(Added after analysis, finding H1; runs after T034.)*

**Checkpoint**: a form can be built, checked, published and seen on a page. Submitting is not
yet possible (the page's CSP still says `form-action 'none'`).

---

## Phase 4: User Story 2 - A visitor submits the form and gets clear feedback (Priority: P1)

**Goal**: `POST /_forms/{formId}` checks values against the published form and stores valid
submissions; visitors see errors, success or a redirect, with and without scripts.

**Independent Test**: quickstart §2. Every field type and rule with valid, invalid and
crafted requests, scripts on and off; only valid submissions are stored; refusals name every
failing field; success shows the message or redirects.

### Tests for User Story 2 ⚠️

- [ ] T036 [P] [US2] Create `FIXTURES/forms/value-rules.json` (field definition + submitted values → normalised values or the value code), covering every type and every code of public-forms.md §4, trimming, line-break normalisation, NFC, the 254-character email limit, checkbox counts and a field `message`.
- [ ] T037 [P] [US2] Create `UTESTS/Forms/FormValueRulesTests.cs` running T036 through `FormValueRules`. It must fail first.
- [ ] T038 [P] [US2] Create `ADMINTESTS/editor/formRules.spec.ts` running T036 through `EDITOR/forms/formRules.ts`. It must fail first.
- [ ] T039 [P] [US2] Create `UTESTS/Forms/SubmitPolicyTests.cs` for the outcome order of data-model §4 rows 3, 5, 8 and the database-failure row (rows 1, 4, 6 and 7 are added by T082): `gone`, `invalid` with every failing field, `ok`. It must fail first.
- [ ] T040 [P] [US2] Create `ITESTS/Forms/SubmitEndpointTests.cs`:
  - JSON (`Accept: application/json`): `ok`; `invalid` with codes for every failing field; unknown keys ignored and never stored; `gone` for an unknown form, a form on an unpublished page, a form id from another site, and a draft-only form;
  - HTML: `303` to `{_path}#qb-sent-{formId}`; `303` to a live redirect page's address and to an external URL; `422` page with values and errors and `Cache-Control: no-store`; `503` page with the error message; `404` not-found page when `_path` no longer resolves;
  - a form in a component on a page and on an item page (template), recording `page_id` and `page_path`; a `_path` that does not belong to the page is replaced by the page's own address;
  - `413` over 256 KB, `400` for JSON or multipart bodies, `405` with `Allow: POST` for GET;
  - the stored row: values with labels and types, `search_text`, `form_name`, unread, inbox;
  - nothing in the captured logs contains a submitted value (`ITESTS/Infrastructure/LogCapture.cs`);
  - after a refusal, the next GET of the page is the unchanged cached page;
  - a redirect page unpublished after the form's publish: success at `#qb-sent-…` (JSON `ok` without `redirect`), no error.
  It must fail first.
- [ ] T041 [P] [US2] Create `UTESTS/Rendering/Forms/FormScriptTests.cs` (bytes pinned, size ≤ 6 KB, hash, written once per page after the first form, CSP lists both hashes on a page with a menu and a form) and `ADMINTESTS/rendering/formScript.spec.ts`, which loads the script text from `FormScript.cs` as `navScript.spec.ts` loads `NavScript.cs`, runs T036 through its rules, and drives a rendered golden form in jsdom: `novalidate` set, errors and summary shown and focused, `fetch` with `Accept: application/json`, button disabled in flight, success panel focused, `redirect` followed. They must fail first.
- [ ] T042 [P] [US2] Extend `UTESTS/Rendering/Forms/FormGoldenTests.cs` with state cases: invalid (values kept, errors, summary with links, `aria-invalid`), the limited banner and the error banner.

### Implementation for User Story 2

- [ ] T043 [US2] Create `FORMS/Domain/ValueCodes.cs` and `FORMS/Domain/FormValueRules.cs` (pure: normalise then check one field's submitted values, FS-08). Make T037 pass.
- [ ] T044 [P] [US2] Create `EDITOR/forms/formRules.ts`, mirroring T043. Make T038 pass.
- [ ] T045 [P] [US2] Create `FORMS/Domain/SearchText.cs` (normalisation as Media's `SearchText`: lower case, NFKD without marks, collapsed spaces; `LikePattern` with escaping) and `FORMS/Domain/Fingerprint.cs` (SHA-256 of form id and normalised values in field order, FS-14), with `UTESTS/Forms/SearchTextTests.cs` and `UTESTS/Forms/FingerprintTests.cs`.
- [ ] T046 [US2] Create `FORMS/Infrastructure/LiveFormLocator.cs` (FS-07: `GetLivePageAsync` for `_page`, find the form node in it, else in its live components via `GetLiveComponentsAsync`; return the `FormDefinition` and the recorded path) and `FORMS/Domain/SubmitPolicy.cs` (the outcome decision of data-model §4 as a pure function of its inputs). Make T039 pass.
- [ ] T047 [US2] Create `RENDERC/IFormResponsePage.cs` and `RENDERC/FormState.cs` exactly as module-surface.md §2. Thread an optional `FormState` through `RENDER/Domain/RenderInputs.cs` and `RENDER/Domain/RenderContext.cs` so `FormRenderer` passes it to `FormMarkup` for the form it names. Make T042 pass.
- [ ] T048 [US2] Create `RENDER/Features/FormResponse/FormResponsePage.cs` implementing `IFormResponsePage`: resolve `path` as `PageResponder` does (live page, else item page by template), render with the state **without** the cache, return the given status with `Cache-Control: no-store` and the page CSP, or null when the address no longer resolves. Register it in `RENDER/RenderingModule.cs`.
- [ ] T049 [US2] Create `RENDER/Rendering/Forms/FormScript.cs` (the inline script of FS-10 as a constant, with its `Hash`), write it once after the first form, and change `RENDER/Domain/RenderedPage.cs` so the page carries a list of script hashes and the external redirect origins of its forms. In `RENDER/Features/RenderPage/PageResponder.cs`, `ContentSecurityPolicy` lists every script hash and, on pages with a form, `form-action 'self'` plus those origins and `connect-src 'self'`; other pages keep `form-action 'none'`. Update `NavScript` users and their tests for the list. Make T041 pass.
- [ ] T050 [US2] Create `FORMS/Features/Submit/Submit.cs` (handler: locate the form, check every field with `FormValueRules`, decide with `SubmitPolicy`, and on `ok` insert the `Submission` with values, `search_text`, `fingerprint`, `form_name` and `notification_state = not_needed` in one transaction) and `FORMS/Features/Submit/SubmitController.cs` (`POST /_forms/{formId}`, `[AllowAnonymous]`, `[RequestSizeLimit(262144)]`, urlencoded only, `_page`/`_path`/`f.<id>` parsing, `405` for other methods, JSON or HTML by `Accept` as public-forms.md §2.4 and §3, `303` targets built from the live redirect page's address with the base path, refusals through `IFormResponsePage`). When `redirect.pageId` is no longer live, answer as without a redirect (`{_path}#qb-sent-{formId}`, or JSON `ok` without `redirect`). Use `ICurrentSite`; return `404` when no site is resolved. Make T040 pass.
- [ ] T051 [US2] Add `POST /_forms/{formId}` (and `405` for other methods) to the anonymous allow-list in `specs/002-identity-setup/contracts/authorization.md` §4 and to `ATESTS/AuthorizationDeclarationTests.cs`; update `specs/012-publishing-rendering/contracts/public-rendering.md` (the `/_forms` prefix, CSP on pages with forms, script hashes as a list) and list the endpoint in `ITESTS/Infrastructure/EndpointCatalog.cs`.

**Checkpoint**: visitors can submit; submissions are stored. Members cannot read them yet
except in the database.

---

## Phase 5: User Story 3 - An editor reads, searches and exports submissions (Priority: P1)

**Goal**: Submissions in the admin: list, filters, search, detail, read state, delete
(Designers and Administrators), CSV export, the unread badge, and per-form retention.

**Independent Test**: quickstart §3 and §6. Two forms on two sites with awkward values;
filter, search, open, mark and delete as each role; export and open in a spreadsheet; no
member sees another site's submissions; retention removes old submissions.

### Tests for User Story 3 ⚠️

- [ ] T052 [P] [US3] Create `ITESTS/Forms/SubmissionEndpointsTests.cs`:
  - list: newest first, 50 per page with the keyset cursor, `preview`, `formPublished`, `notification`; filters by form, dates and read state; search by words in any value ignoring case and accents, all words required, `%` and `_` treated literally; `400 forms.invalid_filter` for a search over 200 characters;
  - get marks read; `PUT …/read` toggles; unread count;
  - forms list (registry) and retention impact counts;
  - delete: Designer and Administrator succeed and a `submission_log` row records actor and count; Editor gets `403`; ids of another site are ignored;
  - every operation answers `404` to a member of another site only (FR-025, SC-006 functional part).
  It must fail first.
- [ ] T053 [P] [US3] Create `UTESTS/Forms/CsvWriterTests.cs` (RFC 4180 quoting, `\r\n`, BOM, `; `-joined checkbox values, `'` prefix for `=`, `+`, `-`, `@`, tab and carriage return) and `ITESTS/Forms/SubmissionExportTests.cs` (headers `Content-Type`, `Content-Disposition`, `Cache-Control: no-store`; columns time, form, page, then labels in first-appearance order, merged across forms by label; removed fields still exported; spam excluded; filters applied; a submission inserted during the export is not in the file; a `submission_log` row with filters and `searchUsed` but no words; Vietnamese and emoji survive). They must fail first.
- [ ] T054 [P] [US3] Create `ITESTS/Forms/FormsSweepTests.cs` (retention part): a form with 30-day retention loses only its own older inbox submissions, using the last published setting after the form is unpublished; forms without retention keep everything; a `retention_purge` log row per form with the count; batches of 10,000; the sweep's cross-site query is the only filter bypass besides the worker (dedicated assertion). It must fail first.
- [ ] T055 [P] [US3] Create `ADMINTESTS/features/forms/SubmissionsView.spec.ts` and `SubmissionDetail.spec.ts` (filters, search, paging, selection, delete hidden for Editors, export link carries the filters, detail values by label, notification state text, the unread badge) and `ADMINTESTS/editor/FormRetentionControl.spec.ts` (choices, the impact warning before saving). axe passes. They must fail first.

### Implementation for User Story 3

- [ ] T056 [US3] Create the read slices in `FORMS/Features/`: `ListForms/`, `ListSubmissions/` (filters, `SearchText.LikePattern` per word, keyset on `(received_at, id)`, `permissions.delete` from the caller's capabilities), `GetSubmission/` (marks read), `MarkSubmission/`, `UnreadCount/` (capped at 1000) and `RetentionImpact/`, all `forms.submissions.view`.
- [ ] T057 [US3] Create `FORMS/Infrastructure/SubmissionLog.cs` (append-only writes, no values) and `FORMS/Features/DeleteSubmissions/DeleteSubmissions.cs` (`forms.submissions.delete`, at most 500 ids, inbox or spam, one log row). Make T052 pass.
- [ ] T058 [US3] Create `FORMS/Infrastructure/CsvWriter.cs` and `FORMS/Features/ExportSubmissions/ExportSubmissions.cs` (FS-16: one `REPEATABLE READ` read-only transaction, the label pass then streamed rows, file name from the site slug and UTC time, a log row). Make T053 pass.
- [ ] T059 [US3] Create `FORMS/Infrastructure/FormsSweepService.cs` (hourly `BackgroundService` modelled on `IdentityRetentionService`, batches of 10,000, `AcrossAllSites` with its reason) with the retention step of FS-17 and its log rows; spam purge, rate-window and counter clean-up are added by T087. Register it in `FORMS/FormsModule.cs`. Make T054 pass.
- [ ] T060 [P] [US3] Create `ADMIN/api/forms.ts` (typed calls for every operation in forms-api.yaml) and add the routes `/sites/:siteId/submissions` and `/sites/:siteId/submissions/:submissionId` to `ADMIN/app/router.ts`.
- [ ] T061 [US3] Create `ADMIN/features/forms/SubmissionsView.vue`, `SubmissionFilters.vue` and `SubmissionDetail.vue` (FS-21) with `ADMIN/i18n/en.json` strings; the "form no longer published" marker; retention shown per form.
- [ ] T062 [US3] Create `ADMIN/features/forms/useUnreadCount.ts` (refresh on navigation and every 60 s while visible) and show the badge beside a **Submissions** entry in the site navigation in `ADMIN/features/sites/SiteDetailView.vue` (where 024 added Menus).
- [ ] T063 [P] [US3] Create `EDITOR/panel/controls/FormRetentionControl.vue` (off, 30, 90, 180, 365 days; when shortened or turned on, shows the count from `RetentionImpact` before saving) and add the retention group to `EDITOR/blocks/form/definition.ts`. Make T055 pass.

**Checkpoint**: US1–US3 together are the smallest useful feature: build, submit, read, export.

---

## Phase 6: User Story 4 - Email notification when a submission arrives (Priority: P2)

**Goal**: per-form recipients receive a plain-text email for each submission, sent in the
background through the operator's SMTP server, with retries, states and a test email.

**Independent Test**: quickstart §4 with Mailpit: 0, 1 and 10 recipients; values on and off;
reply address; header-injection values; the server stopped and restarted; no SMTP configured.

### Tests for User Story 4 ⚠️

- [ ] T064 [P] [US4] Create `UTESTS/Shared/Email/SmtpOptionsValidatorTests.cs`: empty host is valid (email off); host without `From` fails naming the key; ports, timeout and `FromName` bounds; `Security=None` with a user name fails unless the host is loopback. It must fail first.
- [ ] T065 [P] [US4] Create `UTESTS/Forms/NotificationComposerTests.cs`: subject default and custom, CR and LF stripped; body with values (multi-line indented) and without; admin link from `Hosting:PublicUrl` and `Admin:PathPrefix`, and the no-link sentence when it is empty; reply address only with values on and a valid email value; fixed words in the site language. It must fail first.
- [ ] T066 [P] [US4] Add a Mailpit Testcontainers fixture in `ITESTS/Infrastructure/MailpitFixture.cs` and create `ITESTS/Forms/NotificationTests.cs`:
  - one email to all recipients with the expected subject, body, link and `Reply-To` (SC-005 timing: handed over within 1 minute);
  - values off: no values, no `Reply-To`;
  - a name containing `\r\nBcc: x@y` adds no header;
  - server stopped: `pending` with attempts and next time; restarted: `sent`;
  - permanent `5xx`: `failed` with the reply; six failed attempts: `failed`;
  - a delivery left `sending` past its lease becomes `failed` (`interrupted`) and is never sent;
  - two worker instances never send one delivery twice;
  - no SMTP configured: `not_sent`, no delivery row; no recipients: `not_needed`;
  - the worker's cross-site claim is the documented filter bypass (dedicated assertion);
  - no recipient or value appears in logs.
  It must fail first.
- [ ] T067 [P] [US4] Create `ITESTS/Forms/EmailEndpointsTests.cs`: `GET /api/v1/forms/email-status` for any signed-in user; `POST /api/v1/forms/email-test` sends to the Administrator's own address, `409 forms.email_not_configured` without SMTP, `502 forms.email_failed` with the reply when the server refuses, `403` for Designers. It must fail first.
- [ ] T099 [P] [US4] Create `ITESTS/Email/SmtpEmailSenderTests.cs` against the Mailpit fixture, as the provider suite Principle X requires: `None` and `StartTls` modes, sign-in, the timeout, `Retryable` true for 4xx and connection failures and false for 5xx, the reply text without credentials, and `DisabledEmailSender` when no host is set. It must fail first. *(Added after analysis, finding H2; runs before T070.)*
- [ ] T068 [P] [US4] Create `ADMINTESTS/editor/FormNotifyControl.spec.ts` (recipients validated, at most 10; subject; values switch disables the reply field; the warning when email is not configured) and `ADMINTESTS/settings/TestEmail.spec.ts`. They must fail first.

### Implementation for User Story 4

- [ ] T069 [US4] Add `MailKit` to `Directory.Packages.props` in a labelled group citing research FS-18 and its licence, reference it from `SHARED/QuickBuilder.Shared.Infrastructure.csproj`, and list MailKit, MimeKit and their dependencies with licences in `THIRD-PARTY-NOTICES.md`.
- [ ] T070 [US4] Create `SHARED/Email/IEmailSender.cs`, `EmailMessage.cs`, `EmailResult.cs` (module-surface.md §2), `SmtpOptions.cs`, `SmtpOptionsValidator.cs`, `SmtpEmailSender.cs` (MailKit; security mode; timeout; `Retryable` from the reply class; reply text without credentials), `DisabledEmailSender.cs` and `EmailRegistration.cs` (`AddEmail(IConfiguration)` choosing the sender by `Email:Smtp:Host`). Bind and `ValidateOnStart` the options in `HOST/Program.cs` like the other option groups. Make T064 pass.
- [ ] T071 [US4] Create `FORMS/Infrastructure/NotificationComposer.cs` (FS-19, building `EmailMessage` from a submission and its form's registry row, the site's language and name, `Hosting:PublicUrl`). Log one startup warning in `FORMS/FormsModule.cs` (a hosted start-up check) when email is configured and `Hosting:PublicUrl` is empty. Make T065 pass.
- [ ] T072 [US4] In `FORMS/Features/Submit/Submit.cs`, set `notification_state` from the form's recipients and `IEmailSender.IsConfigured` (`not_needed`, `not_sent` or `pending`) and insert the `pending` `NotificationDelivery` in the same transaction as the submission.
- [ ] T073 [US4] Create `FORMS/Infrastructure/NotificationWorker.cs` (FS-17: 5-second poll; claim with `FOR UPDATE SKIP LOCKED`, mark `sending` with a 2-minute lease and commit; send; record `sent`, or the next attempt at 1, 5, 15, 30, 60 minutes, or `failed`; expire leases to `failed`/`interrupted`; mirror the state onto `submissions.notification_state`). Register it in `FORMS/FormsModule.cs`. Make T066 pass.
- [ ] T074 [US4] Create `FORMS/Features/EmailStatus/EmailStatus.cs` (`[RequireSignedIn]`) and `FORMS/Features/TestEmail/TestEmail.cs` (`forms.email.test`; the address from `IUserDirectory`). Make T067 pass.
- [ ] T075 [P] [US4] Create `EDITOR/panel/controls/FormNotifyControl.vue` (recipients, subject, "include values in notifications", reply field limited to the form's email fields and disabled while values are off, and the warning from `email-status`) and add the notify group to `EDITOR/blocks/form/definition.ts`.
- [ ] T076 [P] [US4] Add a "Send test email" action for Administrators in `ADMIN/features/settings/` (beside the installation settings), showing success or the server's reply. Make T068 pass.
- [ ] T077 [US4] Document the `Email:Smtp:*` keys in a new "Email" section of `docs/configuration.md` (module-surface.md §5, the loopback rule, secrets) and an email set-up and test-email section in `docs/operations.md`, including `Hosting:PublicUrl` for links.

**Checkpoint**: notifications arrive; their state is visible on each submission.

---

## Phase 7: User Story 5 - Spam and abuse are kept out (Priority: P2)

**Goal**: the trap field with a Spam list, a per-address rate limit, duplicate suppression,
counters, restore and clean-up.

**Independent Test**: quickstart §5. Trap filled with valid and invalid values; above the
rate limit from one address and normal pace from many; the same submission twice within
10 seconds; restore from Spam; spam older than 30 days removed.

### Tests for User Story 5 ⚠️

- [ ] T078 [P] [US5] Extend `UTESTS/Forms/SubmitPolicyTests.cs` with rows 1, 4, 6 and 7 of data-model §4 and their order relative to the others. It must fail first.
- [ ] T079 [P] [US5] Create `ITESTS/Forms/SpamAndRateLimitTests.cs`:
  - trap filled and valid: stored as spam, `notification_state = none`, no email, response identical to `ok` (JSON and HTML);
  - trap filled and invalid: nothing stored, `trapped_dropped` counted, response identical to `ok`;
  - the sixth request in a minute from one address is `429` with `Retry-After` (HTML page with values, and JSON `retryAfter`), counted as `rate_limited`; the 31st in an hour likewise; other addresses and other sites are not affected; limits follow `Forms:RateLimit:*`;
  - on two app instances sharing the database, limits are shared (SC-004);
  - addresses behind a named proxy are the forwarded client's; IPv6 is grouped by /64;
  - the same values twice within 10 s: one submission, one delivery, both answered `ok`; after 10 s, a second one;
  - restore moves spam to the inbox unread and creates its delivery; restoring a non-spam submission is `409 forms.not_spam`;
  - spam is absent from the inbox list, the unread count, search and export, and listed with `box=spam`;
  - spam counters endpoint sums per form;
  - no client address, hashed or not, is stored outside `rate_windows`; the rate-window SQL states the site (dedicated test).
  It must fail first.
- [ ] T080 [P] [US5] Extend `ITESTS/Forms/FormsSweepTests.cs` (spam older than 30 days removed with a `spam_purge` row; rate windows older than an hour removed; counters older than 90 days removed) and `UTESTS/Rendering/Forms/FormGoldenTests.cs` (the trap field markup: off-screen class, `aria-hidden`, `tabindex="-1"`, `autocomplete="off"`, localised label), and create `ADMINTESTS/features/forms/SpamView.spec.ts` (list, restore, delete hidden for Editors, counters shown). They must fail first.

### Implementation for User Story 5

- [ ] T081 [US5] Write the trap field in `RENDER/Rendering/Forms/FormMarkup.cs` and `EDITOR/blocks/form/FormBlock.vue`, and the off-screen `.qb-form__hp` rule in `RENDER/Rendering/BlockStylesheet.cs` and `EDITOR/blocks/blocks.css` (FS-12).
- [ ] T082 [US5] Complete `FORMS/Domain/SubmitPolicy.cs` with the full order of data-model §4 (limited, trap-invalid, duplicate, trap-valid). Make T078 pass.
- [ ] T083 [US5] Create `FORMS/FormsOptions.cs` (`Forms:RateLimit:PerMinute`, `PerHour`, ranges and `PerHour ≥ PerMinute`, `ValidateOnStart` in `FORMS/FormsModule.cs`) and `FORMS/Infrastructure/RateWindows.cs` (the atomic `INSERT … ON CONFLICT … DO UPDATE … RETURNING` for the minute and hour windows through `SqlQuery`, the client key as SHA-256 of the address grouped as `SignInThrottleService` groups it, seconds to the window's end for `Retry-After`).
- [ ] T084 [US5] In `FORMS/Features/Submit/Submit.cs` and `SubmitController.cs`: count the request in `RateWindows` first; read `_hp`; take `pg_advisory_xact_lock` on the fingerprint and look for the same fingerprint within 10 seconds; store trapped valid submissions as spam with `notification_state = none` and no delivery; increment `spam_counters` for each counted outcome; answer silent outcomes exactly as `ok`; answer `limited` as public-forms.md §2.4 and §3.
- [ ] T085 [US5] Add `box=spam` to `FORMS/Features/ListSubmissions/` and exclude spam from inbox lists, unread count, search and export; create `FORMS/Features/RestoreSubmission/RestoreSubmission.cs` (`forms.submissions.view`; `Submission.Restore`; a delivery as `Submit` creates one; a `restore` log row) and `FORMS/Features/SpamCounters/SpamCounters.cs` (last 30 days per form). Make T079 pass.
- [ ] T086 [US5] Create `ADMIN/features/forms/SpamView.vue` (list, restore, delete for Designers and Administrators) and show the per-form counters in `ADMIN/features/forms/SubmissionsView.vue`, with `ADMIN/i18n/en.json` strings.
- [ ] T087 [US5] Extend `FORMS/Infrastructure/FormsSweepService.cs` with the spam purge (30 days, `spam_purge` rows), rate windows older than one hour and counters older than 90 days. Make T080 pass.
- [ ] T088 [US5] Document `Forms:RateLimit:*` in a new "Forms" section of `docs/configuration.md`, with the `Hosting:KnownProxies` warning (plan upgrade note 4).

**Checkpoint**: all five stories work independently and together.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T089 [P] Create `ITESTS/Isolation/Probes/FormsIsolationProbe.cs` covering every Forms admin endpoint and the submit endpoint with another site's ids and host names (FR-024), registered as the other probes are.
- [ ] T090 [P] Create `ITESTS/Rendering/UpgradeFrom024Tests.cs` on the previous release's database snapshot: the Forms migration adds the `forms` schema only; existing pages render the same body and the same CSP; nothing else changes (FR-041).
- [ ] T091 [P] Create `STESTS/SubmissionListPerformanceTests.cs` (100,000 submissions on a site, seeded as `ItemSeedSql.cs` seeds items: list, filters and search under 2 s; export of 10,000 under 30 s; SC-006) and `STESTS/FormRenderPerformanceTests.cs` (a page with two forms within 012's budgets on a miss and a hit; SC-009), and a submit p95 measurement against the plan's 300 ms goal.
- [ ] T092 Extend the release journey in `STESTS/ReferenceDeploymentSmokeTests.cs`: add a Form block to a page, publish, submit through the public host, and find the submission in Submissions.
- [ ] T093 [P] Update `docs/backlog.md` with the deferred items of plan.md, the site-deletion obligation and the #20 export obligations of module-surface.md §6; update `docs/contracts/README.md` if it lists node types.
- [ ] T094 [P] Add an `ATESTS` rule (or extend an existing one) asserting that no Forms type logs a property named after submission values, recipients or search text, alongside the log-capture assertions in T040 and T066.
- [ ] T095 Run coverage and confirm at least 80 % line coverage on `FORMS/Features/Submit/`, `FORMS/Domain/SubmitPolicy.cs`, `FORMS/Domain/FormValueRules.cs`, `FORMS/Infrastructure/RateWindows.cs`, `FORMS/Infrastructure/NotificationWorker.cs` and `FORMS/Infrastructure/FormsSweepService.cs`; add tests where it falls short.
- [ ] T096 Run quickstart.md end to end on the dev stack with Mailpit, including the manual screen-reader and no-script checks of §7, and record the results, timings (SC-001, SC-002) and any deviation in `specs/018-forms-submissions/quickstart-results.md`.
- [ ] T097 Open the pull request for branch `018-forms-submissions`, closing #18, listing the constitution amendment, the operator impact and upgrade notes from plan.md.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies. T002 (the constitution amendment) blocks T003 and
  everything after it.
- **Added after analysis**: T098 runs after T034; T099 runs before T070.
- **Foundational (Phase 2)**: depends on Setup; blocks every story.
- **US1 (Phase 3)**: depends on Phase 2.
- **US2 (Phase 4)**: depends on Phase 2, and on US1's renderer (T034, T035), because
  submissions come from rendered forms and refusals re-render them.
- **US3 (Phase 5)**: depends on Phase 2. Its tests seed submissions directly, so it can run
  in parallel with US2; end to end it needs US2.
- **US4 (Phase 6)**: depends on US2 (T050, for T072). T063 and T075 both edit
  `EDITOR/blocks/form/definition.ts`; do them one after the other.
- **US5 (Phase 7)**: depends on US2 (submit) and US3 (lists and the sweep, for T085 and T087);
  restore creates a delivery only when US4 is in (T085 checks for it).
- **Polish (Phase 8)**: after the stories it covers.

### Within Each User Story

- Tests first, failing; then domain; then infrastructure; then endpoints; then admin.
- Shared fixtures (T021, T036) come before both their C# and TypeScript consumers.
- Each story ends at its checkpoint with its tests passing.

### Parallel Opportunities

- Phase 2: T007, T008, T009 together; T014, T015, T016 together; T017 and T019 together.
- US1: T021–T025 together; then T028, T030, T031 beside T026/T027.
- US2: T036–T042 together; T044 and T045 beside T043.
- US3: T052–T055 together; T060 and T063 beside the backend slices.
- US4: T064–T068 together; T075 and T076 beside T073.
- US5: T078–T080 together.
- Polish: T089–T091, T093 and T094 together.

---

## Parallel Example: Foundational

```text
Task: "T007 Create the form golden examples in docs/contracts/page-schema/examples/"
Task: "T008 Create UTESTS/Pages/Forms/FormSchemaGoldenTests.cs"
Task: "T009 Extend ADMINTESTS/editor/documentValidator.spec.ts with the form examples"
Task: "T014 Create FORMSC/FormsCapabilities.cs"
Task: "T015 Create FORMS/FormsErrors.cs and FormsErrorResults.cs"
Task: "T016 Create FORMSC/FormsRoutes.cs and reserve /_forms"
```

## Parallel Example: User Story 2

```text
Task: "T036 Create FIXTURES/forms/value-rules.json"
Task: "T039 Create UTESTS/Forms/SubmitPolicyTests.cs"
Task: "T040 Create ITESTS/Forms/SubmitEndpointTests.cs"
Task: "T041 Create FormScriptTests.cs and formScript.spec.ts"
Task: "T042 Extend FormGoldenTests.cs with state cases"
```

---

## Implementation Strategy

### MVP First (User Stories 1–3)

1. Phases 1 and 2.
2. US1: build and publish a form (demoable: the form appears on the site).
3. US2: submit it (demoable: rows in the database, success and errors on the page).
4. US3: read and export (demoable: the whole enquiry loop).
5. **Stop and validate** with quickstart §1–§3 and §6. Do not expose forms on a public site
   before US5.

### Incremental Delivery

1. MVP (US1–US3).
2. US4: notifications, without changing anything visitors see.
3. US5: spam, limits and duplicates; from here forms are fit for public sites.
4. Polish: isolation, upgrade, performance, smoke, docs, quickstart results, PR.

If the PR must be split (plan.md, Principle XI), split along the same lines: (1) Phases 1–5,
(2) Phase 6, (3) Phase 7, each with its share of Phase 8.

---

## Notes

- [P] tasks touch different files and depend on no incomplete task.
- Verify each test fails before implementing; commit after each task or logical group,
  referencing #18.
- Keep values out of logs at every step; T040, T066 and T094 check it.
