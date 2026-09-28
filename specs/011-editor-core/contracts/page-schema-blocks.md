# Contract change: Page Schema v1, content blocks

**Planning copy.** Implementation applies this to `docs/contracts/page-schema.md` and
`docs/contracts/page-schema/v1.schema.json` in the same PR, with the golden examples
(constitution, Development Workflow). **Additive within version 1** (contract §6): every
document valid before stays valid, and no stored document needs a migration.

Research: ED-04 … ED-08. Data model: [data-model.md §1](../data-model.md).

---

## Changes to `page-schema.md`

### §2 Node types: replace the table

| Type | Props | Children | Allowed in |
|---|---|---|---|
| `root` | none | `rootChild` | the envelope's `root` only |
| `component-ref` | `componentId` (UUID, same site) | none | page documents, under `root` only |
| `section` | `background`, `paddingY`, `width`, `layerLabel` | `contentChild` | under `root` |
| `container` | `direction`, `gap`, `padding`, `align`, `background`, `stackOnMobile`, `layerLabel` | `contentChild` | in `section` or `container` |
| `heading` | `text`\*, `level`, `align`, `color`, `layerLabel` | none | in `section` or `container` |
| `text` | `text`\*, `align`, `color`, `size`, `layerLabel` | none | 〃 |
| `rich-text` | `content`\*, `align`, `color`, `layerLabel` | none | 〃 |
| `image` | `media`, `alt`, `decorative`, `width`, `link`, `layerLabel` | none | 〃 |
| `button` | `label`\*, `link`, `style`, `align`, `newTab`, `layerLabel` | none | 〃 |
| `spacer` | `height`, `layerLabel` | none | 〃 |
| `divider` | `color`, `thickness`, `spacing`, `layerLabel` | none | 〃 |
| `embed` | `url`, `title`, `aspectRatio`, `layerLabel` | none | 〃 |

\* required. Values, limits and defaults are in the schema, which is the single definition.
`layerLabel` is an editor aid and is never rendered.

