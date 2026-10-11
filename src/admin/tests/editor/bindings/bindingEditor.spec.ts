import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/vue'
import axe from 'axe-core'
import en from '../../../src/i18n/en.json'
import { useEditorStore } from '../../../src/editor/state/editorStore'
import { documentWith, mountEditor, PAGE, SITE, stubServer, type Server } from '../harness'

// 016 US1, US2 (tasks T029, T042): on a template page the canvas shows a sample item's draft
// values, marked as a draft; bound text is not edited in place; the binding control offers only
// fields that fit, sets formats and the empty choice, builds text templates, unbinds, and says
// when a binding is broken.

const POSTS = '0192f5a0-0000-7000-8000-00000000c001'
const ITEM = '0192f5a1-0000-7000-8000-00000000a001'

const schema = {
  schemaVersion: 1,
  titleFieldId: 'f1',
  fields: [
    { id: 'f1', key: 'title', name: 'Title', help: '', required: true, type: 'text', multiline: false, maxLength: 256 },
    { id: 'f2', key: 'body', name: 'Body', help: '', required: false, type: 'richText' },
    { id: 'f3', key: 'reading-time', name: 'Reading time', help: '', required: false, type: 'number', decimals: 0 },
  ],
}

const item = {
  id: ITEM, collectionId: POSTS, name: 'Hello world', slug: 'hello-world', status: 'draft', attention: false, schedules: [],
  createdAtUtc: '2026-10-01T00:00:00Z', updatedAtUtc: '2026-10-02T00:00:00Z', updatedBy: { id: 'u', displayName: 'Dana' }, publishedAtUtc: null,
  revision: 1, schemaRevision: 1, draft: { schemaVersion: 1, values: { f1: 'Hello world', f3: 7 } }, published: null, publishedSlug: null,
  liveVersion: null, report: { problems: [] }, scheduledDiffersFromDraft: false, unpublishedReferences: [],
}

const heading = (bind: object) => ({ id: 'h1', type: 'heading', props: { text: 'Own heading', level: 1, bind }, children: [] })
const page = (...blocks: object[]) => ({ ...documentWith({ id: 's1', type: 'section', props: {}, children: blocks }), bindingVersion: 1 })

function serveTemplate(server: Server): void {
  const pagePath = `GET /api/v1/pages/sites/${SITE}/pages/${PAGE}`
  for (let i = 0; i < 5; i++) {
    server.script(pagePath, { status: 200, body: { id: PAGE, title: 'Post', slug: 'post', path: '/blog/{slug}', templateCollectionId: POSTS, version: 1 } })
  }
  server.script(`GET /api/v1/cms/sites/${SITE}/collections`, { status: 200, body: { collections: [{ id: POSTS, name: 'Blog posts', slug: 'blog-posts' }], canManage: true } })
  for (let i = 0; i < 5; i++) {
    server.script(`GET /api/v1/cms/sites/${SITE}/collections/${POSTS}`, { status: 200, body: { id: POSTS, name: 'Blog posts', schema } })
    server.script(`GET /api/v1/cms/sites/${SITE}/collections/${POSTS}/items/${ITEM}`, { status: 200, body: item })
  }
}

const panel = () => screen.getByRole('region', { name: en.editor.panel.title })

