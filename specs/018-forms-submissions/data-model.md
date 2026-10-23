# Data Model: Forms and Submissions

**Feature**: `018-forms-submissions` | **Date**: 2026-10-21 | **Research**: [research.md](./research.md)

Two places hold data:
- **form definitions** live in page and component documents (page-schema v1, `form` node,
  [contracts/form-block.md](./contracts/form-block.md)), owned by Pages and published with
  them;
- **everything else** lives in the new `forms` schema, owned by the Forms module.

Every Forms table carries `site_id` with the EF Core global site filter (Principle V). Raw SQL
is used only for the rate-limit upsert (FS-13), which states the site explicitly.

## 1. Tables (schema `forms`)

### `forms`: the form registry (FS-05)

| Column | Type | Rule |
|---|---|---|
| `site_id` | uuid | PK part |
| `form_id` | text | PK part; the `form` node id |
| `name` | text | ≤ 100; last published name |
| `retention_days` | int null | null (off), 30, 90, 180 or 365; last published setting |
| `include_values` | bool | last published "include values in notifications" |
| `recipients` | text[] | 0–10 addresses, last published |
| `subject` | text | ≤ 200, last published |
| `reply_field_id` | text null | an email field id of the form |
| `fields` | jsonb | the last published field list (id, label, type), for the admin filter and column names |
| `last_published_at` | timestamptz | the latest event applied |
| `updated_at` | timestamptz | |

A form is **published** while it has at least one row in `form_placements`.

### `form_placements`: where a form is live (FS-05)

| Column | Type | Rule |
|---|---|---|
| `site_id` | uuid | PK part |
| `subject_kind` | text | `page` or `component`; PK part |
| `subject_id` | uuid | PK part |
| `form_id` | text | PK part |

Replaced for a subject on each publish or rollback event, and deleted on unpublish.

### `submissions`

| Column | Type | Rule |
|---|---|---|
| `id` | uuid | PK (v7, time-ordered) |
| `site_id` | uuid | |
| `form_id` | text | the form; no foreign key, so submissions outlive registry changes |
| `form_name` | text | the name when received, shown if the form is gone |
| `page_id` | uuid | source page (a template for item pages) |
| `page_path` | text | ≤ 2,048; the address it was sent from (FS-07) |
| `received_at` | timestamptz | |
| `values` | jsonb | `[{ "fieldId", "label", "type", "value" }]`, in field order; `value` is a string or an array of strings |
| `search_text` | text | normalised values (FS-15) |
| `fingerprint` | bytea | 32 bytes (FS-14) |
| `is_read` | bool | default false |
| `is_spam` | bool | default false |
| `notification_state` | text | see §3 |

Indexes:
- `(site_id, is_spam, received_at DESC, id)`;
- `(site_id, form_id, received_at DESC)`;
- `(site_id, form_id, fingerprint, received_at)`.

No visitor address, user agent or cookie is stored (FR-021).

### `notification_deliveries`

| Column | Type | Rule |
|---|---|---|
| `submission_id` | uuid | PK; cascade delete with the submission |
| `site_id` | uuid | |
| `state` | text | `pending`, `sending`, `sent`, `failed` |
| `attempts` | int | 0–6 |
| `next_attempt_at` | timestamptz null | for `pending` |
| `lease_until` | timestamptz null | for `sending` |
| `last_error` | text null | ≤ 500; SMTP reply code and text, or `interrupted`, `no_recipients_valid` |
| `sent_at` | timestamptz null | |

Recipients, subject and content are **not** stored: the worker builds the message at send
time from the submission and the registry's last published settings, so nothing personal is
duplicated. Index `(state, next_attempt_at)` across sites (the worker bypasses the site
filter with a commented `AcrossAllSites`, as `ThumbnailWorker` does, and a dedicated test).

### `spam_counters` (FR-019)

| Column | Type | Rule |
|---|---|---|
| `site_id`, `form_id`, `day` | uuid, text, date | PK |
| `trapped_kept` | int | stored as spam |
| `trapped_dropped` | int | trap filled and invalid |
| `rate_limited` | int | refused by FS-13 (form id from the route) |

Kept 90 days, then removed by the sweep.

### `rate_windows` (FS-13)

