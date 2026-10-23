// A new form field's id (018 FR-006; contracts/form-block.md §2): 12 lowercase hexadecimal
// characters, made when the field is added and never changed. Stored submissions are keyed by
// it, so renaming or moving a field never separates it from what was submitted before.

/** A new field id, unique within `taken` (the form's other field ids). */
export function newFieldId(taken: Iterable<string> = []): string {
  const used = new Set(taken)
  for (;;) {
    const id = crypto.randomUUID().replace(/-/g, '').slice(0, 12)
    if (!used.has(id)) return id
  }
}