describe('binding on a template page', () => {
  beforeEach(() => localStorage.setItem(`qb.sample.${SITE}.${PAGE}`, ITEM))
  afterEach(() => {
    vi.unstubAllGlobals()
    localStorage.clear()
  })

  it('shows the sample item’s draft values, marked as a draft, and does not edit bound text in place', async () => {
    const server = stubServer(page(heading({ text: { value: { field: 'f1' } } })))
    serveTemplate(server)
    const { container } = await mountEditor()

    const h1 = await waitFor(() => {
      const found = container.querySelector<HTMLElement>('[data-node-id="h1"] h1')!
      expect(found.textContent).toBe('Hello world')
      return found
    })
    expect(h1.getAttribute('contenteditable')).toBe('false')
    expect(screen.getByText(en.editor.sample.draft)).toBeTruthy()
    expect(screen.getByText('Hello world', { selector: '.sample-item span' })).toBeTruthy()
  })

  it('offers only the fields that fit, sets a number format, then unbinds back to the block’s own value', async () => {
    const server = stubServer(page(heading({ text: { value: { field: 'f1' } } })))
    serveTemplate(server)
    const { container } = await mountEditor()
    const store = useEditorStore()
    store.select('h1')

    const select = await within(panel()).findByLabelText(en.editor.binding.field.replace('{prop}', en.editor.panel.props.text))
    const labels = [...select.querySelectorAll('option')].map((o) => o.textContent?.trim())
    expect(labels).toEqual(expect.arrayContaining(['Title', 'Reading time', 'Slug', 'First published', 'Last published']))
    expect(labels).not.toContain('Body')
    expect(labels).not.toContain('Item page')

    const readingTime = [...select.querySelectorAll('option')].find((o) => o.textContent?.trim() === 'Reading time')!
    await fireEvent.update(select, readingTime.value)
    await fireEvent.update(within(panel()).getByLabelText(en.editor.binding.decimals), '1')

    await waitFor(() => expect(container.querySelector('[data-node-id="h1"] h1')?.textContent).toBe('7.0'))
    expect((store.root!.children[0].children[0].props.bind as Record<string, unknown>).text)
      .toEqual({ value: { field: 'f3', format: { number: { decimals: 1 } } } })

    await fireEvent.click(within(panel()).getByRole('radio', { name: en.editor.binding.hideBlock }))
    expect((store.root!.children[0].children[0].props.bind as Record<string, { empty?: string }>).text.empty).toBe('hide')

    await fireEvent.click(within(panel()).getByRole('button', { name: en.editor.binding.unbind }))
    expect(store.root!.children[0].children[0].props.bind).toBeUndefined()
    await waitFor(() => expect(container.querySelector('[data-node-id="h1"] h1')?.textContent).toBe('Own heading'))
  })

  it('builds a text from fixed text and values, with the keyboard alone', async () => {
    const server = stubServer(page({ id: 't1', type: 'text', props: { text: '' }, children: [] }))
    serveTemplate(server)
    const { container } = await mountEditor()
    const store = useEditorStore()
    store.select('t1')

    await fireEvent.click(await within(panel()).findByRole('button', { name: en.editor.binding.useTemplate }))
    const firstPart = within(panel()).getByLabelText(en.editor.binding.template.text.replace('{n}', '1'))
    await fireEvent.update(firstPart, 'By ')
    await fireEvent.change(firstPart)
    const add = within(panel()).getByLabelText(en.editor.binding.template.chooseValue)
    const title = [...add.querySelectorAll('option')].find((o) => o.textContent?.trim() === 'Title')!
    await fireEvent.update(add, title.value)
    await fireEvent.click(within(panel()).getByRole('button', { name: en.editor.binding.template.addValue }))

    expect((store.root!.children[0].children[0].props.bind as Record<string, unknown>).text).toEqual({ value: { parts: ['By ', { field: 'f1' }] } })
    await waitFor(() => expect(container.querySelector('[data-node-id="t1"] p')?.textContent).toBe('By Hello world'))

    await fireEvent.click(within(panel()).getByRole('button', { name: en.editor.binding.template.moveUp.replace('{n}', '2') }))
    expect((store.root!.children[0].children[0].props.bind as { text: { value: { parts: unknown[] } } }).text.value.parts).toEqual([{ field: 'f1' }, 'By '])

    const results = await axe.run(panel(), {
      runOnly: { type: 'rule', values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'button-name', 'aria-allowed-attr', 'select-name'] },
    })
    expect(results.violations).toEqual([])
  })

  it('says when a binding is broken, and the page still saves', async () => {
    const server = stubServer(page(heading({ text: { value: { field: 'f99' } } })))
    serveTemplate(server)
    const { container } = await mountEditor()
    const store = useEditorStore()
    store.select('h1')

    expect(await within(panel()).findByText(en.editor.binding.broken.field_missing)).toBeTruthy()
    await waitFor(() => expect(container.querySelector('[data-node-id="h1"] h1')?.textContent).toBe('Own heading'))
    expect(container.querySelector('[data-node-id="h1"]')!.classList.contains('qb-node--broken-binding')).toBe(true)
    expect(container.querySelector('.editor-layer[data-node-id="h1"] .editor-layer__broken')?.textContent).toBe(en.editor.layers.brokenBinding)
    expect(store.readOnly).toBeNull()
  })
})
