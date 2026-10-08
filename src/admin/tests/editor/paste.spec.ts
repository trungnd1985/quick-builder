import { describe, expect, it } from 'vitest'
import { existsSync, readFileSync, readdirSync, writeFileSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { generateJSON } from '@tiptap/core'
import { preparePastedHtml } from '../../src/editor/richtext/paste'
import { richTextExtensions } from '../../src/editor/richtext/tiptap'
import { toContractRichText } from '../../src/editor/richtext/toContractRichText'
import { validateDocument } from '../../src/editor/schema/documentValidator'

// 035 US4, FR-012, FR-013, SC-004: pasted content from common sources keeps what the contract
// allows and nothing else. Each sample is the text/html a source puts on the clipboard; its
// expected result sits beside it. Set QB_UPDATE_GOLDEN=1 to rewrite the expected files, then
// review every change before committing.

const directory = resolve(process.cwd(), 'tests/editor/paste')
const samples = readdirSync(directory).filter((f) => f.endsWith('.html')).sort()

/** What a paste of this HTML stores, through the same steps as the editor's paste. */
function pasted(html: string) {
  const prepared = preparePastedHtml(html)
  const content = toContractRichText(generateJSON(prepared.html, richTextExtensions())).content
  return { imagesLeftOut: prepared.imagesLeftOut, content }
}

const asDocument = (content: unknown) => ({
  schemaVersion: 1,
  root: { id: 'root', type: 'root', props: {}, children: [{ id: 's', type: 'section', props: {}, children: [{ id: 'r', type: 'rich-text', props: { content: { type: 'doc', content } }, children: [] }] }] },
})

describe('paste samples', () => {
  it('has samples from every source the spec names', () => {
    expect(samples).toEqual(['google-docs.html', 'hostile.html', 'libreoffice.html', 'web-article.html', 'word-desktop.html', 'word-web.html'])
  })

  it.each(samples)('%s stores its expected result, valid and with nothing else', (sample) => {
    const result = pasted(readFileSync(join(directory, sample), 'utf8'))
    const expectedFile = join(directory, sample.replace(/\.html$/, '.expected.json'))

    if (process.env.QB_UPDATE_GOLDEN === '1') writeFileSync(expectedFile, JSON.stringify(result, null, 2) + '\n')

    expect(existsSync(expectedFile), `No expected result for ${sample}`).toBe(true)
    expect(result).toEqual(JSON.parse(readFileSync(expectedFile, 'utf8')))
    expect(validateDocument(asDocument(result.content), 'page')).toEqual({ valid: true })

    const stored = JSON.stringify(result.content)
    for (const forbidden of ['script', 'alert', 'style', 'class', 'onerror', 'javascript:', 'data:', 'src', 'href']) {
      expect(stored).not.toContain(forbidden)
    }
  })
})

describe('preparePastedHtml', () => {
  it('splits merged cells: each keeps its content in its first position', () => {
    const { html } = preparePastedHtml('<table><tr><th colspan="2">A</th><th>B</th></tr><tr><td rowspan="2">C</td><td>D</td><td>E</td></tr><tr><td>F</td><td>G</td></tr></table>')
    const rows = [...new DOMParser().parseFromString(html, 'text/html').querySelectorAll('tr')].map((row) => [...row.cells].map((c) => `${c.tagName}:${c.textContent}`))

    expect(rows).toEqual([['TH:A', 'TH:', 'TH:B'], ['TD:C', 'TD:D', 'TD:E'], ['TD:', 'TD:F', 'TD:G']])
    expect(html).not.toMatch(/colspan|rowspan/)
  })

  it('counts and removes images that exist only as addresses', () => {
    const { html, imagesLeftOut } = preparePastedHtml('<p>a<img src="https://example.com/x.png"><img src="data:image/png;base64,AAAA"></p>')

    expect(imagesLeftOut).toBe(2)
    expect(html).toBe('<p>a</p>')
  })

  it('never runs or loads anything it is given', () => {
    let ran = false
    ;(globalThis as Record<string, unknown>).pasteProbe = () => { ran = true }

    preparePastedHtml('<img src=x onerror="pasteProbe()"><script>pasteProbe()</script><svg onload="pasteProbe()"></svg>')

    expect(ran).toBe(false)
  })
})
