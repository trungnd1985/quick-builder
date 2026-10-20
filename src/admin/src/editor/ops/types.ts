import type { ContentNode } from '../schema/schema'

/** Why an operation was refused. The editor shows each through editorErrors.ts (FR-042). */
export type OpRefusalCode =
  | 'nesting_not_allowed'
  | 'too_many_nodes'
  | 'too_deep'
  | 'too_large'
  | 'not_found'
  | 'invalid_value'
  | 'required'
  | 'rich_text_too_long'
  | 'rich_text_too_many_images'
  | 'rich_text_too_many_tables'
  | 'table_not_rectangular'
  | 'table_header_misplaced'
  | 'cannot_change_root'
  | 'into_itself'
  | 'read_only'
  /** 016: a Collection list needs a collection, and the site has none. */
  | 'no_collections'
  /** 024: a Navigation block shows a menu, and the site has none. */
  | 'no_menus'
  /** 016 rule 20: a list's card and empty state stay with their list, one of each, in order. */
  | 'list_slot'

export interface OpRefusal {
  code: OpRefusalCode
  /** For `invalid_value`: the schema keyword that failed, e.g. `maxLength`. */
  keyword?: string
  /** For `invalid_value`: where inside the value it failed. */
  pointer?: string
}

/** A new root and the block to select, or a refusal that changed nothing. */
export type OpResult =
  | { ok: true; root: ContentNode; selection: string | null }
  | { ok: false; refusal: OpRefusal }

/** A list's card or empty state: made and removed only with its list (016 rule 20). */
export const isListSlot = (type: string): boolean => type === 'list-card' || type === 'list-empty'

export const refuse = (code: OpRefusalCode, extra: Omit<OpRefusal, 'code'> = {}): OpResult => ({ ok: false, refusal: { code, ...extra } })

export const done = (root: ContentNode, selection: string | null): OpResult => ({ ok: true, root, selection })
