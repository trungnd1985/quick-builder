// What a visitor's values must be (018 FR-007; research FS-08): the twin of
// QuickBuilder.Forms.Domain.FormValueRules, held to the same answers by
// tests/fixtures/forms/value-rules.json. The published form script carries its own copy of these
// rules (FormScript.cs), tested against the same fixture.
//
// Normalising: CR LF and CR become LF; single-line values are trimmed and their line breaks
// become spaces; multi-line text loses trailing whitespace only; an empty value is missing.
// Lengths are counted in code points.

import { isObject, type JsonObject } from '../schema/subsetValidator'

export const VALUE_CODES = [
  'required', 'too_short', 'too_long', 'email', 'phone', 'number', 'whole', 'too_small', 'too_large',
  'date', 'too_early', 'too_late', 'choice', 'too_few', 'too_many',
] as const

export type ValueCode = (typeof VALUE_CODES)[number]

export interface FieldCheck {
  code: ValueCode | null
  values: string[]
}

// The page schema's own email shape: no spaces or control characters, one @, a dot in the domain.
// Built from character codes so the pattern holds no escapes that tools could rewrite.
const ADDRESS_CHAR = `[^${String.fromCharCode(0)}-${String.fromCharCode(32)}${String.fromCharCode(127)}@]`
const EMAIL = new RegExp(`^${ADDRESS_CHAR}+@${ADDRESS_CHAR}+[.]${ADDRESS_CHAR}+$`)
const PHONE = /^[0-9 +()-]+$/
const NUMBER = /^-?[0-9]+(\.[0-9]+)?$/
const ISO_DATE = /^[0-9]{4}-[0-9]{2}-[0-9]{2}$/

const defaultMaxLength = (type: string): number | undefined =>
  type === 'text' || type === 'phone' ? 500 : type === 'textarea' ? 10_000 : type === 'email' ? 254 : undefined

const num = (value: unknown): number | undefined => (typeof value === 'number' ? value : undefined)
const str = (value: unknown): string | undefined => (typeof value === 'string' ? value : undefined)
const length = (value: string): number => [...value].length

/** LF line breaks, and the trimming the field's kind calls for. */
export function normalise(value: string, multiLine: boolean): string {
  const text = value.replace(/\r\n/g, '\n').replace(/\r/g, '\n')
  return multiLine ? text.trimEnd() : text.replace(/\n/g, ' ').trim()
}

const isRequired = (field: JsonObject): boolean => (field.required === undefined ? field.type === 'consent' : field.required === true)

const optionValues = (field: JsonObject): string[] =>
  Array.isArray(field.options) ? field.options.filter(isObject).map((o) => String(o.value ?? '')) : []

/** A real calendar date in YYYY-MM-DD. */
function isDate(value: string): boolean {
  if (!ISO_DATE.test(value)) return false
  const [year, month, day] = value.split('-').map(Number) as [number, number, number]
  const date = new Date(Date.UTC(year, month - 1, day))
  return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day
}

function lengthCode(field: JsonObject, value: string): ValueCode | null {
  const count = length(value)
  const min = num(field.minLength)
  const max = num(field.maxLength) ?? defaultMaxLength(String(field.type))
  if (min !== undefined && count < min) return 'too_short'
  if (max !== undefined && count > max) return 'too_long'
  return null
}

function codeOf(field: JsonObject, value: string): ValueCode | null {
  switch (field.type) {
    case 'email':
      return value.length > 254 || !EMAIL.test(value) ? 'email' : lengthCode(field, value)
    case 'phone':
      return !PHONE.test(value) || !/[0-9]/.test(value) ? 'phone' : lengthCode(field, value)
    case 'number': {
      if (!NUMBER.test(value)) return 'number'
      const number = Number(value)
      const min = num(field.min)
      const max = num(field.max)
      if (field.wholeNumber === true && !Number.isInteger(number)) return 'whole'
      if (min !== undefined && number < min) return 'too_small'
      if (max !== undefined && number > max) return 'too_large'
      return null
    }
    case 'date': {
      if (!isDate(value)) return 'date'
      const earliest = str(field.earliest)
      const latest = str(field.latest)
      if (earliest !== undefined && value < earliest) return 'too_early'
      if (latest !== undefined && value > latest) return 'too_late'
      return null
    }
    case 'select':
    case 'radio':
      return optionValues(field).includes(value) ? null : 'choice'
    default:
      return lengthCode(field, value)
  }
}

/** The values to store for a field and why they fail, if they do. */
export function checkField(field: JsonObject, submitted: readonly string[]): FieldCheck {
  const type = String(field.type)

  if (type === 'checkboxes') {
    const chosen = [...new Set(submitted.map((v) => normalise(v, false)).filter((v) => v.length > 0))]
    if (chosen.length === 0) return { code: isRequired(field) ? 'required' : null, values: [] }
    const values = optionValues(field)
    if (chosen.some((c) => !values.includes(c))) return { code: 'choice', values: chosen }
    const ordered = values.filter((v) => chosen.includes(v))
    const min = num(field.minSelected)
    const max = num(field.maxSelected)
    const code = min !== undefined && ordered.length < min ? 'too_few' : max !== undefined && ordered.length > max ? 'too_many' : null
    return { code, values: ordered }
  }

  if (type === 'checkbox' || type === 'consent') {
    return submitted.some((v) => v.trim().length > 0)
      ? { code: null, values: ['yes'] }
      : { code: isRequired(field) ? 'required' : null, values: [] }
  }

  const value = submitted.length === 0 ? '' : normalise(submitted[0]!, type === 'textarea')
  if (value.trim().length === 0) return { code: isRequired(field) ? 'required' : null, values: [] }
  return { code: codeOf(field, value), values: [value] }
}
