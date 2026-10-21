# Contract: the published form and the submit endpoint

Covers what visitors receive and send. Admin operations are in
[forms-api.yaml](./forms-api.yaml).

## 1. Submit endpoint

`POST /_forms/{formId}` on a site's host name (public surface, site-resolved, anonymous).

**Request**
- `Content-Type: application/x-www-form-urlencoded`, at most 256 KB; anything else → `400`
  (or `413` over the size).
- Keys:

  | Key | Meaning |
  |---|---|
  | `_page` | the id of the page that served the form (a template for item pages) |
  | `_path` | the address the page was served at |
  | `_hp` | the trap field; must be empty |
  | `f.<fieldId>` | a field's value; repeated for `checkboxes` |

  Unknown keys are ignored and never stored.
- `Accept: application/json` selects the JSON response (§3); otherwise the HTML responses
  (§2.4).

**Other methods** → `405` with `Allow: POST`.

## 2. HTML

### 2.1 Markup of the `form` block (shape, for golden files)

```html
<div class="qb-form" id="{scope}-form">
  <div class="qb-form__sent" id="qb-sent-{formId}" role="status" tabindex="-1">
    <p>{successMessage}</p>
  </div>
  <form class="qb-form__form" method="post" action="{base}/_forms/{formId}"
        data-qb-form data-msg-required="…" data-msg-email="…" …>
    <!-- refusal only: -->
    <div class="qb-form__summary" role="alert" tabindex="-1" autofocus>
      <h2>{There is a problem}</h2>
      <ul><li><a href="#{scope}-f-{fieldId}">{message}</a></li></ul>
    </div>
    <input type="hidden" name="_page" value="{pageId}">
    <input type="hidden" name="_path" value="{path}">
    <div class="qb-form__hp" aria-hidden="true">
      <label for="{scope}-hp">{Leave this field empty}</label>
      <input id="{scope}-hp" name="_hp" type="text" tabindex="-1" autocomplete="off">
    </div>
    <div class="qb-form__field">
      <label for="{scope}-f-{id}">{label}<span class="qb-form__req"> ({required})</span></label>
      <p class="qb-form__help" id="{scope}-h-{id}">{help}</p>
      <p class="qb-form__error" id="{scope}-e-{id}">{message}</p>   <!-- refusal only -->
      <input id="{scope}-f-{id}" name="f.{id}" type="email" required maxlength="254"
             aria-describedby="{scope}-h-{id} {scope}-e-{id}" aria-invalid="true" value="{value}">
    </div>
    <!-- radio / checkboxes: <fieldset><legend>{label}</legend>…</fieldset> -->
    <button type="submit">{submitLabel}</button>
  </form>
</div>
```

- Every text and value is encoded for its context through `HtmlWriter` (FR-009, SC-008).
- Input types: `text`, `textarea`, `email`, `tel` (phone), `number` (with `step="1"` for
  whole numbers, else `step="any"`), `date`, `select`, radio and checkbox inputs.
- `consent` renders a checkbox whose label is the statement, with an optional link to
  `linkPageId` (opens in the same tab).
- The stylesheet hides `.qb-form__hp` off-screen and, for `.qb-form__sent:target`, shows the
  panel and hides the following form; otherwise the panel is hidden.

### 2.2 Page shell

- One inline `FormScript` after the first form (FS-10); its hash joins the page's script
  hashes.
- CSP additions on pages with a form (FS-11): `form-action 'self' {external redirect
  origins}` and `connect-src 'self'`. Pages without forms are unchanged.

### 2.3 Texts

`PublicTexts` gains, per site language: submit, success, error, rate limit, summary heading,
required marker, trap label, and one message per value code (§4) with `{label}` and bound
placeholders (`{max}`, `{min}`, …).

### 2.4 Responses without scripts

| Outcome | Response |
|---|---|
| `ok` (and the silent spam and duplicate cases) | `303`, `Location`: the redirect target's live address (with base path) or URL; else `{_path}#qb-sent-{formId}`. `Cache-Control: no-store` |
| `invalid` | `422`, the page at `_path` rendered with the form's values and errors, `Cache-Control: no-store` |
| `limited` | `429`, `Retry-After` (seconds to the end of the window), the page with the rate-limit message and values |
| `gone`, `error` | `503`, the page with the form's error message and values; if `_path` no longer resolves, the site's not-found page with `404` |

Re-rendered pages are produced by `IFormResponsePage` (Rendering.Contracts) and never enter
the page cache.

## 3. JSON (form script only)

```json
{ "status": "ok", "redirect": "https://example.com/thanks" }
{ "status": "ok" }
{ "status": "invalid", "errors": { "a1b2c3d4e5f6": "required", "0f1e2d3c4b5a": "email" } }
{ "status": "limited", "retryAfter": 42 }
{ "status": "gone" }
{ "status": "error" }
```

HTTP status codes match §2.4 (`200` for ok). `Cache-Control: no-store`. The silent outcomes
answer exactly as `ok` (FR-016, FR-018).

## 4. Value codes (FS-08)

| Code | Field types | Bound |
|---|---|---|
| `required` | all | |
| `too_short`, `too_long` | text-like | `{min}`, `{max}` |
| `email` | email | |
| `phone` | phone | |
| `number`, `whole` | number | |
| `too_small`, `too_large` | number | `{min}`, `{max}` |
| `date`, `too_early`, `too_late` | date | `{min}`, `{max}` |
| `choice` | select, radio, checkboxes | |
| `too_few`, `too_many` | checkboxes | `{min}`, `{max}` |

A field's `message`, when set, replaces every code's text for that field.

## 5. Accessibility checks (SC-008)

- With and without scripts: every input labelled; required stated; errors announced; the
  summary focused and linking to fields; the trap field never reachable or announced; the
  success panel announced.
- axe in the golden HTML test and the admin preview; manual screen-reader pass in quickstart
  §7.
