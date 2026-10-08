# Contract Delta: Admin API

**Feature**: 035 | **Decisions**: research RT-09, RT-10, RT-13

No endpoint is added or removed. Bodies keep their shapes. The changes are one request header, two error codes and two optional properties.

## 1. Request header

| Header | Sent by | Meaning |
|---|---|---|
| `X-QB-Rich-Text: 2` | every admin request (`api/request.ts`) | the client understands rich text with the 035 formats |

## 2. New errors

| Code | Status | When | Body extensions |
|---|---|---|---|
| `admin_outdated` | 409 | A page or component draft save (pages API), or an item draft save or duplicate (items API), arrives without `X-QB-Rich-Text: 2` (or with a lower value) while the stored draft holds any 035 format (RT-13). | none |
| `pages.image_alt_missing` | 422 | Publishing a page or component (manual, scheduled or rollback) whose document holds a rich text image with neither alternative text nor `decorative` (RT-10). | `problems`: `[{ "pointer": "/root/children/0/children/2/props/content/content/3" }]` |

A scheduled page publish that meets `pages.image_alt_missing` fails like any other refused publish (012 `schedule_failed`).

## 3. Item report problems (015 `items-api.yaml`, `Report`)

New problem codes:
- `format_not_allowed`: with `format` (`headings`, `images` or `tables`);
- `image_alt_missing`: with `path`, a JSON pointer inside the field's value, for example `/content/4`.

Two optional properties are added to problem objects: `format` and `path`. Both are additive, since `Report.problems` items don't forbid other properties. They are stored and returned like 015's other value problems that are kept: they set attention and block publishing, as described in 015 data-model §2.

## 4. Collection schema bodies (014 `collections-api.yaml`)

`richText` fields accept the optional `formats` setting ([rich-text.md §6](./rich-text.md)). The change preview counts the items that need attention after `formats` is narrowed, under the existing "needs attention" count. No new property is added to the preview.

## 5. Registries to update in the same PR

- `docs/contracts/page-schema.md`: rules 10–12 and the rendering allow-list. It is the source; `specs/011-editor-core/contracts/page-schema-blocks.md` and `specs/012-publishing-rendering/contracts/page-schema-rendering.md` link to it, and each gains a one-line "extended by 035" note.
- `specs/011-editor-core/contracts/editor-api.yaml`: `admin_outdated` on draft save.
- `specs/012-publishing-rendering/contracts/publishing-api.yaml`: `pages.image_alt_missing` on publish.
- `specs/015-cms-items/contracts/items-api.yaml`: `admin_outdated`; the `Report` problems' `format` and `path`.
- `specs/015-cms-items/data-model.md` §2: the new problem codes, marked "added by 035".
