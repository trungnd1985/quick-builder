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
  | 'cannot_change_root'
  | 'into_itself'
  | 'read_only'

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

export const refuse = (code: OpRefusalCode, extra: Omit<OpRefusal, 'code'> = {}): OpResult => ({ ok: false, refusal: { code, ...extra } })

export const done = (root: ContentNode, selection: string | null): OpResult => ({ ok: true, root, selection })
