import { afterEach, describe, expect, it } from 'vitest'
import { Editor } from '@tiptap/core'
import { richTextExtensions } from '../../src/editor/richtext/tiptap'
import { fromContractRichText, toContractRichText } from '../../src/editor/richtext/toContractRichText'
import { validateDocument } from '../../src/editor/schema/documentValidator'

// 011 research ED-03, ED-06, FR-013, FR-014: whatever TipTap produces — including pasted web
// content — is normalised to the contract's narrowed rich text, and always validates.

const asDocument = (content: unknown) => ({
  schemaVersion: 1,
  root: { id: 'root', type: 'root', props: {}, children: [{ id: 's', type: 'section', props: {}, children: [{ id: 'r', type: 'rich-text', props: { content }, children: [] }] }] },
})

let editor: Editor | null = null

describe('toContractRichText', () => {
  afterEach(() => {
    editor?.destroy()
    editor = null
  })

  it('drops null attributes, empty marks and unknown nodes and marks', () => {
    const tiptap = {
      type: 'doc',
      content: [
        { type: 'paragraph', attrs: { textAlign: null }, content: [{ type: 'text', text: 'Hi', marks: [] }] },
        { type: 'heading', attrs: { level: 1 }, content: [{ type: 'text', text: 'Lost heading' }] },
        { type: 'paragraph', content: [{ type: 'text', text: 'u', marks: [{ type: 'underline' }, { type: 'bold' }] }, { type: 'image', attrs: { src: 'x' } }] },
        { type: 'orderedList', attrs: { start: 1, type: null }, content: [{ type: 'listItem', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'one' }] }] }] },
        { type: 'paragraph' },
      ],
    }

    const contract = toContractRichText(tiptap)

    expect(contract).toEqual({
      type: 'doc',
      content: [
        { type: 'paragraph', content: [{ type: 'text', text: 'Hi' }] },
        { type: 'heading', attrs: { level: 2 }, content: [{ type: 'text', text: 'Lost heading' }] },
        { type: 'paragraph', content: [{ type: 'text', text: 'u', marks: [{ type: 'underline' }, { type: 'bold' }] }] },
        { type: 'orderedList', content: [{ type: 'listItem', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'one' }] }] }] },
        { type: 'paragraph' },
      ],
    })
    expect(validateDocument(asDocument(contract), 'page')).toEqual({ valid: true })
  })

  it('keeps marks in their stored order and drops repeats (035)', () => {
    const doc = toContractRichText({
      type: 'doc',
      content: [{ type: 'paragraph', content: [{ type: 'text', text: 'x', marks: [{ type: 'italic' }, { type: 'bold' }, { type: 'italic' }] }] }],
    })

    expect(doc.content[0]).toEqual({ type: 'paragraph', content: [{ type: 'text', text: 'x', marks: [{ type: 'italic' }, { type: 'bold' }] }] })
  })

  it('keeps a numbered list start other than 1', () => {
    const contract = toContractRichText({ type: 'doc', content: [{ type: 'orderedList', attrs: { start: 4 }, content: [{ type: 'listItem', content: [{ type: 'paragraph' }] }] }] })

    expect(contract.content[0]).toMatchObject({ type: 'orderedList', attrs: { start: 4 } })
  })

  it('maps links to the contract link, and drops a link without one', () => {
    const contract = toContractRichText({
      type: 'doc',
      content: [{
        type: 'paragraph',
        content: [
          { type: 'text', text: 'page', marks: [{ type: 'link', attrs: { to: { type: 'page', pageId: '0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e20' }, href: null, target: null } }] },
          { type: 'text', text: 'site', marks: [{ type: 'link', attrs: { to: { type: 'url', href: 'https://example.com' } } }] },
          { type: 'text', text: 'raw', marks: [{ type: 'link', attrs: { href: 'javascript:alert(1)' } }] },
        ],
      }],
    })

    const texts = (contract.content[0] as { content: { marks?: unknown[] }[] }).content
    expect(texts[0].marks).toEqual([{ type: 'link', attrs: { to: { type: 'page', pageId: '0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e20' } } }])
    expect(texts[1].marks).toEqual([{ type: 'link', attrs: { to: { type: 'url', href: 'https://example.com' } } }])
    expect(texts[2].marks).toBeUndefined()
    expect(validateDocument(asDocument(contract), 'page')).toEqual({ valid: true })
  })

  it('round-trips the contract through TipTap unchanged', () => {
    const contract = {
      type: 'doc',
      content: [
        { type: 'paragraph', content: [{ type: 'text', text: 'Bold', marks: [{ type: 'bold' }] }, { type: 'hardBreak' }, { type: 'text', text: 'next' }] },
        { type: 'bulletList', content: [{ type: 'listItem', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'a', marks: [{ type: 'link', attrs: { to: { type: 'url', href: 'https://example.com' } } }] }] }] }] },
      ],
    }

    editor = new Editor({ extensions: richTextExtensions(), content: fromContractRichText(contract) })

    expect(toContractRichText(editor.getJSON())).toEqual(contract)
  })

  it('keeps only the allowed formats of pasted web content (FR-013 US2 #4)', () => {
    editor = new Editor({
      extensions: richTextExtensions(),
      content: '<h1>Title</h1><script>alert(1)</script><style>p{}</style><p><u>under</u> <strong>strong</strong> <img src="https://example.com/x.png"> <a href="https://example.com">link</a></p><table><tr><td>cell</td></tr></table>',
    })

    const contract = toContractRichText(editor.getJSON())
    const text = JSON.stringify(contract)

    expect(text).not.toContain('script')
    expect(text).not.toContain('alert')
    expect(text).not.toContain('img')
    expect(text).toContain('underline')
    expect(text).toContain('strong')
    expect(validateDocument(asDocument(contract), 'page')).toEqual({ valid: true })
  })
})

