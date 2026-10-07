// Which changes a save makes, and how each treats stored values (contract §7, research CS-06/07).
//
// A port of the server's SchemaDiff and FieldConversion.Classify, for one decision: whether
// the builder can save straight away (every change free, FR-030) or must show the preview
// first. The server classifies again and enforces confirmation itself, so a disagreement here
// can only cost an extra preview, never lose a value. schemaChanges.spec.ts holds the cases the
// server's unit tests use.

import type { ChangeRule, FieldType, SchemaDocument, SchemaField } from '../../api/cms'

const TYPE_CHANGES: Record<string, ChangeRule> = {
  'text>richText': 'lossless',
  'richText>text': 'lossy',
  'number>text': 'lossless',
  'text>number': 'lossy',
  'date>text': 'lossless',
  'text>date': 'lossy',
  'option>text': 'lossless',
  'text>option': 'lossy',
  'boolean>text': 'lossless',
  'image>file': 'lossless',
  'file>image': 'lossy',
  'reference>multiReference': 'lossless',
  'multiReference>reference': 'lossy',
}

/** The rule of changing a field from one type to another. */
export const typeChangeRule = (from: FieldType, to: FieldType): ChangeRule =>
  from === to ? 'free' : TYPE_CHANGES[`${from}>${to}`] ?? 'forbidden'

export interface ClientChange {
  kind: string
  fieldId: string | null
  rule: ChangeRule
}

const optionIds = (field: SchemaField) => (field.options ?? []).map((o) => o.id)

function settingRules(before: SchemaField, after: SchemaField): ChangeRule[] {
  const rules: ChangeRule[] = []
  const differs = (name: keyof SchemaField) => before[name] !== after[name]

  switch (after.type) {
    case 'text':
      if (differs('multiline')) rules.push(after.multiline ? 'free' : 'lossless')
      if (differs('maxLength')) rules.push((after.maxLength ?? 0) < (before.maxLength ?? 0) ? 'reported' : 'free')
      break
    case 'number':
      if (differs('decimals')) rules.push((after.decimals ?? 0) < (before.decimals ?? 0) ? 'lossy' : 'free')
      if (differs('min')) rules.push(after.min !== undefined && (before.min === undefined || after.min > before.min) ? 'reported' : 'free')
      if (differs('max')) rules.push(after.max !== undefined && (before.max === undefined || after.max < before.max) ? 'reported' : 'free')
      break
    case 'date':
      if (differs('withTime')) rules.push(after.withTime ? 'lossless' : 'lossy')
      break
    case 'file':
      if (differs('accept')) rules.push(after.accept === 'any' ? 'free' : 'reported')
      break
    case 'reference':
    case 'multiReference':
      if (differs('target')) rules.push('forbidden')
      break
    case 'option': {
      const kept = new Set(optionIds(after))
      if (optionIds(before).some((id) => !kept.has(id))) rules.push('lossy')
      break
    }
  }

  return rules
}

/** Every change between the stored schema and the proposal that is not free. */
export function valueChanges(stored: SchemaDocument, proposal: SchemaDocument, fillIns: ReadonlySet<string> = new Set()): ClientChange[] {
  const changes: ClientChange[] = []
  const storedById = new Map(stored.fields.map((f) => [f.id, f]))
  const proposedIds = new Set(proposal.fields.map((f) => f.id))

  for (const field of proposal.fields) {
    const before = storedById.get(field.id)
    if (!before) {
      const reported = fillIns.has(field.id) || (field.required && field.type !== 'boolean')
      if (reported) changes.push({ kind: 'fieldAdded', fieldId: field.id, rule: 'reported' })
      continue
    }

    if (!before.required && field.required) changes.push({ kind: 'requiredChanged', fieldId: field.id, rule: 'reported' })

    if (before.type !== field.type) {
      changes.push({ kind: 'typeChanged', fieldId: field.id, rule: typeChangeRule(before.type, field.type) })
      continue
    }

    for (const rule of settingRules(before, field)) {
      if (rule !== 'free') changes.push({ kind: 'settingsChanged', fieldId: field.id, rule })
    }
  }

  for (const field of stored.fields) {
    if (!proposedIds.has(field.id)) changes.push({ kind: 'fieldRemoved', fieldId: field.id, rule: 'lossy' })
  }

  return changes
}

/** Whether a proposal can be saved without first showing the preview (FR-030). */
export const needsPreview = (stored: SchemaDocument, proposal: SchemaDocument, fillIns?: ReadonlySet<string>): boolean =>
  valueChanges(stored, proposal, fillIns).length > 0
