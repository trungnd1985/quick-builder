// The collection schema's rules beyond its JSON Schema (docs/contracts/collection-schema.md §5).
//
// A port of the server's CollectionSchemaRules: the same rules in the same order, each over
// every field before the next, with the same rule names and pointers. Both run the contract's
// golden files (collectionSchema.spec.ts, CollectionSchemaGoldenTests), so change them together.

import type { SchemaDocument, SchemaField } from '../../api/cms'

export type DocumentKind = 'stored' | 'proposal'

export interface RuleFailure {
  rule: string
  pointer: string
}

export const RESERVED_KEYS: ReadonlySet<string> = new Set([
  'id', 'slug', 'status', 'created-at', 'updated-at', 'published-at', 'collection', 'site', 'schema-version',
])

/** Simple per-character upper case, as the server's ordinal ignore-case comparison folds. */
export const foldCase = (text: string): string =>
  Array.from(text).map((c) => {
    const upper = c.toUpperCase()
    return upper.length === 1 ? upper : c
  }).join('')

export const isTemporaryId = (id: string): boolean => id.length > 1 && id.startsWith('~')

export const isSingleLineText = (field: SchemaField | undefined): boolean =>
  field?.type === 'text' && field.multiline !== true

/** What the server does to a proposal before its rules: name the items, and require that field. */
export function normalise(document: SchemaDocument): SchemaDocument {
  const titleFieldId = document.titleFieldId ?? document.fields.find(isSingleLineText)?.id
  if (!titleFieldId) return document

  return {
    ...document,
    titleFieldId,
    fields: document.fields.map((f) => (f.id === titleFieldId && isSingleLineText(f) && !f.required ? { ...f, required: true } : f)),
  }
}

export function checkRules(document: SchemaDocument, kind: DocumentKind): RuleFailure | null {
  const fields = document.fields

  const keys = new Set<string>()
  for (const [i, field] of fields.entries()) {
    const folded = foldCase(field.key)
    if (keys.has(folded)) return { rule: 'duplicate_key', pointer: `/fields/${i}/key` }
    keys.add(folded)
  }

  for (const [i, field] of fields.entries()) {
    if (RESERVED_KEYS.has(field.key)) return { rule: 'reserved_key', pointer: `/fields/${i}/key` }
  }

  const ids = new Set<string>()
  for (const [i, field] of fields.entries()) {
    if (ids.has(field.id)) return { rule: 'duplicate_field_id', pointer: `/fields/${i}/id` }
    ids.add(field.id)
  }

  for (const [i, field] of fields.entries()) {
    const labels = new Set<string>()
    for (const [j, option] of (field.options ?? []).entries()) {
      const folded = foldCase(option.label)
      if (labels.has(folded)) return { rule: 'duplicate_option_label', pointer: `/fields/${i}/options/${j}/label` }
      labels.add(folded)
    }
  }

  for (const [i, field] of fields.entries()) {
    const optionIds = new Set<string>()
    for (const [j, option] of (field.options ?? []).entries()) {
      if (optionIds.has(option.id)) return { rule: 'duplicate_option_id', pointer: `/fields/${i}/options/${j}/id` }
      optionIds.add(option.id)
    }
  }

  for (const [i, field] of fields.entries()) {
    if (field.min !== undefined && field.max !== undefined && field.min > field.max) {
      return { rule: 'min_above_max', pointer: `/fields/${i}/min` }
    }
  }

  if (document.titleFieldId !== undefined) {
    const title = fields.find((f) => f.id === document.titleFieldId)
    if (!isSingleLineText(title) || title?.required !== true) return { rule: 'invalid_title_field', pointer: '/titleFieldId' }
  }

  if (kind === 'stored') {
    if (document.titleFieldId === undefined && fields.some(isSingleLineText)) {
      return { rule: 'missing_title_field', pointer: '/titleFieldId' }
    }

    for (const [i, field] of fields.entries()) {
      if (isTemporaryId(field.id)) return { rule: 'temporary_id_in_stored', pointer: `/fields/${i}/id` }
      for (const [j, option] of (field.options ?? []).entries()) {
        if (isTemporaryId(option.id)) return { rule: 'temporary_id_in_stored', pointer: `/fields/${i}/options/${j}/id` }
      }
    }
  }

  return null
}
