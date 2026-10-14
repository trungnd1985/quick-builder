// The content types an item page can describe itself as (017 research SE-19).
//
// Loaded from the contract's own file, docs/contracts/structured-data/types.json, which the
// renderer embeds too; structuredDataTypes.spec.ts holds both to the tables of
// structured-data.md §3. `structuredBroken` is a port of the renderer's ItemNode.Broken and runs
// the same `map/` golden examples, so the editor marks exactly the bindings the site leaves out.

import rawTypes from '@contracts/structured-data/types.json'
import { isObject, type Json, type JsonObject } from '../schema/subsetValidator'
import type { FieldInfo, SchemaInfo } from '../bindings/bindingRules'

export interface StructuredDataProperty {
  name: string
  required: boolean
  /** Field types, built-in values and `template` that may fill it. */
  accepts: string[]
  /** `name`, `firstPublishedAt` or `lastPublishedAt`, when it has one. */
  default: string | null
}

export interface StructuredDataType {
  name: string
  properties: StructuredDataProperty[]
}

interface RawProperty { name: string; required?: boolean; accepts: string[]; default?: string }

export const STRUCTURED_DATA_TYPES: Readonly<Record<string, StructuredDataType>> = Object.fromEntries(
  Object.entries((rawTypes as { types: Record<string, { properties: RawProperty[] }> }).types).map(([name, type]) => [name, {
    name,
    properties: type.properties.map((p) => ({ name: p.name, required: p.required === true, accepts: p.accepts, default: p.default ?? null })),
  }]),
)

/** What a field of this type offers, in the contract's accept names; null when nothing accepts it. */
export function acceptOf(field: FieldInfo): string | null {
  return ['text', 'richText', 'option', 'number', 'date', 'boolean', 'image', 'reference'].includes(field.type) ? field.type : null
}

/** What may fill one part of a text template. */
const TEXT_ACCEPTS = ['text', 'option', 'number', 'date', 'reference', 'slug']

function readerBroken(reader: JsonObject, accepts: string[], schema: SchemaInfo | null, schemas: Readonly<Record<string, SchemaInfo>>): string | null {
  if (typeof reader.scope === 'string' && reader.scope !== 'page') return 'scope_missing'
  if (!schema) return 'scope_missing'
  let target: SchemaInfo | undefined = schema
  if (typeof reader.via === 'string') {
    const reference = schema.fields[reader.via]
    if (!reference) return 'field_missing'
    if (reference.type !== 'reference') return 'via_not_reference'
    target = reference.target ? schemas[reference.target] : undefined
    if (!target) return null
  }
  if (typeof reader.item === 'string') return accepts.includes(reader.item) ? null : 'type_mismatch'
  const field = typeof reader.field === 'string' ? target.fields[reader.field] : undefined
  if (!field) return 'field_missing'
  const accept = acceptOf(field)
  return accept && accepts.includes(accept) ? null : 'type_mismatch'
}

/** Why each bound property of a mapping is broken: `unknown_property`, or a binding reason. */
export function structuredBroken(
  typeName: string | null,
  bind: Json | undefined,
  schema: SchemaInfo | null,
  schemas: Readonly<Record<string, SchemaInfo>>,
): Record<string, string> {
  const broken: Record<string, string> = {}
  const type = typeName ? STRUCTURED_DATA_TYPES[typeName] : undefined
  if (!isObject(bind)) return broken
  for (const [name, binding] of Object.entries(bind)) {
    const property = type?.properties.find((p) => p.name === name)
    if (!property) {
      broken[name] = 'unknown_property'
      continue
    }
    if (!isObject(binding) || !isObject(binding.value)) continue
    const value = binding.value
    let readers: JsonObject[]
    let accepts = property.accepts
    if (Array.isArray(value.parts)) {
      if (!property.accepts.includes('template')) {
        broken[name] = 'type_mismatch'
        continue
      }
      readers = value.parts.filter(isObject)
      accepts = TEXT_ACCEPTS
    } else {
      readers = [value]
    }
    for (const reader of readers) {
      const reason = readerBroken(reader, accepts, schema, schemas)
      if (reason) {
        broken[name] = reason
        break
      }
    }
  }
  return broken
}

/** The fields and built-in values of the page's item that can fill a property, for its picker. */
export function choicesFor(property: StructuredDataProperty, schema: SchemaInfo): { key: string; label: string; value: JsonObject }[] {
  const choices: { key: string; label: string; value: JsonObject }[] = []
  for (const id of schema.order) {
    const field = schema.fields[id]
    const accept = acceptOf(field)
    if (accept && property.accepts.includes(accept)) choices.push({ key: `field:${id}`, label: field.name, value: { field: id } })
  }
  for (const builtIn of ['slug', 'firstPublishedAt', 'lastPublishedAt']) {
    if (property.accepts.includes(builtIn)) choices.push({ key: `item:${builtIn}`, label: builtIn, value: { item: builtIn } })
  }
  return choices
}
