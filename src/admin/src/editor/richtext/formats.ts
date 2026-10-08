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
