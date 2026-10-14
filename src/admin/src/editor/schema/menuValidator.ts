// The admin's validator of menu documents (024 research NV-03; docs/contracts/menu.md §4).
//
// A port of the Pages module's MenuDocumentValidator and MenuRules, run before every save so
// the menu editor never sends a menu the server is certain to refuse. It runs the same checks
// in the same order and reports the same rule and pointer for every golden file
// (menuSchema.spec.ts against MenuSchemaGoldenTests).
//
// Not ported: malformed JSON and duplicate property names, which the admin cannot produce from
// objects, and rule 8 (new targets belong to the site), which needs the server.

import rawSchema from '@contracts/menu/v1.schema.json'
import { isObject, SchemaSubsetValidator, type Json, type JsonObject } from './subsetValidator'
import { MAX_MENU_DOCUMENT_BYTES, MAX_MENU_ENTRIES, MAX_MENU_LEVELS, SUPPORTED_MENU_VERSIONS } from './limits'
import { utf8Size } from './documentValidator'

export type MenuTarget =
  | { type: 'page'; pageId: string; anchor?: string }
  | { type: 'item'; collectionId: string; itemId: string }
  | { type: 'url'; href: string }
  | { type: 'anchor'; anchor: string }
  | { type: 'none' }

export type MenuTargetType = MenuTarget['type']

/** One entry of a menu (menu.md §2). */
export interface MenuEntry {
  id: string
  label?: string
  target: MenuTarget
  newTab?: boolean
  hidden?: boolean
  children: MenuEntry[]
}

/** A menu document (menu.md §1). */
export interface MenuDocument {
  schemaVersion: number
  entries: MenuEntry[]
}

export type MenuValidation =
  | { valid: true }
  | { valid: false; rule: string; pointer: string; message: string }

export const menuSchema = SchemaSubsetValidator.load(rawSchema as unknown as JsonObject)

const invalid = (rule: string, pointer: string, message: string): MenuValidation => ({ valid: false, rule, pointer, message })

/** Target types that need their own label (rule 6). */
export const LABEL_REQUIRED: ReadonlySet<MenuTargetType> = new Set(['url', 'anchor', 'none'])

/** Target types that may open in a new tab (rule 7). */
export const NEW_TAB_ALLOWED: ReadonlySet<MenuTargetType> = new Set(['page', 'item', 'url'])

/** An anchor name as the contract allows it (menu.md §3). */
export const ANCHOR_PATTERN = /^[A-Za-z][A-Za-z0-9_-]{0,99}$/

/** The anchor a member typed, without a leading `#` (FR-007a). */
export const cleanAnchor = (typed: string): string => typed.trim().replace(/^#/, '')

interface Walked {
  entry: JsonObject
  pointer: string
  level: number
}

/** Every entry with its pointer and level (1 at the top), depth first, as MenuRules.Walk. */
export function walkEntries(entries: Json, pointer = '/entries', level = 1): Walked[] {
  const found: Walked[] = []
  if (!Array.isArray(entries)) return found
  entries.forEach((entry, index) => {
    if (!isObject(entry)) return
    const here = `${pointer}/${index}`
    found.push({ entry, pointer: here, level })
    found.push(...walkEntries(entry.children, `${here}/children`, level + 1))
  })
  return found
}

const targetType = (entry: JsonObject): string | null =>
  isObject(entry.target) && typeof entry.target.type === 'string' ? entry.target.type : null

export function validateMenu(document: unknown): MenuValidation {
  if (utf8Size(document) > MAX_MENU_DOCUMENT_BYTES) {
    return invalid('document_too_large', '', `A menu may be at most ${MAX_MENU_DOCUMENT_BYTES} bytes.`)
  }

  if (isObject(document) && typeof document.schemaVersion === 'number' && Number.isInteger(document.schemaVersion)) {
    if (!SUPPORTED_MENU_VERSIONS.has(document.schemaVersion)) {
      return invalid('unsupported_schema_version', '/schemaVersion', 'This installation does not support that menu version.')
    }
  }

  const failure = menuSchema.validate(document as Json)
  if (failure) {
    const pointer = failure.keyword === 'discriminator' ? `${failure.location}/type` : failure.location
    const rule = failure.location.startsWith('/entries/') ? 'entry_invalid' : 'schema_violation'
    return invalid(rule, pointer, `The menu is not valid: ${failure.message}`)
  }

  return checkMenuRules((document as JsonObject).entries)
}

/** Rules 3–7 over entries that passed the schema, each its own pass, in the contract's order. */
function checkMenuRules(entries: Json): MenuValidation {
  const all = walkEntries(entries)

  const ids = new Set<string>()
  for (const { entry, pointer } of all) {
    const id = entry.id as string
    if (ids.has(id)) return invalid('duplicate_entry_id', `${pointer}/id`, 'Another entry of this menu has the same id.')
    ids.add(id)
  }

  if (all.length > MAX_MENU_ENTRIES) {
    return invalid('too_many_entries', all[MAX_MENU_ENTRIES].pointer, `A menu may hold at most ${MAX_MENU_ENTRIES} entries.`)
  }

  const tooDeep = all.find((e) => e.level > MAX_MENU_LEVELS)
  if (tooDeep) return invalid('too_deep', tooDeep.pointer, `Entries may be nested at most ${MAX_MENU_LEVELS} levels deep.`)

  for (const { entry, pointer } of all) {
    const type = targetType(entry)
    const label = typeof entry.label === 'string' ? entry.label : ''
    if (type !== null && LABEL_REQUIRED.has(type as MenuTargetType) && label.trim() === '') {
      return invalid('label_required', pointer, 'This kind of entry needs a label.')
    }
  }

  for (const { entry, pointer } of all) {
    const type = targetType(entry)
    if ((type === 'anchor' || type === 'none') && entry.newTab === true) {
      return invalid('new_tab_not_allowed', `${pointer}/newTab`, 'Only entries that link to a page, an item or an address can open in a new tab.')
    }
  }

  return { valid: true }
}

/**
 * Every entry's own problem, by entry id, for marking the tree before saving (FR-011): the
 * schema and rules 6–7 per entry, plus the levels and the entry count. The server still names
 * only the first failure.
 */
export function entryProblems(document: MenuDocument): Map<string, string> {
  const problems = new Map<string, string>()
  const all = walkEntries(document.entries as unknown as Json)
  all.forEach(({ entry, level }, index) => {
    const id = String(entry.id)
    // The entry alone: its children are checked as entries of their own.
    const own = menuSchema.validateDefinition('entry', { ...entry, children: [] } as Json)
    const type = targetType(entry)
    const label = typeof entry.label === 'string' ? entry.label : ''
    const problem = own
      ? 'entry_invalid'
      : level > MAX_MENU_LEVELS
        ? 'too_deep'
        : index >= MAX_MENU_ENTRIES
          ? 'too_many_entries'
          : type !== null && LABEL_REQUIRED.has(type as MenuTargetType) && label.trim() === ''
            ? 'label_required'
            : (type === 'anchor' || type === 'none') && entry.newTab === true
              ? 'new_tab_not_allowed'
              : null
    if (problem && !problems.has(id)) problems.set(id, problem)
  })
  return problems
}

/** A new, empty menu document. */
export const emptyMenu = (): MenuDocument => ({ schemaVersion: 1, entries: [] })
