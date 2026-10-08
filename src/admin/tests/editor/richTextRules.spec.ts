import { describe, expect, it } from 'vitest'
import { checkRichTextRules, imagesWithoutAlt, usesExtendedFormats } from '../../src/editor/schema/richTextRules'
import type { Json } from '../../src/editor/schema/subsetValidator'

// The same cases as the server's RichTextRulesTests (035 research RT-02, SC-007).

const AT = '/root/children/0/children/0/props/content'

const doc = (...blocks: Json[]): Json => ({ type: 'doc', content: blocks })
const paragraph = (text = 'x'): Json => ({ type: 'paragraph', content: [{ type: 'text', text }] })
const image = (alt?: string, decorative?: boolean): Json => ({
  type: 'image',
  attrs: {
    media: { mediaId: '0192f5a1-0000-7000-8000-000000000010' },
    ...(alt === undefined ? {} : { alt }),
    ...(decorative === undefined ? {} : { decorative }),
  },
})
const cell = (type = 'tableCell'): Json => ({ type, content: [{ type: 'paragraph' }] })
const row = (...cells: Json[]): Json => ({ type: 'tableRow', content: cells })
const table = (...rows: Json[]): Json => ({ type: 'table', content: rows })
const grid = (rows = 2, columns = 2, header = true): Json =>
  table(...Array.from({ length: rows }, (_, r) => row(...Array.from({ length: columns }, () => cell(header && r === 0 ? 'tableHeader' : 'tableCell')))))
const times = (n: number, block: Json): Json[] => Array.from({ length: n }, () => block)

describe('rich text rules 10–12', () => {
  it.each([[50, null], [51, 'rich_text_too_many_images']])('allows 50 images (%i)', (n, rule) => {
    const failure = checkRichTextRules(doc(...times(n, image('a'))), AT)
    expect(failure?.rule ?? null).toBe(rule)
    if (rule) expect(failure?.pointer).toBe(AT)
  })

  it.each([[20, null], [21, 'rich_text_too_many_tables']])('allows 20 tables (%i)', (n, rule) => {
    expect(checkRichTextRules(doc(...times(n, grid())), AT)?.rule ?? null).toBe(rule)
  })

  it('counts images before tables', () => {
    expect(checkRichTextRules(doc(...times(21, grid()), ...times(51, image('a'))), AT)?.rule).toBe('rich_text_too_many_images')
  })

  it('refuses a ragged table at the first row that differs', () => {
    expect(checkRichTextRules(doc(paragraph(), table(row(cell(), cell()), row(cell(), cell()), row(cell()))), AT))
      .toEqual({ rule: 'table_not_rectangular', pointer: `${AT}/content/1/content/2` })
  })

  it('refuses a header cell after the first row', () => {
    expect(checkRichTextRules(doc(table(row(cell(), cell()), row(cell(), cell('tableHeader')))), AT))
      .toEqual({ rule: 'table_header_misplaced', pointer: `${AT}/content/0/content/1/content/1` })
  })

  it('refuses a first row that mixes header and ordinary cells', () => {
    expect(checkRichTextRules(doc(table(row(cell('tableHeader'), cell()), row(cell(), cell()))), AT))
      .toEqual({ rule: 'table_header_misplaced', pointer: `${AT}/content/0/content/0/content/1` })
  })

  it('checks rule 11 before rule 12', () => {
    expect(checkRichTextRules(doc(table(row(cell(), cell('tableHeader')), row(cell()))), AT)?.rule).toBe('table_not_rectangular')
  })

  it('passes a table at the limits, with or without a header row', () => {
    expect(checkRichTextRules(doc(grid(200, 20)), AT)).toBeNull()
    expect(checkRichTextRules(doc(grid(3, 3, false)), AT)).toBeNull()
  })
})

describe('what the write paths ask', () => {
  it('lists images without alternative text by pointer', () => {
    const value = doc(image('A shop'), image(), image('   '), image(undefined, true), image('', false), paragraph())
    expect(imagesWithoutAlt(value, AT)).toEqual([`${AT}/content/1`, `${AT}/content/2`, `${AT}/content/4`])
  })

  it.each<Json>([
    { type: 'heading', attrs: { level: 2 } },
    { type: 'blockquote', content: [{ type: 'paragraph' }] },
    { type: 'image', attrs: { media: { mediaId: '0192f5a1-0000-7000-8000-000000000010' } } },
    { type: 'table', content: [{ type: 'tableRow', content: [{ type: 'tableCell', content: [{ type: 'paragraph' }] }] }] },
    { type: 'paragraph', attrs: { align: 'center' } },
    { type: 'paragraph', content: [{ type: 'text', text: 'u', marks: [{ type: 'underline' }] }] },
    { type: 'paragraph', content: [{ type: 'text', text: 's', marks: [{ type: 'strike' }] }] },
    { type: 'bulletList', content: [{ type: 'listItem', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'c', marks: [{ type: 'code' }] }] }] }] },
  ])('counts every 035 format as extended (%#)', (block) => {
    expect(usesExtendedFormats(doc(block))).toBe(true)
  })

  it('does not count 015 formats as extended', () => {
    const value = doc(
      { type: 'paragraph', content: [{ type: 'text', text: 'b', marks: [{ type: 'bold' }, { type: 'italic' }, { type: 'link', attrs: { to: { type: 'url', href: 'https://example.com' } } }] }, { type: 'hardBreak' }] },
      { type: 'orderedList', attrs: { start: 3 }, content: [{ type: 'listItem', content: [{ type: 'paragraph' }] }] },
    )
    expect(usesExtendedFormats(value)).toBe(false)
  })

  it('has nothing to say about a value that is not a document', () => {
    expect(usesExtendedFormats('text')).toBe(false)
    expect(checkRichTextRules('text', AT)).toBeNull()
    expect(imagesWithoutAlt('text', AT)).toEqual([])
  })
})
