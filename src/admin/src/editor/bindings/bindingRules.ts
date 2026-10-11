// Which value may fill which block property (016 research TB-05; binding-expression.md §4).
//
// A port of the Rendering module's BindingRules. Both run the contract's resolve cases, so the
// editor marks exactly the bindings the published site treats as broken. A broken binding is
// never refused at save: schemas change after the page is saved, so it is shown as empty.

import { isObject, type Json } from '../schema/subsetValidator'

export type PropKind = 'text' | 'richText' | 'image' | 'link'

export const BROKEN = {
  fieldMissing: 'field_missing',
  typeMismatch: 'type_mismatch',
  scopeMissing: 'scope_missing',
  viaNotReference: 'via_not_reference',
} as const

export type BrokenReason = (typeof BROKEN)[keyof typeof BROKEN]

export const BUILT_INS = ['url', 'slug', 'firstPublishedAt', 'lastPublishedAt'] as const
export type BuiltIn = (typeof BUILT_INS)[number]

/** One field of a collection schema, with the settings that change how its value reads. */
export interface FieldInfo {
  id: string
  name: string
  type: string
  multiline: boolean
  withTime: boolean
  decimals: number
  options: Record<string, string>
  target: string | null
}

/** What the editor needs of a collection's schema (collection-schema.md §2). */
export interface SchemaInfo {
  collectionId: string
  titleFieldId: string | null
  fields: Record<string, FieldInfo>
  /** In schema order, for pickers. */
  order: string[]
}

/** Reads a stored collection schema document; fields it cannot read are left out. */
export function parseSchema(collectionId: string, schema: Json): SchemaInfo {
  const fields: Record<string, FieldInfo> = {}
  const order: string[] = []
  const list = isObject(schema) && Array.isArray(schema.fields) ? schema.fields : []
  for (const field of list) {
    if (!isObject(field) || typeof field.id !== 'string' || typeof field.type !== 'string') continue
    const options: Record<string, string> = {}
    if (Array.isArray(field.options)) {
      for (const option of field.options) {
        if (isObject(option) && typeof option.id === 'string' && typeof option.label === 'string') options[option.id] = option.label
      }
    }
    fields[field.id] = {
      id: field.id,
      name: typeof field.name === 'string' ? field.name : field.id,
      type: field.type,
      multiline: field.multiline === true,
      withTime: field.withTime === true,
      decimals: typeof field.decimals === 'number' ? field.decimals : 0,
      options,
      target: typeof field.target === 'string' ? field.target : null,
    }
    order.push(field.id)
  }
  const title = isObject(schema) && typeof schema.titleFieldId === 'string' ? schema.titleFieldId : null
  return { collectionId, titleFieldId: title, fields, order }
}

/** The bindable properties of each block type, and the kind of value each takes. */
export function kindOf(blockType: string, prop: string): PropKind | null {
  switch (`${blockType}.${prop}`) {
    case 'heading.text':
    case 'text.text':
    case 'button.label':
    case 'image.alt':
      return 'text'
    case 'rich-text.content':
      return 'richText'
    case 'image.media':
      return 'image'
    case 'button.link':
    case 'image.link':
      return 'link'
    default:
      return null
  }
}

/** Whether a field can fill a property of `kind`. */
export function fits(kind: PropKind, field: FieldInfo): boolean {
  switch (kind) {
    case 'text':
      return ['text', 'number', 'date', 'option', 'boolean', 'reference'].includes(field.type)
    case 'richText':
      return field.type === 'richText' || (field.type === 'text' && field.multiline)
    case 'image':
      return field.type === 'image'
    case 'link':
      return field.type === 'file' || (field.type === 'text' && !field.multiline)
  }
}

/** Whether a built-in item value can fill a property of `kind`. */
export function fitsBuiltIn(kind: PropKind, builtIn: string): boolean {
  if (kind === 'text') return builtIn === 'slug' || builtIn === 'firstPublishedAt' || builtIn === 'lastPublishedAt'
  if (kind === 'link') return builtIn === 'url'
  return false
}
