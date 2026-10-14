import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/vue'
import axe from 'axe-core'
import en from '../../../src/i18n/en.json'
import { useEditorStore } from '../../../src/editor/state/editorStore'
import { documentWith, mountEditor, PAGE, SITE, stubServer, type Server } from '../harness'

// 017 US7 (tasks T097, T103): on a template, the content type item pages describe themselves
// as; each property offers only the fields it accepts, shows its default, marks required
// properties and broken bindings, and the control passes axe.

const POSTS = '0192f5a0-0000-7000-8000-00000000c001'
const ITEM = '0192f5a1-0000-7000-8000-00000000a001'

const schema = {
  schemaVersion: 1,
  titleFieldId: 'f1',
  fields: [
    { id: 'f1', key: 'title', name: 'Title', help: '', required: true, type: 'text', multiline: false, maxLength: 256 },
    { id: 'f2', key: 'body', name: 'Body', help: '', required: false, type: 'richText' },
    { id: 'f4', key: 'hidden', name: 'Hidden', help: '', required: false, type: 'boolean' },
    { id: 'f6', key: 'cover', name: 'Cover', help: '', required: false, type: 'image' },
    { id: 'f7', key: 'starts', name: 'Starts', help: '', required: false, type: 'date' },
  ],
}

const item = {
  id: ITEM, collectionId: POSTS, name: 'Hello world', slug: 'hello-world', status: 'draft', attention: false, schedules: [],
  createdAtUtc: '2026-10-01T00:00:00Z', updatedAtUtc: '2026-10-02T00:00:00Z', updatedBy: { id: 'u', displayName: 'Dana' }, publishedAtUtc: null,
  revision: 1, schemaRevision: 1, draft: { schemaVersion: 1, values: { f1: 'Hello world' } }, published: null, publishedSlug: null,
  liveVersion: null, report: { problems: [] }, scheduledDiffersFromDraft: false, unpublishedReferences: [],
}

function documentWithSeo(seo?: object) {
  const document = { ...documentWith({ id: 's1', type: 'section', props: {}, children: [] }), bindingVersion: 1 }
  if (seo) document.root = { ...document.root, props: { ...document.root.props, seo } }
  return document
}

function serveTemplate(server: Server): void {
  const pagePath = `GET /api/v1/pages/sites/${SITE}/pages/${PAGE}`
  const body = {
    id: PAGE, title: 'Post', slug: 'post', path: '/blog/{slug}', templateCollectionId: POSTS, version: 1,
    permissions: { canManageStructure: true, canEditPageDrafts: true, canManageComponents: true, canPublish: false },
  }
  for (let i = 0; i < 6; i++) server.script(pagePath, { status: 200, body })
  server.script(`GET /api/v1/cms/sites/${SITE}/collections`, { status: 200, body: { collections: [{ id: POSTS, name: 'Blog posts', slug: 'blog-posts' }], canManage: true } })
  for (let i = 0; i < 6; i++) {
    server.script(`GET /api/v1/cms/sites/${SITE}/collections/${POSTS}`, { status: 200, body: { id: POSTS, name: 'Blog posts', schema } })
    server.script(`GET /api/v1/cms/sites/${SITE}/collections/${POSTS}/items/${ITEM}`, { status: 200, body: item })
  }
}

async function openPanel(seo?: object): Promise<HTMLElement> {
  const server = stubServer(documentWithSeo(seo))
  serveTemplate(server)
  await mountEditor()
  await waitFor(() => expect(useEditorStore().bindings.sampleItem?.name).toBe('Hello world'))
  await fireEvent.click(await screen.findByRole('button', { name: en.editor.seo.open }))
  return screen.findByRole('dialog', { name: en.editor.seo.title })
}

const optionLabels = (select: HTMLSelectElement) => [...select.querySelectorAll('option')].map((o) => o.textContent?.trim())
const seo = () => (useEditorStore().root?.props as Record<string, unknown>).seo as Record<string, unknown> | undefined

describe('Structured data on a template', () => {
  beforeEach(() => localStorage.setItem(`qb.sample.${SITE}.${PAGE}`, ITEM))
  afterEach(() => {
    vi.unstubAllGlobals()
    localStorage.clear()
    document.body.innerHTML = ''
  })

  const sd = en.editor.seo.structuredData

  it('chooses a content type, then offers each property only the fields it accepts', async () => {
    const dialog = await openPanel()

    await fireEvent.update(within(dialog).getByLabelText(sd.type), 'Event')
    expect(seo()).toEqual({ structuredData: { type: 'Event' } })

    const start = await within(dialog).findByLabelText<HTMLSelectElement>(`startDate (${sd.required})`)
    expect(optionLabels(start)).toEqual([sd.notBound.replace('{fallback}', sd.leftOut), 'Starts'])

    const name = within(dialog).getByLabelText<HTMLSelectElement>(`name (${sd.required})`)
    expect(optionLabels(name)[0]).toBe(sd.notBound.replace('{fallback}', sd.defaults.name))
    expect(optionLabels(name)).not.toContain('Cover')

    await fireEvent.update(start, 'field:f7')
    expect(seo()).toEqual({ structuredData: { type: 'Event', bind: { startDate: { value: { field: 'f7' } } } } })
  })

  it('marks a binding the site would leave out', async () => {
    const dialog = await openPanel({ structuredData: { type: 'Article', bind: { image: { value: { field: 'f1' } } } } })

    expect((await within(dialog).findByText(en.editor.binding.broken.type_mismatch)).getAttribute('role')).toBe('note')
  })

  it('has no axe violations', async () => {
    const dialog = await openPanel({ structuredData: { type: 'Product' } })
    await within(dialog).findByLabelText(`name (${sd.required})`)

    const results = await axe.run(dialog, { rules: { region: { enabled: false } } })
    expect(results.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target).join(' ')}`)).toEqual([])
  })
})