| Column | Type | Rule |
|---|---|---|
| `site_id` | uuid | PK part |
| `client_key` | bytea | 32 bytes, SHA-256 of the client address; PK part |
| `window_kind` | text | `minute` or `hour`; PK part |
| `window_start` | timestamptz | PK part |
| `count` | int | |

Rows older than one hour are deleted by the sweep.

### `submission_log` (FR-030, FS-20), append-only

| Column | Type | Rule |
|---|---|---|
| `id` | bigint identity | PK |
| `site_id` | uuid | |
| `at` | timestamptz | |
| `actor_user_id` | uuid null | null for `system` |
| `action` | text | `export`, `delete`, `restore`, `retention_purge`, `spam_purge` |
| `form_id` | text null | |
| `count` | int | |
| `filters` | jsonb null | exports: form, dates, read state, `searchUsed: bool`; never the words |

## 2. The form definition (in page documents)

Defined in [contracts/form-block.md](./contracts/form-block.md). In summary:

- **Form node props**: `name`, `fields[]`, `submitLabel`, `successMessage`, `errorMessage`,
  `redirect` (`{ pageId }` or `{ url }`), `notify` (`recipients[]`, `subject`,
  `includeValues`, `replyFieldId`), `retentionDays`.
- **Field**: `id`, `type`, `label`, `help`, `placeholder`, `required`, `message`, and per
  type: `minLength`/`maxLength`, `min`/`max`/`wholeNumber`, `earliest`/`latest`,
  `options[]` (`{ value, label }`), `minSelected`/`maxSelected`, `statement` with an
  optional `linkPageId` (consent).

## 3. States

### Submission

```text
 accepted (trap empty, values valid) ──► inbox, unread ──open──► inbox, read
                                              ▲    └──mark unread──┘
 accepted (trap filled, values valid) ──► spam ──restore──► inbox, unread (+ notification)

 inbox ──delete (Designer/Admin)──► gone      inbox ──retention period passed──► gone
 spam  ──delete (Designer/Admin)──► gone      spam  ──30 days──► gone
```

### Notification (`submissions.notification_state` mirrors the delivery)

| State | When |
|---|---|
| `not_needed` | the form has no recipients |
| `not_sent` | email is not configured; no delivery row |
| `pending` | waiting for its first or next attempt |
| `sending` | claimed by a worker (shown as pending) |
| `sent` | the mail server accepted it |
| `failed` | 6 attempts failed, or interrupted mid-send (reason kept) |
| `none` | spam: never notified unless restored |

Delivery transitions: `pending → sending → sent`; `sending → pending` (SMTP refused, attempts
left); `sending → failed` (attempts used, or lease expired). Restoring spam creates a
`pending` delivery (or `not_needed` / `not_sent`).

## 4. Submit outcomes (FS-06 to FS-09)

Checks run in this order; the first that applies decides.

| # | Check | Outcome | Stored | Counted |
|---|---|---|---|---|
| 1 | over a rate window | `limited` (429) | no | `rate_limited` |
| 2 | body over 256 KB (`413`) or not urlencoded (`400`) | `413` / `400` | no | — |
| 3 | form not found on the live page `_page` of this site | `gone` (503) | no | — |
| 4 | trap filled, values invalid | looks like `ok` | no | `trapped_dropped` |
| 5 | values invalid | `invalid` (422) with codes | no | — |
| 6 | same fingerprint within 10 s | looks like `ok` | no (the first stays) | — |
| 7 | trap filled | looks like `ok` | yes, spam | `trapped_kept` |
| 8 | otherwise | `ok` | yes, inbox, with a delivery | — |
| — | database failure | `error` (503) | no (one transaction) | — |

## 5. Validation rules (summary)

- **Schema (save)**: FS-04 first list; [form-block.md](./contracts/form-block.md) §3.
- **Publish (Pages guard)**: FS-04 second list; reasons `form_invalid`,
  `form_redirect_not_live`.
- **Values (submit)**: FS-08; codes in [public-forms.md](./contracts/public-forms.md) §4.

## 6. Upgrade

- One migration, Forms `Initial`, creates the schema and the seven tables. It is additive
  and automatic, and touches no other schema.
- Existing documents have no `form` node; the page-schema change is additive (v1).
- No backfill: the registry fills as pages with forms are published.