describe('toContractRichText: headings and quotes (035 US1)', () => {
  const txt = (text: string) => ({ type: 'text', text })
  const para = (text: string) => ({ type: 'paragraph', content: [txt(text)] })

  it('keeps levels 2 to 4 and maps the rest to the nearest', () => {
    const contract = toContractRichText({
      type: 'doc',
      content: [1, 2, 3, 4, 5, 6, undefined].map((level) => ({ type: 'heading', attrs: { level, id: 'x' }, content: [txt(String(level))] })),
    })

    expect(contract.content.map((h) => (h as { attrs: { level: number } }).attrs.level)).toEqual([2, 2, 3, 4, 4, 4, 2])
    expect(contract.content[0]).toEqual({ type: 'heading', attrs: { level: 2 }, content: [txt('1')] })
    expect(validateDocument(asDocument(contract), 'page')).toEqual({ valid: true })
  })

  it('keeps a quote of paragraphs and lists, and an empty one', () => {
    const quote = { type: 'blockquote', content: [para('Said'), { type: 'bulletList', content: [{ type: 'listItem', content: [para('a')] }] }] }
    const contract = toContractRichText({ type: 'doc', content: [quote, { type: 'blockquote', content: [] }] })

    expect(contract.content).toEqual([quote, { type: 'blockquote', content: [{ type: 'paragraph' }] }])
    expect(validateDocument(asDocument(contract), 'page')).toEqual({ valid: true })
  })

  it('flattens a quote in a quote, and a heading or quote inside a list or quote, to paragraphs', () => {
    const contract = toContractRichText({
      type: 'doc',
      content: [
        { type: 'blockquote', content: [{ type: 'blockquote', content: [para('inner')] }, { type: 'heading', attrs: { level: 2 }, content: [txt('title')] }] },
        { type: 'bulletList', content: [{ type: 'listItem', content: [{ type: 'heading', attrs: { level: 3 }, content: [txt('h')] }, { type: 'blockquote', content: [para('q')] }] }] },
      ],
    })

    expect(contract.content).toEqual([
      { type: 'blockquote', content: [para('inner'), para('title')] },
      { type: 'bulletList', content: [{ type: 'listItem', content: [para('h'), para('q')] }] },
    ])
    expect(validateDocument(asDocument(contract), 'page')).toEqual({ valid: true })
  })

  it('round-trips headings and quotes through TipTap unchanged', () => {
    const contract = {
      type: 'doc',
      content: [
        { type: 'heading', attrs: { level: 3 }, content: [txt('Title')] },
        { type: 'blockquote', content: [para('Quoted'), { type: 'orderedList', content: [{ type: 'listItem', content: [para('one')] }] }] },
      ],
    }

    editor = new Editor({ extensions: richTextExtensions(), content: fromContractRichText(contract) })

    expect(toContractRichText(editor.getJSON())).toEqual(contract)
  })

  it('reads pasted h1, h5 and h6 as headings 2 and 4', () => {
    editor = new Editor({ extensions: richTextExtensions(), content: '<h1>One</h1><h3>Three</h3><h5>Five</h5><h6>Six</h6><blockquote><p>Q</p></blockquote>' })

    expect(toContractRichText(editor.getJSON()).content).toEqual([
      { type: 'heading', attrs: { level: 2 }, content: [txt('One')] },
      { type: 'heading', attrs: { level: 3 }, content: [txt('Three')] },
      { type: 'heading', attrs: { level: 4 }, content: [txt('Five')] },
      { type: 'heading', attrs: { level: 4 }, content: [txt('Six')] },
      { type: 'blockquote', content: [para('Q')] },
    ])
  })
})

describe('toContractRichText: inline styles (035 US2)', () => {
  it('keeps underline, strikethrough and code, drops marks it does not know, and keeps at most six', () => {
    const marks = ['underline', 'strike', 'code', 'highlight', 'bold', 'italic', 'textStyle'].map((type) => ({ type }))
    const contract = toContractRichText({ type: 'doc', content: [{ type: 'paragraph', content: [{ type: 'text', text: 'x', marks }] }] })

    expect(contract.content[0]).toEqual({
      type: 'paragraph',
      content: [{ type: 'text', text: 'x', marks: [{ type: 'underline' }, { type: 'strike' }, { type: 'code' }, { type: 'bold' }, { type: 'italic' }] }],
    })
    expect(validateDocument(asDocument(contract), 'page')).toEqual({ valid: true })
  })

  it('reads pasted u, s, del and code as the contract marks', () => {
    editor = new Editor({ extensions: richTextExtensions(), content: '<p><u>u</u><s>s</s><del>d</del><code>c</code></p>' })

    expect(toContractRichText(editor.getJSON()).content[0]).toEqual({
      type: 'paragraph',
      content: [
        { type: 'text', text: 'u', marks: [{ type: 'underline' }] },
        { type: 'text', text: 'sd', marks: [{ type: 'strike' }] },
        { type: 'text', text: 'c', marks: [{ type: 'code' }] },
      ],
    })
  })
})

