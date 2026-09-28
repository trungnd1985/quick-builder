# Content contracts

Where versioned content schemas live (constitution Principle VI): the page tree schema,
the collection schema and the binding expression format. Each is shared by the editor and
the renderer, and both validate against the same definition.

| Contract | Document | Definition | Since |
|---|---|---|---|
| Page schema (pages and shared components) | [page-schema.md](page-schema.md) | [page-schema/v1.schema.json](page-schema/v1.schema.json), with golden examples under [page-schema/examples/](page-schema/examples/) | 010; content blocks since 011 |
| Design tokens (a site's colours, typography and spacing) | [design-tokens.md](design-tokens.md) | [design-tokens/v1.schema.json](design-tokens/v1.schema.json), with golden examples under [design-tokens/examples/](design-tokens/examples/) | 011 |

The collection schema and the binding expression format arrive with the CMS features.

Changing anything here needs the contract document, the schema file and, for a breaking
change, the forward migration of stored data, all in the same pull request.
