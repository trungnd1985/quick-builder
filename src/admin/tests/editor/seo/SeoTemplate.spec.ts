import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/vue'
import axe from 'axe-core'
import en from '../../../src/i18n/en.json'
import { useEditorStore } from '../../../src/editor/state/editorStore'
import { documentWith, mountEditor, PAGE, SITE, stubServer, type Server } from '../harness'

// 017 US4 (tasks T068, T073): on a template, each metadata field binds to the item shown with
// the 016 binding control, offering only the fields that fit; a bound text field takes the text
// to use when an item has none; the hide switch binds to a boolean field; a broken binding says
// so; and the previews show the sample item's values.

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

const fieldSelect = (dialog: HTMLElement, label: string) => within(dialog).findByLabelText<HTMLSelectElement>(en.editor.binding.field.replace('{prop}', label))
const optionLabels = (select: HTMLSelectElement) => [...select.querySelectorAll('option')].map((o) => o.textContent?.trim())
const seo = () => (useEditorStore().root?.props as Record<string, unknown>).seo as Record<string, unknown> | undefined

describe('Search and sharing on a template', () => {
  beforeEach(() => localStorage.setItem(`qb.sample.${SITE}.${PAGE}`, ITEM))
  afterEach(() => {
    vi.unstubAllGlobals()
    localStorage.clear()
    document.body.innerHTML = ''
  })

  it('binds the meta title to a field that fits, then asks for the text to use when an item has none', async () => {
    const dialog = await openPanel()
    const select = await fieldSelect(dialog, en.editor.seo.metaTitle)

    expect(optionLabels(select)).toEqual(expect.arrayContaining(['Title', 'Body', 'Hidden', 'Slug']))
    expect(optionLabels(select)).not.toContain('Cover')

    const title = [...select.querySelectorAll('option')].find((o) => o.textContent?.trim() === 'Title')!
    await fireEvent.update(select, title.value)

    expect(seo()).toEqual({ bind: { title: { value: { field: 'f1' } } } })
    const fallback = await within(dialog).findByLabelText(en.editor.seo.whenNone.replace('{field}', en.editor.seo.metaTitle))
    await fireEvent.update(fallback, 'A post')
    expect(seo()?.fallback).toEqual({ title: 'A post' })
  })

  it('previews the sample item’s values', async () => {
    const dialog = await openPanel({ bind: { title: { value: { parts: [{ field: 'f1' }, ' | Blog'] } } } })

    await waitFor(() => expect(dialog.textContent).toContain('Hello world | Blog'))
  })

  it('binds the hide switch to boolean fields only', async () => {
    const dialog = await openPanel()
    const select = await fieldSelect(dialog, en.editor.seo.hideItem)

    const labels = optionLabels(select).filter((l) => l !== en.editor.binding.notBound)
    expect(labels).toEqual(['Hidden'])

    await fireEvent.update(select, [...select.querySelectorAll('option')].find((o) => o.textContent?.trim() === 'Hidden')!.value)
    expect(seo()).toEqual({ bind: { noindex: { value: { field: 'f4' } } } })
  })

  it('marks a binding to a field that is gone as broken', async () => {
    const dialog = await openPanel({ bind: { description: { value: { field: 'f99' } } } })

    expect((await within(dialog).findByText(en.editor.binding.broken.field_missing)).getAttribute('role')).toBe('note')
  })

  it('has no axe violations', async () => {
    const dialog = await openPanel({ bind: { title: { value: { field: 'f1' } } } })
    await fieldSelect(dialog, en.editor.seo.metaTitle)

    const results = await axe.run(dialog, { rules: { region: { enabled: false } } })
    expect(results.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target).join(' ')}`)).toEqual([])
  })
})
