import { afterEach, describe, expect, it } from 'vitest'
import { markRaw } from 'vue'
import { fireEvent, render, screen } from '@testing-library/vue'
import axe from 'axe-core'
import { Editor } from '@tiptap/core'
import en from '../../src/i18n/en.json'
import RichTextToolbar from '../../src/editor/richtext/RichTextToolbar.vue'
import { noticeText } from '../../src/editor/richtext/notices'
import type { RichTextNotice } from '../../src/editor/richtext/paste'
import { richTextExtensions } from '../../src/editor/richtext/tiptap'
import { toContractRichText } from '../../src/editor/richtext/toContractRichText'
import { validateDocument } from '../../src/editor/schema/documentValidator'
import { i18n } from './harness'

// Simple tables (035 US5, FR-011, spec edge case "Limits", research RT-06).

let editor: Editor | null = null
let notices: RichTextNotice[] = []

afterEach(() => {
  editor?.destroy()
  editor = null
  notices = []
  document.body.innerHTML = ''
})

const p = (text?: string) => (text ? { type: 'paragraph', content: [{ type: 'text', text }] } : { type: 'paragraph' })
const cell = (type = 'tableCell', text?: string) => ({ type, content: [p(text)] })
const grid = (rows: number, columns: number, header = false) => ({
  type: 'table',
  content: Array.from({ length: rows }, (_, r) => ({ type: 'tableRow', content: Array.from({ length: columns }, () => cell(header && r === 0 ? 'tableHeader' : 'tableCell')) })),
})

function mount(content: unknown[] = [p('Shop')]) {
  const element = document.createElement('div')
  document.body.appendChild(element)
  editor = markRaw(new Editor({ element, extensions: richTextExtensions({ onNotice: (n) => notices.push(n) }), content: { type: 'doc', content } }))
  return render(RichTextToolbar, { props: { editor: editor!, siteId: 'site', notice: '' }, global: { plugins: [i18n] } })
}

const tables = () => toContractRichText(editor!.getJSON()).content.filter((b) => (b as { type: string }).type === 'table') as { content: { content: { type: string }[] }[] }[]
const shape = (t: { content: { content: { type: string }[] }[] }) => t.content.map((row) => row.content.map((c) => (c.type === 'tableHeader' ? 'H' : 'C')).join(''))
/** Puts the cursor in the table's first cell. */
const intoTable = () => {
  let at = 0
  editor!.state.doc.descendants((node, pos) => {
    if (at === 0 && (node.type.name === 'tableCell' || node.type.name === 'tableHeader')) at = pos + 2
  })
  editor!.commands.setTextSelection(at)
}
const button = (name: string) => screen.getByRole('button', { name })

describe('tables', () => {
  it('inserts a 3 × 3 table with a header row', async () => {
    mount()

    await fireEvent.click(button(en.editor.richText.table.insert))

    expect(tables().map(shape)).toEqual([['HHH', 'CCC', 'CCC']])
  })

  it('moves between cells with Tab and Shift+Tab, and adds a row from the last cell', async () => {
    mount([grid(2, 2)])
    intoTable()
    const tab = (shiftKey = false) => fireEvent.keyDown(editor!.view.dom, { key: 'Tab', code: 'Tab', keyCode: 9, shiftKey })

    await tab()
    editor!.commands.insertContent('b')
    await tab(true)
    editor!.commands.insertContent('a')
    await tab()
    await tab()
    await tab()
    await tab()

    const contract = tables()[0]!
    expect(contract.content).toHaveLength(3)
    expect(JSON.stringify(contract.content[0])).toMatch(/"a".*"b"/)
  })

  it('adds and removes rows and columns, and turns the header row on and off', async () => {
    mount([grid(2, 2)])
    intoTable()
    expect(await screen.findByRole('group', { name: en.editor.richText.table.group })).toBeTruthy()

    await fireEvent.click(button(en.editor.richText.table.rowBelow))
    await fireEvent.click(button(en.editor.richText.table.columnAfter))
    expect(tables().map(shape)).toEqual([['CCC', 'CCC', 'CCC']])

    await fireEvent.click(button(en.editor.richText.table.deleteRow))
    await fireEvent.click(button(en.editor.richText.table.deleteColumn))
    expect(tables().map(shape)).toEqual([['CC', 'CC']])

    await fireEvent.click(button(en.editor.richText.table.headerRow))
    expect(tables().map(shape)).toEqual([['HH', 'CC']])
    expect(button(en.editor.richText.table.headerRow).getAttribute('aria-pressed')).toBe('true')

    await fireEvent.click(button(en.editor.richText.table.deleteTable))
    expect(tables()).toEqual([])
  })

  it('refuses a 21st column, a 201st row and a 21st table, and says which limit', async () => {
    mount([grid(1, 20), grid(200, 1)])
    intoTable()

    await fireEvent.click(button(en.editor.richText.table.columnAfter))
    expect(tables()[0]!.content[0]!.content).toHaveLength(20)

    editor!.commands.setTextSelection(editor!.state.doc.content.size - 3)
    await fireEvent.click(button(en.editor.richText.table.rowBelow))
    await fireEvent.keyDown(editor!.view.dom, { key: 'Tab', code: 'Tab', keyCode: 9 })
    expect(tables()[1]!.content).toHaveLength(200)

    editor!.commands.setContent({ type: 'doc', content: [...Array.from({ length: 20 }, () => grid(1, 1)), p('end')] })
    editor!.commands.setTextSelection(editor!.state.doc.content.size - 1)
    await fireEvent.click(button(en.editor.richText.table.insert))
    expect(tables()).toHaveLength(20)

    expect(notices).toEqual([{ kind: 'limit', limit: 'columns' }, { kind: 'limit', limit: 'rows' }, { kind: 'limit', limit: 'rows' }, { kind: 'limit', limit: 'tables' }])
    expect(noticeText(i18n.global.t, i18n.global.te, notices[0]!)).toBe(en.editor.richText.notice.columnsLimit)
  })

  it('shows the table commands only inside a table, and passes the accessibility checks', async () => {
    const { container } = mount([p('Before'), grid(2, 2, true)])
    expect(screen.queryByRole('group', { name: en.editor.richText.table.group })).toBeNull()

    intoTable()
    await screen.findByRole('group', { name: en.editor.richText.table.group })
    expect((await axe.run(container)).violations).toEqual([])
  })

  it('offers no table button when the field does not allow tables', () => {
    const element = document.createElement('div')
    document.body.appendChild(element)
    editor = markRaw(new Editor({ element, extensions: richTextExtensions(), content: { type: 'doc', content: [p('x')] } }))
    render(RichTextToolbar, { props: { editor: editor!, siteId: 'site', allowed: { headings: true, images: true, tables: false } }, global: { plugins: [i18n] } })

    expect(screen.queryByRole('button', { name: en.editor.richText.table.insert })).toBeNull()
  })
})

