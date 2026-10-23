// Every refusal the editor can meet, as a translation key (011 FR-042, T065a).
//
// Server error codes (data-model §6 and 010's), document rules (DocumentProblem.rule), the
// editor's own refused operations and save states all come through here, so each has one
// clear message and editorErrors.spec.ts can prove none is missing from the lookup.

import type { OpRefusalCode } from './ops'

/** The editor's own refusals: operations the editor would not apply (ops/types.ts). */
export const OP_REFUSALS: readonly (OpRefusalCode | 'needs_section' | 'clipboard_empty')[] = [
  'nesting_not_allowed',
  'too_many_nodes',
  'too_deep',
  'too_large',
  'not_found',
  'invalid_value',
  'required',
  'rich_text_too_long',
  'rich_text_too_many_images',
  'rich_text_too_many_tables',
  'table_not_rectangular',
  'table_header_misplaced',
  'cannot_change_root',
  'into_itself',
  'read_only',
  'no_collections',
  'no_menus',
  'list_slot',
  'needs_section',
  'clipboard_empty',
]

/** Rules a saved document can break (docs/contracts/page-schema.md §3). */
export const DOCUMENT_RULES: readonly string[] = [
  'malformed_json',
  'duplicate_property',
  'schema_violation',
  'unknown_type',
  'props_invalid',
  'children_not_allowed',
  'root_misplaced',
  'nesting_not_allowed',
  'duplicate_node_id',
  'too_many_nodes',
  'too_deep',
  'too_large',
  'component_ref_not_allowed',
  'unknown_component',
  'unsupported_schema_version',
  'rich_text_too_long',
  'rich_text_too_many_images',
  'rich_text_too_many_tables',
  'table_not_rectangular',
  'table_header_misplaced',
  // 016: bindings and collection lists (page-schema.md §3, rules 13–20).
  'binding_version_required',
  'unknown_collection',
  'too_many_lists',
  'list_too_deep',
  'list_children_invalid',
  'unknown_scope',
  'page_scope_not_allowed',
  'duplicate_page_param',
  // 016 FR-027: rule 21, across a page and the components it shows (server only).
  'page_param_taken',
  // 017: search metadata belongs to pages (page-schema.md §3, rule 22).
  'seo_in_component',
  // 018: forms (page-schema.md §3, rules 26–27).
  'too_many_forms',
  'form_in_list',
  'duplicate_field_id',
]

/** Server error codes the editor and the design screen can receive. */
export const SERVER_CODES: readonly string[] = [
  'pages.out_of_date',
  'pages.admin_outdated',
  'pages.not_found',
  'pages.component_not_found',
  'pages.invalid_document',
  'pages.unsupported_schema_version',
  'pages.document_too_large',
  'pages.unknown_component',
  'pages.invalid_token_id',
  'sites.not_found',
  'identity.forbidden',
  'editor.out_of_date',
  'editor.invalid_tokens',
  'editor.duplicate_token_id',
  'editor.duplicate_token_name',
  'editor.content_widths_order',
  'editor.token_required',
  'editor.tokens_too_large',
  'editor.unsupported_schema_version',
]

const key = (group: string, code: string): string => `editor.errors.${group}.${code.replace(/\./g, '_')}`

/** The message key for an operation the editor refused. */
export const refusalKey = (code: string): string => key('ops', code)

/** The message key for a broken document rule. */
export const ruleKey = (rule: string): string => (DOCUMENT_RULES.includes(rule) ? key('rules', rule) : key('rules', 'schema_violation'))

/** The message key for a server error code, falling back to a general one. */
export const serverKey = (code: string | null | undefined): string =>
  code && SERVER_CODES.includes(code) ? key('server', code) : 'editor.errors.server.unexpected'
