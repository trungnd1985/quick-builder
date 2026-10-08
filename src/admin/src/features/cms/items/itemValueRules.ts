// The checks of an item's values as the member types (015 FR-012, research IT-05): a port of
// the server's ItemValidation. Each value is checked against its field type's definition in the
// collection schema contract (rich text against the page schema's rtDoc), then against the
// field's settings. Whether a media file or a referenced item exists needs the server; callers
// that know pass lookups, others leave those checks to the save. The server's report decides;
// itemValues.spec.ts runs the golden files of docs/contracts/collection-schema/examples/items
// through this, and ItemValueGoldenTests runs them on the server.

import type { SchemaDocument, SchemaField } from '../../../api/cms'
import type { Problem } from '../../../api/items'
import { codePointLength, type Json } from '../../../editor/schema/subsetValidator'
import { pageSchema } from '../../../editor/schema/schema'
import { richTextChars } from '../../../editor/schema/documentValidator'
import { checkRichTextRules, imageMedia, imagesWithoutAlt, isEmptyRichText } from '../../../editor/schema/richTextRules'
import { MAX_RICH_TEXT_CHARS } from '../../../editor/schema/limits'
import { collectionSchema } from '../collectionSchema'

export const MAX_REFERENCES = 100
export const SLUG_FIELD = 'slug'
export const MAX_SLUG_LENGTH = 100

/** Problems that mean a value can never be stored (015 data-model §3.2). */
export const MALFORMED = new Set([
  'malformed',
  'unknown_field',
  'unknown_option',
  'reference_missing',
  'reference_wrong_collection',
  'reference_self',
  'duplicate_reference',
  'too_many_references',
  'invalid_slug',
])

export type MediaKind = 'image' | 'document' | 'missing'

/** What only the server knows, when the caller knows it. Undefined means "not known here". */
export interface ItemLookups {
  mediaKind?: (mediaId: string) => MediaKind | undefined
  /** The collection of an item, null when it does not exist. */
  collectionOf?: (itemId: string) => string | null | undefined
}

export const isMalformed = (problem: Problem): boolean => MALFORMED.has(problem.problem)

export const isValidSlug = (slug: string): boolean =>
  slug.length > 0 && slug.length <= MAX_SLUG_LENGTH && /^[a-z0-9]+(-[a-z0-9]+)*$/.test(slug)

const definitionOf = (field: SchemaField): string =>
  ({
    text: 'textValue',
    richText: 'rtDoc',
    number: 'numberValue',
    boolean: 'booleanValue',
    date: field.withTime ? 'dateTimeValue' : 'dateValue',
    image: 'mediaValue',
    file: 'mediaValue',
    option: 'optionValue',
    reference: 'referenceValue',
    multiReference: 'multiReferenceValue',
  })[field.type]

/** A real calendar date in the contract's fixed form, so `2026-13-01` is refused as the server does. */
function isRealDate(value: string, withTime: boolean): boolean {
  const match = withTime
    ? /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})Z$/.exec(value)
    : /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  if (!match) return false
  const [year, month, day, hour = 0, minute = 0, second = 0] = match.slice(1).map(Number)
  const date = new Date(Date.UTC(year!, month! - 1, day!, hour, minute, second))
  return (
    date.getUTCFullYear() === year &&
    date.getUTCMonth() === month! - 1 &&
    date.getUTCDate() === day &&
    hour < 24 &&
    minute < 60 &&
    second < 60
  )
}

function isWellFormed(field: SchemaField, value: Json): boolean {
  if (field.type === 'richText') {
    return pageSchema.validateDefinition('rtDoc', value) === null
      && richTextChars(value) <= MAX_RICH_TEXT_CHARS
      && checkRichTextRules(value, '') === null
  }

  if (collectionSchema.validateDefinition(definitionOf(field), value) !== null) return false
  if (field.type === 'date') return isRealDate(value as string, field.withTime === true)
  return true
}