describe('toContractRichText: tables', () => {
  const asDocument = (content: unknown) => ({
    schemaVersion: 1,
    root: { id: 'root', type: 'root', props: {}, children: [{ id: 's', type: 'section', props: {}, children: [{ id: 'r', type: 'rich-text', props: { content: { type: 'doc', content } }, children: [] }] }] },
  })

  it('drops spans and widths, splits merged cells and pads short rows', () => {
    const contract = toContractRichText({ type: 'doc', content: [{ type: 'table', content: [
      { type: 'tableRow', content: [{ type: 'tableHeader', attrs: { colspan: 2, rowspan: 1, colwidth: [120, 80] }, content: [p('A')] }, { type: 'tableHeader', attrs: { colspan: 1 }, content: [p('B')] }] },
      { type: 'tableRow', content: [{ type: 'tableCell', attrs: { rowspan: 2 }, content: [p('C')] }, cell('tableCell', 'D'), cell('tableCell', 'E')] },
      { type: 'tableRow', content: [cell('tableCell', 'F')] },
    ] }] })

    expect(contract.content).toEqual([{ type: 'table', content: [
      { type: 'tableRow', content: [cell('tableHeader', 'A'), cell('tableHeader'), cell('tableHeader', 'B')] },
      { type: 'tableRow', content: [cell('tableCell', 'C'), cell('tableCell', 'D'), cell('tableCell', 'E')] },
      { type: 'tableRow', content: [cell(), cell('tableCell', 'F'), cell()] },
    ] }])
    expect(validateDocument(asDocument(contract.content), 'page')).toEqual({ valid: true })
  })

  it('makes a header row whole, keeps header cells out of later rows, and makes cells hold paragraphs', () => {
    const contract = toContractRichText({ type: 'doc', content: [{ type: 'table', content: [
      { type: 'tableRow', content: [cell('tableHeader', 'A'), cell('tableCell', 'B')] },
      { type: 'tableRow', content: [cell('tableHeader', 'C'), { type: 'tableCell', content: [{ type: 'bulletList', content: [{ type: 'listItem', content: [p('item')] }] }] }] },
    ] }] })

    expect(contract.content).toEqual([{ type: 'table', content: [
      { type: 'tableRow', content: [cell('tableHeader', 'A'), cell('tableHeader', 'B')] },
      { type: 'tableRow', content: [cell('tableCell', 'C'), cell('tableCell', 'item')] },
    ] }])
  })

  it('turns a table inside a list or quote into its cells\' paragraphs', () => {
    const contract = toContractRichText({ type: 'doc', content: [{ type: 'blockquote', content: [grid(1, 2)] }] })

    expect(contract.content).toEqual([{ type: 'blockquote', content: [p(), p()] }])
  })

  it('keeps at most 200 rows and 20 columns', () => {
    const contract = toContractRichText({ type: 'doc', content: [grid(205, 25)] })

    const table = contract.content[0] as { content: { content: unknown[] }[] }
    expect(table.content).toHaveLength(200)
    expect(table.content[0]!.content).toHaveLength(20)
  })
})
