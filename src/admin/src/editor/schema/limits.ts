// The page schema contract's document limits (docs/contracts/page-schema.md §3 and §4).
//
// The schema file can't state these, so they are constants on both sides. They mirror
// QuickBuilder.Pages.Contracts.PageSchema exactly; PageSchemaLimitsParityTests reads this
// file and fails the build if a value drifts.

export const MAX_DOCUMENT_BYTES = 1_048_576
export const MAX_NODES = 5000
export const MAX_NODE_DEPTH = 32
export const MAX_RICH_TEXT_CHARS = 20_000
export const MAX_RICH_TEXT_IMAGES = 50
export const MAX_RICH_TEXT_TABLES = 20
export const MAX_LISTS_PER_DOCUMENT = 20
export const MAX_LIST_DEPTH = 2
export const CURRENT_BINDING_VERSION = 1

/** The schema versions this editor can read and write. */
export const SUPPORTED_SCHEMA_VERSIONS: ReadonlySet<number> = new Set([1])
export const CURRENT_SCHEMA_VERSION = 1
