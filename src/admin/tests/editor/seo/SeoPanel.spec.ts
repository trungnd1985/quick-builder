import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/vue'
import axe from 'axe-core'
import en from '../../../src/i18n/en.json'
import { useEditorStore } from '../../../src/editor/state/editorStore'
import { emptyDocument, mountEditor, PAGE, SITE, stubServer, type Server } from '../harness'

// 017 US1 (tasks T025, T032, T033): the editor's Search and sharing panel. It edits
// root.props.seo through the editor's operations, so undo and autosave cover it, warns past the
// lengths search engines show, refuses what the schema refuses, previews the listing, and keeps
// the canonical address and hiding to members who may change the page structure (SE-03).

const PAGE_URL = `/api/v1/pages/sites/${SITE}/pages/${PAGE}`

function page(canManageStructure: boolean) {
  return {
    status: 200,
    body: {
      id: PAGE,
      title: 'Services',
      slug: 'services',
      path: '/services',
      permissions: { canManageStructure, canEditPageDrafts: true, canManageComponents: canManageStructure, canPublish: false },
    },
  }
}

async function openPanel(server: Server, canManageStructure = true): Promise<HTMLElement> {
  server.script(`GET ${PAGE_URL}`, page(canManageStructure), page(canManageStructure), page(canManageStructure))
  await mountEditor('page')
  await fireEvent.click(await screen.findByRole('button', { name: en.editor.seo.open }))
  return screen.findByRole('dialog', { name: en.editor.seo.title })
}

const seo = () => (useEditorStore().root?.props as Record<string, unknown>).seo as Record<string, unknown> | undefined

afterEach(() => {
  vi.unstubAllGlobals()
  document.body.innerHTML = ''
})

describe('Search and sharing panel', () => {
  it('sets the meta title and description in the draft, and undo takes them back', async () => {
    const dialog = await openPanel(stubServer(emptyDocument()))

    await fireEvent.update(within(dialog).getByLabelText(en.editor.seo.metaTitle), 'Web design in Hanoi')
    await fireEvent.update(within(dialog).getByLabelText(en.editor.seo.metaDescription), 'Fast sites.')

    expect(seo()).toEqual({ title: 'Web design in Hanoi', description: 'Fast sites.' })

    await fireEvent.click(within(dialog).getByRole('button', { name: en.editor.seo.close }))
    await fireEvent.click(screen.getByRole('button', { name: en.editor.history.undo }))
    await fireEvent.click(screen.getByRole('button', { name: en.editor.history.undo }))

    expect(seo()).toBeUndefined()
  })

  it('warns past 60 and 160 characters without refusing', async () => {
    const dialog = await openPanel(stubServer(emptyDocument()))

    await fireEvent.update(within(dialog).getByLabelText(en.editor.seo.metaTitle), 'x'.repeat(61))
    await fireEvent.update(within(dialog).getByLabelText(en.editor.seo.metaDescription), 'y'.repeat(161))

    expect(within(dialog).getAllByRole('note').map((n) => n.textContent)).toEqual([
      expect.stringContaining('60'),
      expect.stringContaining('160'),
    ])
    expect(seo()?.title).toBe('x'.repeat(61))
  })

  it('refuses a title over the hard maximum and keeps the last valid one', async () => {
    const dialog = await openPanel(stubServer(emptyDocument()))
    const title = within(dialog).getByLabelText(en.editor.seo.metaTitle)

    await fireEvent.update(title, 'Kept')
    await fireEvent.update(title, 'z'.repeat(201))

    expect(seo()?.title).toBe('Kept')
    expect(within(dialog).getByRole('alert').textContent).toContain('200')
  })

  it('previews the search listing with the fallbacks applied', async () => {
    const dialog = await openPanel(stubServer(emptyDocument()))
    const preview = within(dialog).getByRole('region', { name: en.editor.seo.searchPreview })

    expect(preview.textContent).toContain('Services')

    await fireEvent.update(within(dialog).getByLabelText(en.editor.seo.metaTitle), 'Web design')
    await fireEvent.update(within(dialog).getByLabelText(en.editor.seo.metaDescription), 'Fast sites.')

    expect(preview.textContent).toContain('Web design')
    expect(preview.textContent).toContain('Fast sites.')
    expect(preview.textContent).toContain('/services')
  })

  it('turns hiding and leaving out of the sitemap on and off', async () => {
    const dialog = await openPanel(stubServer(emptyDocument()))

    await fireEvent.click(within(dialog).getByLabelText(en.editor.seo.noindex))
    await fireEvent.click(within(dialog).getByLabelText(en.editor.seo.noSitemap))
    expect(seo()).toEqual({ noindex: true, noSitemap: true })

    await fireEvent.click(within(dialog).getByLabelText(en.editor.seo.noindex))
    expect(seo()).toEqual({ noSitemap: true })
  })

  it('keeps hiding to members who may change the page structure, and says why', async () => {
    const dialog = await openPanel(stubServer(emptyDocument()), false)
    const hide = within(dialog).getByLabelText<HTMLInputElement>(en.editor.seo.noindex)

    expect(hide.disabled).toBe(true)
    expect(within(dialog).getByText(en.editor.seo.restricted)).toBeTruthy()
    expect(within(dialog).getByLabelText<HTMLInputElement>(en.editor.seo.metaTitle).disabled).toBe(false)
  })

  it('is reachable by keyboard, closes on Escape, and has no axe violations', async () => {
    const dialog = await openPanel(stubServer(emptyDocument()))

    await waitFor(() => expect(dialog.contains(document.activeElement)).toBe(true))
    const results = await axe.run(dialog, { rules: { region: { enabled: false } } })
    expect(results.violations.map((v) => v.id)).toEqual([])

    await fireEvent.keyDown(dialog, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog', { name: en.editor.seo.title })).toBeNull())
  })
})