/** Which setting a well-formed value breaks, if any. */
function settingProblem(field: SchemaField, value: Json): Problem | null {
  switch (field.type) {
    case 'text': {
      const text = value as string
      if (codePointLength(text) > (field.maxLength ?? 256)) return { fieldId: field.id, problem: 'too_long', limit: field.maxLength }
      if (!field.multiline && /[\n\r]/.test(text)) return { fieldId: field.id, problem: 'not_single_line' }
      return null
    }
    case 'number': {
      const number = value as number
      const decimals = field.decimals ?? 0
      if (Number(number.toFixed(decimals)) !== number) return { fieldId: field.id, problem: 'too_many_decimals', limit: decimals }
      if (field.min !== undefined && field.min !== null && number < field.min) return { fieldId: field.id, problem: 'out_of_range', limit: field.min }
      if (field.max !== undefined && field.max !== null && number > field.max) return { fieldId: field.id, problem: 'out_of_range', limit: field.max }
      return null
    }
    case 'option':
      return field.options?.some((o) => o.id === value) ? null : { fieldId: field.id, problem: 'unknown_option' }
    case 'multiReference': {
      const ids = (value as string[]).map((id) => id.toLowerCase())
      return new Set(ids).size === ids.length ? null : { fieldId: field.id, problem: 'duplicate_reference' }
    }
    default:
      return null
  }
}

function checkField(field: SchemaField, value: Json | undefined, itemId: string | null, lookups: ItemLookups): Problem | null {
  if (value === undefined) {
    return field.required && field.type !== 'boolean' ? { fieldId: field.id, problem: 'missing_required' } : null
  }

  if (field.type === 'multiReference' && Array.isArray(value) && value.length > MAX_REFERENCES) {
    return { fieldId: field.id, problem: 'too_many_references', limit: MAX_REFERENCES }
  }

  if (!isWellFormed(field, value)) return { fieldId: field.id, problem: 'malformed' }

  // A rich text with no text and no images is no value (035 spec edge case "Empty structures").
  if (field.type === 'richText' && isEmptyRichText(value)) {
    return field.required ? { fieldId: field.id, problem: 'missing_required' } : null
  }

  // 035 FR-014: an image needs alternative text, or to be decorative; then its file must exist.
  if (field.type === 'richText') {
    const withoutAlt = imagesWithoutAlt(value, '')[0]
    if (withoutAlt !== undefined) return { fieldId: field.id, problem: 'image_alt_missing', path: withoutAlt }
    if (lookups.mediaKind) {
      for (const image of imageMedia(value, '')) {
        const kind = lookups.mediaKind(image.mediaId)
        if (kind === 'missing') return { fieldId: field.id, problem: 'media_missing', path: image.pointer }
        if (kind === 'document') return { fieldId: field.id, problem: 'file_kind', path: image.pointer }
      }
    }
  }

  const setting = settingProblem(field, value)
  if (setting) return setting

  if ((field.type === 'image' || field.type === 'file') && lookups.mediaKind) {
    const kind = lookups.mediaKind((value as { mediaId: string }).mediaId)
    const allowed = field.type === 'image' ? 'images' : (field.accept ?? 'any')
    if (kind === 'missing') return { fieldId: field.id, problem: 'media_missing' }
    if ((allowed === 'images' && kind === 'document') || (allowed === 'documents' && kind === 'image')) {
      return { fieldId: field.id, problem: 'file_kind' }
    }
  }

  if (field.type === 'reference' || field.type === 'multiReference') {
    const ids = Array.isArray(value) ? (value as string[]) : [value as string]
    for (const id of ids) {
      if (itemId !== null && id.toLowerCase() === itemId.toLowerCase()) return { fieldId: field.id, problem: 'reference_self' }
      const collection = lookups.collectionOf?.(id)
      if (collection === null) return { fieldId: field.id, problem: 'reference_missing' }
      if (collection !== undefined && collection !== field.target) return { fieldId: field.id, problem: 'reference_wrong_collection' }
    }
  }

  return null
}

/**
 * The report of an item's values and slug, in the server's order: the slug, then each field in
 * schema order (one problem each), then keys that are not fields.
 */
export function checkItem(
  schema: SchemaDocument,
  slug: string,
  values: Record<string, unknown>,
  itemId: string | null = null,
  lookups: ItemLookups = {},
): Problem[] {
  const problems: Problem[] = []
  if (!isValidSlug(slug)) problems.push({ fieldId: SLUG_FIELD, problem: 'invalid_slug', limit: MAX_SLUG_LENGTH })

  for (const field of schema.fields) {
    const problem = checkField(field, values[field.id] as Json | undefined, itemId, lookups)
    if (problem) problems.push(problem)
  }

  const known = new Set(schema.fields.map((f) => f.id))
  for (const id of Object.keys(values)) {
    if (!known.has(id)) problems.push({ fieldId: id, problem: 'unknown_field' })
  }

  return problems
}
