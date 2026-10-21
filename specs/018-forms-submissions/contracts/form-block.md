# Contract draft: the `form` node (page-schema v1 addition)

Draft of the changes to `docs/contracts/page-schema.md` and `page-schema/v1.schema.json`.
The change is **additive** under §6 (a new node type), so documents stay at version 1.

## 1. Placement

- `form` joins `contentChild`, so it may appear wherever a Text or Button may, in pages,
  templates and components.
- It has **no children** (`children: []`).
- It is not allowed inside `collection-list` cards (`listChild`): a form per list item would
  give one form many sources with nothing to tell them apart. Rule 26 below.

## 2. Props

| Prop | Type | Default | Notes |
|---|---|---|---|
| `name` | string 1–100 | `"Contact form"` (admin, localised) | required |
| `fields` | array 0–50 of `formField` | one email field | at least 1 to publish |
| `submitLabel` | string ≤ 60 | *(absent: "Send", site language)* | |
| `successMessage` | string ≤ 1,000 | *(absent: default text)* | plain text, line breaks kept |
| `errorMessage` | string ≤ 1,000 | *(absent: default text)* | |
| `redirect` | `{ "type": "page", "pageId": uuid }` or `{ "type": "url", "href": string ≤ 2,048 }` | *(absent: show the success message)* | `href` scheme `https` or `http` only; tagged by `type` like `link`, as the schema subset requires (§7) |
| `notify` | `formNotify` | *(absent: no notification)* | |
| `retentionDays` | 30, 90, 180 or 365 | *(absent: off)* | |
| `layerLabel` | as other blocks | | never rendered |

### `formNotify`

| Prop | Type | Default |
|---|---|---|
| `recipients` | array 0–10 of email strings (≤ 254, FS-08 shape) | `[]` |
| `subject` | string ≤ 200, no CR or LF | *(absent: "New submission: {name}")* |
| `includeValues` | boolean | `true` |
| `replyFieldId` | field id | *(absent)* |

### `formField`

Common props:

| Prop | Type | Notes |
|---|---|---|
| `id` | string, `^[0-9a-f]{12}$` | unique in the form; never changes |
| `type` | `text`, `textarea`, `email`, `phone`, `number`, `date`, `select`, `radio`, `checkboxes`, `checkbox`, `consent` | |
| `label` | string 1–200 | |
| `help` | string ≤ 500 | optional |
| `placeholder` | string ≤ 200 | text-like types only |
| `required` | boolean | default `false`; `consent` defaults to `true` |
| `message` | string ≤ 200 | optional; replaces every default error text of the field |

Type-specific props:

| Type | Props | Bounds |
|---|---|---|
| `text`, `email`, `phone` | `minLength`, `maxLength` | 0–500, default max 500 (`email` 254) |
| `textarea` | `minLength`, `maxLength` | 0–10,000, default max 10,000 |
| `number` | `min`, `max`, `wholeNumber` | finite numbers |
| `date` | `earliest`, `latest` | `YYYY-MM-DD` |
| `select`, `radio` | `options` | 1–100 of `{ value ≤ 200, label ≤ 200 }` |
| `checkboxes` | `options`, `minSelected`, `maxSelected` | as above; 0–100 |
| `checkbox` | (none) | value `"yes"` when ticked |
| `consent` | `statement` ≤ 1,000, `linkPageId` (uuid, optional) | the statement is the label shown beside the box; `label` names the column |

## 3. Rules beyond the schema

Numbered after the existing rules of page-schema.md §3, which end at 25 (017).

26. At most 20 forms per document (`too_many_forms`), and no `form` under a
    `collection-list` (`form_in_list`), both at the form's `/type` (save).
27. Field ids are unique within a form (`duplicate_field_id`, at the second field's `/id`)
    (save).
28. **Publish rules** (FS-04), checked by Pages when a page or component is published,
    by hand, by schedule or by rollback; the admin shows them on every change:
    1. `no_fields`: at least one field;
    2. `duplicate_label`: labels unique within the form, ignoring case;
    3. `no_options`, `duplicate_option`: `select`, `radio` and `checkboxes` have at least
       one option, with unique values;
    4. `min_above_max`: `minLength ≤ maxLength`, `min ≤ max`, `earliest ≤ latest`,
       `minSelected ≤ maxSelected ≤ options`;
    5. `reply_field_invalid`: `replyFieldId` names an `email` field of the form, and
       `includeValues` is not `false`;
    6. `redirect.pageId` and `consent.linkPageId` name live pages of the site.

    Refusal reasons: `form_invalid` (with `formId`, `fieldId` and `rule`) and
    `form_redirect_not_live` (with `formId` and `pageId`).

Final text: docs/contracts/page-schema.md §3.

## 4. Limits

- §4 gains: **Forms per document: 20** (rule 26).
- A field's props count toward the existing document size limit.

## 5. Rendering (§9 addition)

See [public-forms.md](./public-forms.md) §2. The canvas component draws the same markup,
disabled, with the success panel and error states previewable from the panel.

## 6. Examples (golden files)

`docs/contracts/page-schema/examples/`:
- `valid/form-every-field-type.json`;
- `valid/form-in-component.json` (also valid as a component document);
- `valid/form-minimal-draft.json` (no fields, duplicate labels: valid to save);
- `invalid/form_in_list--in-list-card.json` (rule 26);
- `invalid/too_many_forms--21.json` (rule 26);
- `invalid/duplicate_field_id--twice.json` (rule 27);
- `invalid/props_invalid--form-redirect-javascript.json` (schema);
- `invalid/props_invalid--form-too-many-recipients.json` (schema);
- `invalid/props_invalid--form-option-on-text.json` (schema: type-specific props);
- `invalid/children_not_allowed--form-child.json` (schema).

Both validators run them (`FormSchemaGoldenTests`, `documentValidator.spec.ts`). The publish
rules (rule 28) are not document examples: their cases are the shared fixture
`tests/fixtures/forms/setup-rules.json`.
