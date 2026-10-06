import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/vue'
import { createI18n } from 'vue-i18n'
import axe from 'axe-core'
import FolderTree from '../../src/features/media/FolderTree.vue'
import type { MediaFolder } from '../../src/api/media'
import en from '../../src/i18n/en.json'

// 013 US1: the folder tree — the ARIA treeview keyboard pattern, and create, rename, move and
// delete with the server's refusals shown in words (FR-001, FR-002, FR-004).

const SITE = '11111111-1111-1111-1111-111111111111'
const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

function folder(id: string, name: string, parentId: string | null = null): MediaFolder {
  return { id, name, parentId, fileCount: 0, createdAtUtc: '2026-10-01T00:00:00Z' }
}

interface Call { url: string; method: string; body?: unknown }

function stubFetch(folders: MediaFolder[], refusal?: { status: number; errorCode: string }) {
  const calls: Call[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    const method = (init?.method ?? 'GET').toUpperCase()
    calls.push({ url, method, body: init?.body ? JSON.parse(String(init.body)) : undefined })

    if (method !== 'GET' && refusal) {
      return { ok: false, status: refusal.status, statusText: 'x', text: async () => JSON.stringify({ title: 'x', status: refusal.status, errorCode: refusal.errorCode }) }
    }

    if (method === 'POST') {
      const body = JSON.parse(String(init?.body)) as { name: string; parentId: string | null }
      const created = folder('new', body.name, body.parentId)
      folders.push(created)
      return { ok: true, status: 201, json: async () => created }
    }

    if (method === 'DELETE') {
      return { ok: true, status: 204, json: async () => undefined }
    }

    if (method === 'PATCH') {
      return { ok: true, status: 200, json: async () => folders[0] }
    }

    return { ok: true, status: 200, json: async () => ({ folders, topLevelFileCount: 0, revision: 1 }) }
  }))
  return calls
}

function renderTree(selectedId: string | null = null) {
  return render(FolderTree, { props: { siteId: SITE, selectedId, canEdit: true }, global: { plugins: [i18n] } })
}

describe('FolderTree', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('is a tree operable by keyboard: arrows move, Right opens, Left closes, Enter selects', async () => {
    stubFetch([folder('team', 'Team photos'), folder('year', '2026', 'team'), folder('docs', 'Docs')])
    const { emitted } = renderTree()

    const tree = await screen.findByRole('tree', { name: en.media.folders.label })
    const top = within(tree).getByRole('treeitem', { name: en.media.folders.topLevel })
    expect(top.getAttribute('tabindex')).toBe('0')
    top.focus()

    await fireEvent.keyDown(top, { key: 'ArrowDown' })
    const docs = within(tree).getByRole('treeitem', { name: /Docs/ })
    await waitFor(() => expect(document.activeElement).toBe(docs))

    await fireEvent.keyDown(docs, { key: 'ArrowDown' })
    const team = within(tree).getByRole('treeitem', { name: /Team photos/ })
    await waitFor(() => expect(document.activeElement).toBe(team))
    expect(team.getAttribute('aria-expanded')).toBe('false')

    await fireEvent.keyDown(team, { key: 'ArrowRight' })
    await waitFor(() => expect(team.getAttribute('aria-expanded')).toBe('true'))
    const year = within(tree).getByRole('treeitem', { name: /2026/ })
    expect(year.getAttribute('aria-level')).toBe('3')

    await fireEvent.keyDown(team, { key: 'ArrowRight' })
    await waitFor(() => expect(document.activeElement).toBe(year))

    await fireEvent.keyDown(year, { key: 'ArrowLeft' })
    await waitFor(() => expect(document.activeElement).toBe(team))

    await fireEvent.keyDown(team, { key: 'End' })
    await waitFor(() => expect(document.activeElement).toBe(year))
    await fireEvent.keyDown(year, { key: 'Home' })
    await waitFor(() => expect(document.activeElement).toBe(top))

    await fireEvent.keyDown(top, { key: 'ArrowDown' })
    await fireEvent.keyDown(document.activeElement!, { key: 'Enter' })
    expect(emitted().select?.at(-1)).toEqual(['docs'])
  })

  it('creates a folder inside the selected one and announces it', async () => {
    const calls = stubFetch([folder('team', 'Team photos')])
    renderTree('team')

    await fireEvent.click(await screen.findByRole('button', { name: en.media.folders.new }))
    await fireEvent.update(screen.getByLabelText(en.media.folders.newName), '2026 Q4')
    await fireEvent.click(screen.getByRole('button', { name: en.media.folders.submit.create }))

    await waitFor(() => expect(screen.getByText('Folder 2026 Q4 created')).toBeTruthy())
    expect(calls.find((c) => c.method === 'POST')?.body).toEqual({ name: '2026 Q4', parentId: 'team' })
  })

  it('shows the server refusal in words', async () => {
    stubFetch([folder('team', 'Team photos')], { status: 409, errorCode: 'media.folder_not_empty' })
    renderTree('team')

    await fireEvent.click(await screen.findByRole('button', { name: en.media.folders.delete }))
    const dialogArea = screen.getByText(/Delete the folder/).closest('form')!
    await fireEvent.click(within(dialogArea as HTMLElement).getByRole('button', { name: en.media.folders.submit.delete }))

    await waitFor(() => expect(screen.getByRole('alert').textContent).toBe(en.media.folders.errors.folder_not_empty))
  })

  it('offers only valid move targets: never the folder itself or anything inside it', async () => {
    stubFetch([folder('a', 'A'), folder('a1', 'A1', 'a'), folder('b', 'B')])
    renderTree('a')

    await fireEvent.click(await screen.findByRole('button', { name: en.media.folders.move }))
    const options = Array.from(screen.getByRole('combobox').querySelectorAll('option')).map((o) => o.textContent)

    expect(options).toEqual([en.media.folders.topLevel, 'B'])
  })

  it('has no baseline accessibility violations', async () => {
    stubFetch([folder('team', 'Team photos')])
    const { container } = renderTree('team')
    await screen.findByRole('tree')

    const results = await axe.run(container, {
      runOnly: { type: 'rule', values: ['aria-required-children', 'aria-required-parent', 'aria-valid-attr-value', 'button-name', 'label'] },
    })
    expect(results.violations).toEqual([])
  })
})
