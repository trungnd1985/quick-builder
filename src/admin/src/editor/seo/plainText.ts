// Metadata values as one line of text (017 FR-008, research SE-04): rich text as its words,
// whitespace collapsed, and a value over the field's limit cut at a word with an ellipsis.
//
// A port of the Rendering module's PlainText; both run the contract's seo-* resolve cases.

import { isObject, type Json } from '../schema/subsetValidator'

/** What is added where a value was cut. */
export const ELLIPSIS = '…'

// The same set as the server: ASCII whitespace, no-break and the Unicode spaces.
const WHITESPACE = /[\t\n\v\f\r \u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff]+/gu

/** The longest value of each metadata text field, as the page schema allows. */
export function maxLength(field: string): number {
  return field === 'title' || field === 'ogTitle' ? 200 : 500
}

/** Whitespace runs as one space, without leading or trailing space. */
export function collapse(text: string): string {
  return text.replace(WHITESPACE, ' ').replace(/^ +| +$/g, '')
}

/** A rich text document's text: block texts joined with spaces, marks dropped. */
export function plainTextOf(richText: Json): string {
  const parts: string[] = []
  append(richText, parts)
  return collapse(parts.join(''))
}

/**
 * `text` collapsed and, when longer than `max`, cut at the last space before `max - 1` with an
 * ellipsis added, so it is at most `max` long. A single word longer than that is cut inside it.
 */
export function clean(text: string, max: number): string {
  const collapsed = collapse(text)
  if (collapsed.length <= max) return collapsed

  const limit = max - ELLIPSIS.length
  const space = collapsed.lastIndexOf(' ', limit)
  let cut = space > 0 ? space : limit
  const code = collapsed.charCodeAt(cut - 1)
  if (code >= 0xd800 && code <= 0xdbff) cut--
  return collapsed.slice(0, cut).replace(/ +$/, '') + ELLIPSIS
}

function append(node: Json, parts: string[]): void {
  if (!isObject(node)) return
  if (node.type === 'text') {
    if (typeof node.text === 'string') parts.push(node.text)
    return
  }
  if (node.type === 'hardBreak') {
    parts.push(' ')
    return
  }
  if (Array.isArray(node.content)) {
    for (const child of node.content) append(child, parts)
    parts.push(' ')
  }
}
