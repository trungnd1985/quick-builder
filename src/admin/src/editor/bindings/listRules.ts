// What a Collection list can filter and sort on (016 binding-expression.md §5): the operators each
// field type has, the literal each takes, and whether a stored filter still fits the schema. The
// server applies the same table (PublishedListTranslation.cs); a filter that no longer fits is
// broken there and matches nothing, so the panel marks it.

import { isObject, type Json } from '../schema/subsetValidator'
import type { FieldInfo, SchemaInfo } from './bindingRules'

export const OPERATORS = ['eq', 'ne', 'contains', 'gt', 'lt', 'empty', 'notEmpty'] as const
export type ListOperator = (typeof OPERATORS)[number]

/** The built-in item values a list filters and sorts on. */
export const ITEM_VALUES = ['slug', 'firstPublishedAt', 'lastPublishedAt'] as const
export type ItemValue = (typeof ITEM_VALUES)[number]

/** The literal a filter takes, by what it reads. */
export type LiteralKind = 'text' | 'number' | 'date' | 'boolean' | 'option' | 'item' | 'none'

export const MAX_FILTERS = 10
export const MAX_SORTS = 3
export const MAX_TEXT_LITERAL = 500

const BY_TYPE: Readonly<Record<string, readonly ListOperator[]>> = {
  text: ['eq', 'ne', 'contains', 'empty', 'notEmpty'],
  number: ['eq', 'ne', 'gt', 'lt', 'empty', 'notEmpty'],
  date: ['eq', 'gt', 'lt', 'empty', 'notEmpty'],
  boolean: ['eq'],
  option: ['eq', 'ne', 'empty', 'notEmpty'],
  reference: ['eq', 'ne', 'empty', 'notEmpty'],
  multiReference: ['contains', 'empty', 'notEmpty'],
  richText: ['empty', 'notEmpty'],
  image: ['empty', 'notEmpty'],
  file: ['empty', 'notEmpty'],
}

const BY_ITEM_VALUE: Readonly<Record<ItemValue, readonly ListOperator[]>> = {
  slug: ['eq', 'ne'],
  firstPublishedAt: ['eq', 'gt', 'lt'],
  lastPublishedAt: ['eq', 'gt', 'lt'],
}

/** What a filter or sort key reads: a field, or a built-in item value. */
export type ListTarget = { field: FieldInfo } | { item: ItemValue }

/** The target a stored filter or sort key names, read against the source's schema; null when it no longer exists. */
export function targetOf(stored: Json, schema: SchemaInfo | null): ListTarget | null {
  if (!isObject(stored)) return null
  if (typeof stored.field === 'string') {
    const field = schema?.fields[stored.field]
    return field ? { field } : null
  }
  return typeof stored.item === 'string' && (ITEM_VALUES as readonly string[]).includes(stored.item)
    ? { item: stored.item as ItemValue }
    : null
}

/** The operators a target has. */
export const operatorsFor = (target: ListTarget): readonly ListOperator[] =>
  'field' in target ? BY_TYPE[target.field.type] ?? [] : BY_ITEM_VALUE[target.item]

/** The literal an operator on a target takes. */
export function literalFor(target: ListTarget, op: ListOperator): LiteralKind {
  if (op === 'empty' || op === 'notEmpty') return 'none'
  if ('item' in target) return target.item === 'slug' ? 'text' : 'date'
  switch (target.field.type) {
    case 'text': return 'text'
    case 'number': return 'number'
    case 'date': return 'date'
    case 'boolean': return 'boolean'
    case 'option': return 'option'
    case 'reference':
    case 'multiReference': return 'item'
    default: return 'none'
  }
}

/** Whether a target can be a sort key: values that order. */
export const sortable = (target: ListTarget): boolean =>
  'item' in target || ['text', 'number', 'date', 'boolean', 'option'].includes(target.field.type)

const DAY = /^\d{4}-\d{2}-\d{2}$/
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/** Whether a literal fits what the operator on the target takes. */
function literalFits(kind: LiteralKind, value: Json | undefined, target: ListTarget): boolean {
  switch (kind) {
    case 'none': return value === undefined
    case 'text': return typeof value === 'string' && value.length <= MAX_TEXT_LITERAL
    case 'number': return typeof value === 'number' && Number.isFinite(value)
    case 'date': return typeof value === 'string' && DAY.test(value)
    case 'boolean': return typeof value === 'boolean'
    case 'option': return typeof value === 'string' && 'field' in target && value in target.field.options
    case 'item': return typeof value === 'string' && UUID.test(value)
  }
}

/**
 * Why a stored filter no longer fits, or null when it does. A value read from a scope
 * (`{ field }` or `{ current }`) is checked where it is read, so only its shape is checked here.
 */
export function filterProblem(filter: Json, schema: SchemaInfo | null): 'field_missing' | 'operator' | 'value' | null {
  if (!isObject(filter)) return 'field_missing'
  const target = targetOf(filter, schema)
  if (!target) return 'field_missing'
  const op = filter.op as ListOperator
  if (!operatorsFor(target).includes(op)) return 'operator'
  const value = filter.value
  if (isObject(value)) return literalFor(target, op) === 'none' ? 'value' : null
  return literalFits(literalFor(target, op), value, target) ? null : 'value'
}