**Adding a block type**: add a `$defs` entry and add it to `rootChild` or `contentChild`,
plus golden examples, a TypeScript `definition.ts` and canvas component (ED-08), and later
the renderer (#12). No other type changes.

### §2a Shared values: new

- **Token reference** `{ "token": id }`, used for colours, spacing and text sizes. The id
  refers to the site's design token set (contracts/design-tokens.md). It **may name a token
  that no longer exists**; consumers then use the property's `default`.
- **Link**: `{ "type": "page", "pageId": uuid }` or `{ "type": "url", "href": … }`.
  - Schemes are `https`, `http`, `mailto` and `tel` only.
  - A `pageId` may name a page that no longer exists. The renderer (#12) decides how that
    is shown.
- **Media**: `{ "mediaId": uuid }`, a file of the site's media library. It may no longer
  exist.
- **Rich text**: a ProseMirror `doc` limited to paragraphs, bulleted and ordered lists,
  hard breaks, and the marks bold, italic and link. There is never HTML.

### §3 Rules beyond the schema: add

9. A Rich Text block holds at most **20,000** characters of text (`rich_text_too_long`).

In the validation order, step 5 (schema) gains `nesting_not_allowed`: a type defined in
this schema, placed in a children list that doesn't admit it. Step 6 gains rule 9.
Failures anywhere under a node's `props` are reported as `props_invalid`.

### §7 The schema file: the subset gains two keywords

- **Validation**: `minItems`.
- **Annotations**: `default`. It is never validated. Consumers use it as the value of a
  missing property.
- **Patterns** are limited to syntax that means the same in ECMAScript and .NET
  (`RegexOptions.ECMAScript`): no lookbehind, no named groups, no Unicode classes.

### §8 Golden files: add

| File | Expected |
|---|---|
| `valid/every-block.json` | passes: one of each type, nested Section › Container › Container › leaves |
| `valid/tokens-missing.json` | passes: token references to ids that exist in no token set |
| `valid/rich-text-lists.json` | passes: nested lists, all marks, and both link kinds |
| `valid/component-page.json` | passes as a component document: Sections only |
| `invalid/nesting_not_allowed--section-in-section.json` | at `/root/children/0/children/0/type` |
| `invalid/nesting_not_allowed--heading-at-root.json` | at `/root/children/0/type` |
| `invalid/children_not_allowed--child-in-heading.json` | at `…/children` |
| `invalid/props_invalid--javascript-link.json` | at `…/props/link/href` |
| `invalid/props_invalid--embed-other-provider.json` | at `…/props/url` |
| `invalid/props_invalid--rich-text-unknown-mark.json` | at `…/props/content/content/0/content/0/marks/0/type` |
| `invalid/props_invalid--heading-level-7.json` | at `…/props/level` |
| `invalid/props_invalid--token-bad-id.json` | at `…/props/background/token` |
| `invalid/props_invalid--missing-required-text.json` | at `…/props` |
| `invalid/rich_text_too_long--20001.json` | at `…/props/content` |

The C# `PageDocumentValidatorTests` and the TypeScript `documentValidator.spec.ts` both run
every file.

### §9 Styling hand-off to rendering: new, informative

For #12:
- **Container**: the canvas wraps the page in an element with `container-type:
  inline-size; container-name: page`. Block CSS uses `@container page (max-width: 767px)`
  for tablet and `(max-width: 480px)` for mobile. The published page must wrap its body the
  same way so that the same block stylesheet applies (ED-09).
- **Tokens**: they become CSS custom properties on that wrapper:
  - `--qb-color-<id>`, `--qb-space-<id>` and `--qb-text-<id>`;
  - `--qb-font-heading` and `--qb-font-body`.
- A missing token uses the property default's custom property. Every id in the built-in
  default set (contracts/design-tokens.md) is always emitted, with its built-in value when
  the site's set lacks it, so a fallback always resolves.

---

## Schema fragment (`v1.schema.json`, new and changed `$defs`)

The envelope, `nodeId` and `component-ref` are unchanged. `root.children.items` changes from
`childNode` to `rootChild`. `childNode` is removed, because it was only ever referenced from
`root`. Every block's `props` has `"additionalProperties": false` and includes
`"layerLabel": { "$ref": "#/$defs/layerLabel" }`. For brevity that line and the four-key
node envelope (`id`, `type`, `props`, `children`, all required) are written once here as
`NODE(type, props, children)`.

```jsonc
{
  "rootChild":    { "oneOf": [ {"$ref":"#/$defs/section"}, {"$ref":"#/$defs/component-ref"} ] },
  "contentChild": { "oneOf": [ {"$ref":"#/$defs/container"}, {"$ref":"#/$defs/heading"},
                               {"$ref":"#/$defs/text"}, {"$ref":"#/$defs/rich-text"},
                               {"$ref":"#/$defs/image"}, {"$ref":"#/$defs/button"},
                               {"$ref":"#/$defs/spacer"}, {"$ref":"#/$defs/divider"},
                               {"$ref":"#/$defs/embed"} ] },
  "noChildren":   { "type": "array", "maxItems": 0 },
  "contentChildren": { "type": "array", "items": { "$ref": "#/$defs/contentChild" } },

  "layerLabel": { "type": "string", "minLength": 1, "maxLength": 60 },
  "tokenId":    { "type": "string", "pattern": "^[a-z0-9-]{1,40}$" },
  "colorRef":   { "type": "object", "required": ["token"], "additionalProperties": false,
                  "properties": { "token": { "$ref": "#/$defs/tokenId" } } },
  "spaceRef":   { /* same shape as colorRef */ },
  "textSizeRef":{ /* same shape as colorRef */ },
  "align":      { "enum": ["start", "center", "end"], "default": "start" },
  "link": { "oneOf": [
    { "type": "object", "required": ["type","pageId"], "additionalProperties": false,
      "properties": { "type": {"const":"page"}, "pageId": {"type":"string","format":"uuid"} } },
    { "type": "object", "required": ["type","href"], "additionalProperties": false,
      "properties": { "type": {"const":"url"},
        "href": { "type":"string", "maxLength": 2048,
                  "pattern": "^(https?://\\S+|mailto:\\S+|tel:[+0-9 ().-]+)$" } } } ] },

  "section":   NODE("section", {
      "background": { "$ref":"#/$defs/colorRef" },
      "paddingY":   { "$ref":"#/$defs/spaceRef", "default": {"token":"space-xl"} },
      "width":      { "enum": ["full","contained"], "default": "contained" } },
    "contentChildren"),
  "container": NODE("container", {
      "direction":  { "enum": ["vertical","horizontal"], "default": "vertical" },
      "gap":        { "$ref":"#/$defs/spaceRef", "default": {"token":"space-md"} },
      "padding":    { "$ref":"#/$defs/spaceRef", "default": {"token":"space-none"} },
      "align":      { "$ref":"#/$defs/align" },
      "background": { "$ref":"#/$defs/colorRef" },
      "stackOnMobile": { "type":"boolean", "default": true } },
    "contentChildren"),
  "heading":   NODE("heading", {                                   /* required: text */
      "text":  { "type":"string", "maxLength": 300, "pattern": "^[^\\r\\n]*$" },
      "level": { "type":"integer", "minimum": 1, "maximum": 6, "default": 2 },
      "align": { "$ref":"#/$defs/align" },
      "color": { "$ref":"#/$defs/colorRef", "default": {"token":"color-text"} } },
    "noChildren"),
  "text":      NODE("text", {                                      /* required: text */
      "text":  { "type":"string", "maxLength": 5000 },
      "align": { "$ref":"#/$defs/align" },
      "color": { "$ref":"#/$defs/colorRef", "default": {"token":"color-text"} },
      "size":  { "$ref":"#/$defs/textSizeRef", "default": {"token":"text-md"} } },
    "noChildren"),
  "rich-text": NODE("rich-text", {                                 /* required: content */
      "content": { "$ref":"#/$defs/rtDoc" },
      "align":   { "$ref":"#/$defs/align" },
      "color":   { "$ref":"#/$defs/colorRef", "default": {"token":"color-text"} } },
    "noChildren"),
  "image":     NODE("image", {
      "media": { "type":"object", "required":["mediaId"], "additionalProperties": false,
                 "properties": { "mediaId": {"type":"string","format":"uuid"} } },
      "alt":        { "type":"string", "maxLength": 300 },
      "decorative": { "type":"boolean", "default": false },
      "width":      { "enum": ["full","natural"], "default": "full" },
      "link":       { "$ref":"#/$defs/link" } },
    "noChildren"),
  "button":    NODE("button", {                                    /* required: label */
      "label":  { "type":"string", "minLength": 1, "maxLength": 100 },
      "link":   { "$ref":"#/$defs/link" },
      "style":  { "enum": ["primary","secondary","outline"], "default": "primary" },
      "align":  { "$ref":"#/$defs/align" },
      "newTab": { "type":"boolean", "default": false } },
    "noChildren"),
  "spacer":    NODE("spacer",  { "height": { "$ref":"#/$defs/spaceRef", "default": {"token":"space-lg"} } }, "noChildren"),
  "divider":   NODE("divider", {
      "color":     { "$ref":"#/$defs/colorRef", "default": {"token":"color-border"} },
      "thickness": { "enum": ["thin","thick"], "default": "thin" },
      "spacing":   { "$ref":"#/$defs/spaceRef", "default": {"token":"space-md"} } },
    "noChildren"),
  "embed":     NODE("embed", {                                     /* nothing required: a new Embed has no url yet */
      "url": { "type":"string", "maxLength": 2048,
               "pattern": "^https://((www\\.)?youtube\\.com/watch\\?v=[A-Za-z0-9_-]{11}(&\\S*)?|youtu\\.be/[A-Za-z0-9_-]{11}(\\?\\S*)?|(www\\.)?vimeo\\.com/[0-9]{1,12}(\\?\\S*)?)$" },
      "title":       { "type":"string", "minLength": 1, "maxLength": 200 },
      "aspectRatio": { "enum": ["16:9","4:3","1:1"], "default": "16:9" } },
    "noChildren"),

  "rtDoc":       { "type":"object", "required":["type","content"], "additionalProperties": false,
                   "properties": { "type": {"const":"doc"},
                     "content": { "type":"array", "maxItems": 500, "items": {"$ref":"#/$defs/rtBlock"} } } },
  "rtBlock":     { "oneOf": [ {"$ref":"#/$defs/rtParagraph"}, {"$ref":"#/$defs/rtBulletList"}, {"$ref":"#/$defs/rtOrderedList"} ] },
  "rtParagraph": { "type":"object", "required":["type"], "additionalProperties": false,
                   "properties": { "type": {"const":"paragraph"},
                     "content": { "type":"array", "maxItems": 1000, "items": {"$ref":"#/$defs/rtInline"} } } },
  "rtBulletList":  { /* type "bulletList", content: rtListItem[] minItems 1 maxItems 200 */ },
  "rtOrderedList": { /* as rtBulletList with type "orderedList" and optional
                        attrs { start: integer 1..9999 } */ },
  "rtListItem":  { "type":"object", "required":["type","content"], "additionalProperties": false,
                   "properties": { "type": {"const":"listItem"},
                     "content": { "type":"array", "minItems": 1, "maxItems": 50, "items": {"$ref":"#/$defs/rtBlock"} } } },
  "rtInline":    { "oneOf": [ {"$ref":"#/$defs/rtText"}, {"$ref":"#/$defs/rtHardBreak"} ] },
  "rtText":      { "type":"object", "required":["type","text"], "additionalProperties": false,
                   "properties": { "type": {"const":"text"},
                     "text":  { "type":"string", "minLength": 1, "maxLength": 20000 },
                     "marks": { "type":"array", "maxItems": 3, "items": {"$ref":"#/$defs/rtMark"} } } },
  "rtHardBreak": { "type":"object", "required":["type"], "additionalProperties": false,
                   "properties": { "type": {"const":"hardBreak"} } },
  "rtMark":      { "oneOf": [
                   { "type":"object", "required":["type"], "additionalProperties": false, "properties": { "type": {"const":"bold"} } },
                   { "type":"object", "required":["type"], "additionalProperties": false, "properties": { "type": {"const":"italic"} } },
                   { "type":"object", "required":["type","attrs"], "additionalProperties": false,
                     "properties": { "type": {"const":"link"},
                       "attrs": { "type":"object", "required":["to"], "additionalProperties": false,
                                  "properties": { "to": {"$ref":"#/$defs/link"} } } } } ] }
}
```

The shipped file is plain JSON with every `NODE(...)` expanded and every `/* */` comment
removed. `PageSchemaContractTests` checks that the §2 table and the file list the same types
and properties.
