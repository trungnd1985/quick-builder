// The format groups a rich text field may limit (035 FR-022, research RT-09). The core formats
// — paragraphs, lists, line breaks, inline styles, links and alignment — are always allowed.

export interface AllowedFormats {
  /** Headings and quotes. */
  headings: boolean
  images: boolean
  tables: boolean
}

/** Every group allowed: Rich Text blocks on pages, and fields without a `formats` setting. */
export const ALL_FORMATS: Readonly<AllowedFormats> = Object.freeze({ headings: true, images: true, tables: true })

/** A field's `formats` setting as the groups it allows: a missing group is allowed. */
export function allowedFormats(formats: Partial<AllowedFormats> | undefined): AllowedFormats {
  return { headings: formats?.headings !== false, images: formats?.images !== false, tables: formats?.tables !== false }
}

/** Whether `after` disallows a group `before` allows: a reported change (035 FR-025). */
export function narrows(before: AllowedFormats, after: AllowedFormats): boolean {
  return (before.headings && !after.headings) || (before.images && !after.images) || (before.tables && !after.tables)
}
