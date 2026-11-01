// Which fields another change altered (023 FR-007, research CE-10).
//
// Compared in the admin, by field id, between the values the form loaded and the latest
// stored draft. Values are compared by content, so JSON with the same values in another key
// order is not a change; arrays keep their order, because a reordered list is a change.

export interface ChangedField {
  id: string
  /** The field's name, or its id when the field no longer exists. */
  label: string
}

function same(a: unknown, b: unknown): boolean {
  if (a === b) return true
  if (a === null || b === null || typeof a !== 'object' || typeof b !== 'object') return false
  if (Array.isArray(a) !== Array.isArray(b)) return false
  if (Array.isArray(a) && Array.isArray(b)) return a.length === b.length && a.every((value, i) => same(value, b[i]))

  const left = a as Record<string, unknown>
  const right = b as Record<string, unknown>
  const keys = new Set([...Object.keys(left), ...Object.keys(right)])
  for (const key of keys) {
    if (!same(left[key], right[key])) return false
  }
  return true
}

export function changedFields(
  loaded: Record<string, unknown>,
  latest: Record<string, unknown>,
  fields: readonly { id: string; name: string }[],
): ChangedField[] {
  const changed = new Set<string>()
  for (const id of new Set([...Object.keys(loaded), ...Object.keys(latest)])) {
    if (!same(loaded[id], latest[id])) changed.add(id)
  }

  const known = fields.filter((field) => changed.has(field.id)).map((field) => ({ id: field.id, label: field.name }))
  const gone = [...changed].filter((id) => !fields.some((field) => field.id === id)).map((id) => ({ id, label: id }))
  return [...known, ...gone]
}
