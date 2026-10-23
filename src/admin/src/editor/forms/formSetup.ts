// What a form must be before it reaches visitors (page-schema.md §3 rule 28; 018 research FS-04).
//
// The twin of QuickBuilder.Pages.Domain.Forms.FormPublishRules: same problems, same order, held
// equal by tests/fixtures/forms/setup-rules.json. The panel shows them on every change; the
// server refuses to publish a form that has any. Rule 28.6 (live pages) needs the site, so this
// only names the pages (formLinkedPages).

import { isObject, type Json, type JsonObject } from '../schema/subsetValidator'

export type FormSetupRule = 'no_fields' | 'duplicate_label' | 'no_options' | 'duplicate_option' | 'min_above_max' | 'reply_field_invalid'

export interface FormSetupProblem {
  rule: FormSetupRule
  fieldId: string | null
}

const TEXT_TYPES = new Set(['text', 'textarea', 'email', 'phone'])
const CHOICE_TYPES = new Set(['select', 'radio', 'checkboxes'])

/** The schema's default maxLength per text-like type (form-block.md §2). */
const defaultMaxLength = (type: string): number => (type === 'textarea' ? 10_000 : type === 'email' ? 254 : 500)

const num = (value: Json | undefined): number | undefined => (typeof value === 'number' ? value : undefined)
const str = (value: Json | undefined): string | undefined => (typeof value === 'string' ? value : undefined)

const fieldsOf = (props: JsonObject): JsonObject[] => (Array.isArray(props.fields) ? props.fields.filter(isObject) : [])

/** Every problem of one form's props: the form's first, then each field in order, then the reply field. */
export function formSetupProblems(props: JsonObject): FormSetupProblem[] {
  const fields = fieldsOf(props)
  const problems: FormSetupProblem[] = []

  if (fields.length === 0) problems.push({ rule: 'no_fields', fieldId: null })

  const labels = new Set<string>()
  for (const field of fields) {
    const id = str(field.id) ?? ''
    const type = str(field.type) ?? ''

    const label = (str(field.label) ?? '').trim().toLowerCase()
    if (labels.has(label)) problems.push({ rule: 'duplicate_label', fieldId: id })
    labels.add(label)

    let optionCount = 0
    if (CHOICE_TYPES.has(type)) {
      const options = Array.isArray(field.options) ? field.options.filter(isObject) : []
      if (options.length === 0) {
        problems.push({ rule: 'no_options', fieldId: id })
      } else {
        optionCount = options.length
        const values = options.map((o) => str(o.value))
        if (new Set(values).size !== values.length) problems.push({ rule: 'duplicate_option', fieldId: id })
      }
    }

    if (minAboveMax(field, type, optionCount)) problems.push({ rule: 'min_above_max', fieldId: id })
  }

  const notify = isObject(props.notify) ? props.notify : undefined
  const replyFieldId = str(notify?.replyFieldId)
  if (notify && replyFieldId !== undefined) {
    const valuesIncluded = notify.includeValues !== false
    const isEmailField = fields.some((f) => f.id === replyFieldId && f.type === 'email')
    if (!valuesIncluded || !isEmailField) problems.push({ rule: 'reply_field_invalid', fieldId: replyFieldId })
  }

  return problems
}

/** The pages rule 28.6 needs live: the redirect's page first, then each consent link once. */
export function formLinkedPages(props: JsonObject): string[] {
  const pages: string[] = []
  const redirect = isObject(props.redirect) ? props.redirect : undefined
  const redirectPage = str(redirect?.pageId)
  if (redirectPage) pages.push(redirectPage)
  for (const field of fieldsOf(props)) {
    const link = str(field.linkPageId)
    if (link && !pages.includes(link)) pages.push(link)
  }
  return pages
}

function minAboveMax(field: JsonObject, type: string, optionCount: number): boolean {
  if (TEXT_TYPES.has(type)) {
    const minLength = num(field.minLength)
    return minLength !== undefined && minLength > (num(field.maxLength) ?? defaultMaxLength(type))
  }
  if (type === 'number') {
    const min = num(field.min)
    const max = num(field.max)
    return min !== undefined && max !== undefined && min > max
  }
  if (type === 'date') {
    // ISO dates compare as strings.
    const earliest = str(field.earliest)
    const latest = str(field.latest)
    return earliest !== undefined && latest !== undefined && earliest > latest
  }
  if (type === 'checkboxes') {
    const low = num(field.minSelected)
    const high = num(field.maxSelected)
    if (low !== undefined && high !== undefined && low > high) return true
    return optionCount > 0 && ((high !== undefined && high > optionCount) || (low !== undefined && low > optionCount))
  }
  return false
}
