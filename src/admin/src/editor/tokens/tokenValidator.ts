// The admin's check of a design token set (docs/contracts/design-tokens.md §2), so the Design
// screen can point at a problem before saving. The same order and the same answers as the
// server's TokenSetValidator; designTokens.spec.ts runs both sides' golden files.

import tokenSchema from '@contracts/design-tokens/v1.schema.json'
import { isObject, SchemaSubsetValidator, type JsonObject } from '../schema/subsetValidator'

const schema = SchemaSubsetValidator.load(tokenSchema as unknown as JsonObject)

export type TokenValidation = { valid: true } | { valid: false; code: string; pointer: string; message: string }

const MAX_BYTES = 65_536
const LISTS: [string, string[]][] = [
  ['/colors', ['colors']],
  ['/typography/sizes', ['typography', 'sizes']],
  ['/spacing', ['spacing']],
  // Optional (066): a set without radii uses the default set's, so an absent list is skipped.
  ['/radii', ['radii']],
]

const fail = (code: string, pointer: string, message = ''): TokenValidation => ({ valid: false, code, pointer, message })

const at = (value: unknown, path: string[]): unknown => path.reduce<unknown>((node, key) => (isObject(node) ? node[key] : undefined), value)

export function validateTokenSet(set: unknown): TokenValidation {
  if (new TextEncoder().encode(JSON.stringify(set)).length > MAX_BYTES) return fail('editor.tokens_too_large', '')

  if (isObject(set) && typeof set.schemaVersion === 'number' && Number.isInteger(set.schemaVersion) && set.schemaVersion !== 1) {
    return fail('editor.unsupported_schema_version', '/schemaVersion')
  }

  for (const [pointer, path] of LISTS) {
    const list = at(set, path)
    if (Array.isArray(list) && list.length === 0) return fail('editor.token_required', pointer)
  }

  const failure = schema.validate(set as never)
  if (failure) return fail('editor.invalid_tokens', failure.location, failure.message)

  const ids = new Set<string>()
  for (const [pointer, path] of LISTS) {
    const list = at(set, path) as { id: string }[] | undefined
    if (!list) continue
    for (let i = 0; i < list.length; i++) {
      if (ids.has(list[i].id)) return fail('editor.duplicate_token_id', `${pointer}/${i}/id`)
      ids.add(list[i].id)
    }
  }

  for (const [pointer, path] of LISTS) {
    const names = new Set<string>()
    const list = at(set, path) as { name: string }[] | undefined
    if (!list) continue
    for (let i = 0; i < list.length; i++) {
      const name = list[i].name.toLocaleLowerCase('en')
      if (names.has(name)) return fail('editor.duplicate_token_name', `${pointer}/${i}/name`)
      names.add(name)
    }
  }

  const widths = isObject(set) ? set.contentWidths : undefined
  if (isObject(widths) && !((widths.narrow as number) <= (widths.normal as number) && (widths.normal as number) <= (widths.wide as number))) {
    return fail('editor.content_widths_order', '/contentWidths')
  }

  return { valid: true }
}
