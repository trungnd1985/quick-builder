# Contract: Public Media Addresses and Responsive Image Markup

**Feature**: 013 | **Research**: MV-08, MV-11, MV-12, MV-15

What visitors and browsers can rely on. This extends 003's public serving contract
(`/_media`, research M-11) and 012's rendering contract (`docs/contracts/page-schema.md`
§9).

---

## 1. Addresses under `/_media`

### 1.1 Grammar

```text
file     = id [ "-v" version [ "-w" width ] ] "." ext
id       = 32 lower-case hex digits
version  = 1–5 digits, no leading zero, 1 … 99999
width    = one of Media:Variants:Widths (default 320 640 960 1280 1920 2560)
ext      = the content version's stored extension (1–8 of [a-z0-9])
```

The only form that is valid for a request, the **canonical form**, is:

| Resource | Canonical form |
|---|---|
| Original, content version 1 | `{id}.{ext}` (no `-v1`) |
| Original, content version n ≥ 2 | `{id}-v{n}.{ext}` |
| Variant of content version n, any n | `{id}-v{n}-w{width}.{ext}` |

### 1.2 Resolution

Every step that fails gives `404 media.not_found` with the same body, whatever the reason
(003 FR-014):

1. The path must match the grammar and be canonical.
2. A file with that id must exist on **the site resolved from the Host header**.
3. The version must equal the file's current `content_version`, and the extension must
   equal its current extension.
4. For a variant:
   - the width must be configured and smaller than the file's width;
   - the file must be eligible (data-model §3.1).
5. The bytes must exist in storage. For a variant, a miss triggers production (MV-12).

### 1.3 Response headers

These are identical for originals and variants, as in 003 M-11:
- `Content-Type` is the validated type;
- `X-Content-Type-Options: nosniff`;
- for images, `Content-Security-Policy: default-src 'none'; img-src 'self';
  style-src 'unsafe-inline'; sandbox`. PDFs keep 003's rule (no CSP, because Chromium will
  not render a sandboxed PDF);
- `Cache-Control: public, max-age=31536000, immutable`;
- `Accept-Ranges: bytes`;
- a strong `ETag` that names exactly what the address serves:
  - `"{id}"` for version 1 originals (unchanged from 003);
  - `"{id}-v{n}"` for later originals;
  - `"{id}-v{n}-w{width}"` for variants.
- `Last-Modified`: `replaced_at_utc` when set, otherwise `uploaded_at_utc`.

**The one exception**: when a variant cannot be produced (MV-12), the original's bytes are
sent with `Cache-Control: no-store`.

### 1.4 Guarantees

- An address never serves content other than what it was issued for (FR-018). A
  replacement changes the address.
- After a delete or replacement commits, the old addresses are not found on the next
  request, on every instance (SC-009).
- Variants are upright, in sRGB, carry no embedded metadata (no EXIF, GPS, XMP or ICC), are
  never wider than the original, and keep alpha (FR-023).
- Every 003 address of a file still at version 1 keeps working (FR-045).

---

## 2. Image markup on published pages (addition to page-schema.md §9)

For an Image block whose file resolves (012 FR-027, unchanged otherwise):

```html
<figure class="qb-image qb-image--{full|natural}">
  [<a href="…">]
  <img src="{src}"
       srcset="{v1} 320w, {v2} 640w, …[, {original} {W}w]"
       sizes="{sizes}"
       width="{W}" height="{H}"
       alt="{alt}" loading="lazy" decoding="async">
  [</a>]
</figure>
```

| Attribute | Rule |
|---|---|
| `width`, `height` | The file's displayed dimensions. Omitted while unknown. |
| `srcset` | Every variant in `PublicMediaFile.Variants`, as `{basePath}{path} {w}w`. The original `{W}w` is added **only if** `W` ≤ the largest configured width. Omitted entirely when `Variants` is empty. |
| `sizes` | Written only with `srcset`. Let `C` be the enclosing Section's content width (Narrow 720, Normal 1,200, Wide 1,440, from design tokens). `full`: `(max-width: {C}px) 100vw, {C}px`. `natural`: the same with `min(C, W)`. |
| `src` | The largest variant with width ≤ 1,280. If there is none, the original. |
| `alt`, `loading`, `decoding` | Unchanged from 012. |

- **Order**: the attribute order is fixed as above, and golden files pin it.
- **Encoding**: every value is written through `HtmlWriter`: paths with `SitePath` and
  `sizes` as attribute text. No value comes from page content except `alt` (012 FR-025).
- **JavaScript**: none. The 100 KB budget is unaffected (FR-028 of 012).
- **Canvas parity**: the editor's `ImageBlock.vue` writes the same structure and
  attributes, with admin variant addresses (MV-16). `CanvasMarkupParity.spec.ts` ignores
  the address values in `src` and `srcset`, and compares everything else.
