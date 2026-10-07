// The collection schema as the builder sees it (014 research CS-04).
//
// The builder imports docs/contracts/collection-schema/v1.schema.json itself — the file the
// server embeds — and checks a proposal in the server's order: the schema version, the
// schema, then the rules. It points at a problem before anything is saved; the server checks
// again regardless. Malformed JSON and duplicate property names cannot come from this app,
// so those checks are the server's alone.

import rawSchema from '@contracts/collection-schema/v1.schema.json'
import { isObject, SchemaSubsetValidator, type JsonObject } from '../../editor/schema/subsetValidator'
import type { FieldType, SchemaDocument, SchemaField } from '../../api/cms'
import { checkRules, normalise, type DocumentKind } from './collectionSchemaRules'

export const collectionSchema = SchemaSubsetValidator.load(rawSchema as unknown as JsonObject)

export const FIELD_TYPES: readonly FieldType[] = [
  'text', 'richText', 'number', 'boolean', 'date', 'image', 'file', 'option', 'reference', 'multiReference',
]

export const LIMITS = {
  fields: 100,
  options: 200,
  name: 100,
  key: 64,
  help: 300,
  description: 500,
  textLength: 10_000,
  decimals: 6,
} as const

export type SchemaValidation =
  | { valid: true; document: SchemaDocument }
  | { valid: false; code: string; rule: string; pointer: string }

export function validateSchema(candidate: unknown, kind: DocumentKind): SchemaValidation {
  if (isObject(candidate) && typeof candidate.schemaVersion === 'number' && Number.isInteger(candidate.schemaVersion) && candidate.schemaVersion !== 1) {
    return { valid: false, code: 'cms.unsupported_schema_version', rule: 'unsupported_schema_version', pointer: '/schemaVersion' }
  }

  const failure = collectionSchema.validate(candidate as JsonObject)
  if (failure) {
    return { valid: false, code: 'cms.invalid_schema', rule: 'schema_violation', pointer: failure.location }
  }

  const document = kind === 'proposal' ? normalise(candidate as unknown as SchemaDocument) : (candidate as unknown as SchemaDocument)
  const broken = checkRules(document, kind)
  return broken ? { valid: false, code: 'cms.invalid_schema', ...broken } : { valid: true, document }
}

/** The index of the field a pointer such as `/fields/3/key` points into, or null. */
export function fieldIndexOf(pointer: string): number | null {
  const match = /^\/fields\/(\d+)/.exec(pointer)
  return match ? Number(match[1]) : null
}

/** A key suggested from a display name: the slug grammar of field keys (contract §2). */
export function suggestKey(name: string): string {
  const key = name
    .normalize('NFD')
    .replace(/\p{M}+/gu, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .replace(/^[^a-z]+/, '')
    .slice(0, LIMITS.key)
    .replace(/-+$/, '')
  return key
}

/** A new field of a type, with the settings the builder offers by default (contract §2). */
export function newField(type: FieldType, id: string, name: string): SchemaField {
  const base: SchemaField = { id, key: suggestKey(name), name, help: '', required: false, type }
  switch (type) {
    case 'text':
      return { ...base, multiline: false, maxLength: 256 }
    case 'number':
      return { ...base, decimals: 0 }
    case 'date':
      return { ...base, withTime: false }
    case 'file':
      return { ...base, accept: 'any' }
    case 'option':
      return { ...base, options: [] }
    case 'reference':
    case 'multiReference':
      return { ...base, target: '' }
    default:
      return base
  }
}

/** The field with only the settings its type has, so a type change never sends stray ones. */
export function withType(field: SchemaField, type: FieldType, id = field.id): SchemaField {
  const fresh = newField(type, id, field.name)
  return { ...fresh, key: field.key, help: field.help, required: field.required, ...(type === 'option' && field.options ? { options: field.options } : {}) }
}

/** Allocates temporary ids (`~1`, `~2`, …) that are unique within one proposal (contract §6). */
export class TemporaryIds {
  private next = 1

  constructor(document?: SchemaDocument) {
    for (const field of document?.fields ?? []) {
      this.see(field.id)
      for (const option of field.options ?? []) this.see(option.id)
    }
  }

  take(): string {
    return `~${this.next++}`
  }

  private see(id: string): void {
    if (id.startsWith('~')) this.next = Math.max(this.next, Number(id.slice(1)) + 1)
  }
}
