# Content contracts

Where versioned content schemas live (constitution Principle VI): the page tree schema,
the collection schema and the binding expression format. Each is shared by the editor and
the renderer, and both validate against the same definition.

| Contract | Document | Definition | Since |
|---|---|---|---|
| Page schema (pages and shared components) | [page-schema.md](page-schema.md) | [page-schema/v1.schema.json](page-schema/v1.schema.json), with golden examples under [page-schema/examples/](page-schema/examples/) | 010 |

The collection schema and the binding expression format arrive with the CMS features.

Changing anything here needs the contract document, the schema file and, for a breaking
change, the forward migration of stored data, all in the same pull request.
