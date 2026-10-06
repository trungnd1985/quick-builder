import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/vue'
import axe from 'axe-core'
import en from '../../src/i18n/en.json'
import type { MediaFile } from '../../src/api/media'
import { useAuthStore } from '../../src/app/authStore'
import { useEditorStore } from '../../src/editor/state/editorStore'
import { locate } from '../../src/editor/ops/tree'
import { documentWith, mountEditor, stubServer, SITE, type Server } from './harness'

// 013 US4 (tasks T090): the Image block's picker is the media library in a dialog. It opens
// in the folder last used on this site, uploads into the folder shown, and brings the
// library's alternative text to a block that has none (FR-009).

const FILES = `/api/v1/media/sites/${SITE}/files`
const FOLDERS = `/api/v1/media/sites/${SITE}/folders`
const LAST_FOLDER_KEY = `qb.media.lastFolder.u1.${SITE}`

const ID: Record<string, string> = {
  a: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
  top: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb',
  new: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc',
}

/** A library image named `{name}.png`; block values hold its id, which must be a UUID. */
function file(name: string, overrides: Partial<MediaFile> = {}): MediaFile {
  const id = ID[name]!
  return {
    id,
    siteId: SITE,
    fileName: `${name}.png`,
    contentType: 'image/png',
    sizeBytes: 2048,
    uploadedAtUtc: '2026-10-01T10:00:00Z',
    uploadedBy: { id: 'u1', displayName: 'Uploader' },
    publicPath: `/_media/${id}.png`,
    available: true,
    canDelete: true,
    folderId: null,
    altText: null,
    width: 64,
    height: 64,
    contentVersion: 1,
    metadataState: 'recorded',
    replacedAtUtc: null,
    replacedBy: null,
    variants: [],
    thumbnailPath: `/thumb/${id}`,
    usage: { places: 0, draftPlaces: 0, livePlaces: 0, historyOnlyPlaces: 0, inLiveUse: false },
    canReplace: true,
    ...overrides,
  }
}

const page = (...items: MediaFile[]) => ({ status: 200, body: { items, totalCount: items.length, nextCursor: null } })
const folders = { status: 200, body: { folders: [{ id: 'f1', name: 'Team', parentId: null, fileCount: 1, createdAtUtc: '' }], topLevelFileCount: 0, revision: 1 } }

const imageBlock = (props: Record<string, unknown> = {}) =>
  documentWith({ id: 's', type: 'section', props: {}, children: [{ id: 'img', type: 'image', props, children: [] }] })

const panel = () => screen.getByRole('region', { name: en.editor.panel.title })

/** A stand-in XMLHttpRequest answering each upload with the given file. */
class FakeXhr {
  static next: MediaFile
  static urls: string[] = []
  status = 0
  statusText = ''
  responseText = ''
  withCredentials = false
  upload = { onprogress: null as ((e: { lengthComputable: boolean; loaded: number; total: number }) => void) | null }
  onload: (() => void) | null = null
  onerror: (() => void) | null = null
  onabort: (() => void) | null = null
  private url = ''
  open(_method: string, url: string): void {
    this.url = url
  }
  setRequestHeader(): void {}
  getResponseHeader(): string | null {
    return null
  }
  send(): void {
    FakeXhr.urls.push(this.url)
    setTimeout(() => {
      this.status = 201
      this.responseText = JSON.stringify(FakeXhr.next)
      this.onload?.()
    }, 0)
  }
}

async function openEditor(server: Server, lastFolder: string | null = null): Promise<{ container: Element; trigger: HTMLElement }> {
  if (lastFolder) localStorage.setItem(LAST_FOLDER_KEY, lastFolder)
  server.script(`GET ${FOLDERS}`, folders, folders, folders)
  const { container } = await mountEditor()
  useAuthStore().signedIn({ id: 'u1', displayName: 'Dana', email: 'd@example.test', isAdministrator: true, siteRoles: [] })
  useEditorStore().select('img')
  const trigger = await within(panel()).findByRole('button', { name: en.editor.panel.media.choose })
  return { container, trigger }
}

const listCalls = (server: Server) => server.calls.filter((c) => c.method === 'GET' && c.url.startsWith(FILES) && !c.url.slice(FILES.length).startsWith('/'))

describe('MediaControl (US4)', () => {
  beforeEach(() => {
    localStorage.clear()
    FakeXhr.urls = []
    vi.stubGlobal('XMLHttpRequest', FakeXhr)
  })

  afterEach(() => vi.unstubAllGlobals())

  it('opens the library as a dialog in the folder last used, keyboard-operable and accessible', async () => {
    const server = stubServer(imageBlock())
    server.script(`GET ${FILES}`, page(file('a', { folderId: 'f1' })))
    const { trigger } = await openEditor(server, 'f1')

    await fireEvent.click(trigger)
    const dialog = await screen.findByRole('dialog', { name: en.editor.panel.media.dialogTitle })
    await within(dialog).findByText('a.png')

    expect(listCalls(server).at(-1)!.url).toContain('folderId=f1')
    expect(listCalls(server).at(-1)!.url).toContain('kind=image')
    expect(dialog.contains(document.activeElement)).toBe(true)

    const results = await axe.run(dialog, {
      runOnly: { type: 'rule', values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'button-name', 'aria-dialog-name'] },
    })
    expect(results.violations.map((v) => v.id)).toEqual([])

    // Tab from the last control wraps to the first; Escape closes and focus goes back.
    const focusable = [...dialog.querySelectorAll<HTMLElement>('button:not([disabled]), input:not([disabled]), select:not([disabled])')]
    focusable.at(-1)!.focus()
    await fireEvent.keyDown(dialog, { key: 'Tab' })
    expect(document.activeElement).toBe(focusable[0])

    await fireEvent.keyDown(dialog, { key: 'Escape' })
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(document.activeElement).toBe(trigger)
  })

  it('falls back to the top level when the remembered folder no longer exists, and forgets it', async () => {
    const server = stubServer(imageBlock())
    server.script(
      `GET ${FILES}`,
      { status: 404, body: { title: 'Not found', status: 404, errorCode: 'media.folder_not_found' } },
      page(file('top')),
    )
    const { trigger } = await openEditor(server, 'gone')

    await fireEvent.click(trigger)
    await screen.findByText('top.png')

    expect(listCalls(server).at(-1)!.url).not.toContain('folderId=')
    expect(localStorage.getItem(LAST_FOLDER_KEY)).toBeNull()
  })

  it('fills an empty alternative text from the library in the same undo step', async () => {
    const server = stubServer(imageBlock())
    server.script(`GET ${FILES}`, page(file('a', { altText: 'A red bicycle' })))
    server.script(`GET ${FILES}/${ID.a}`, { status: 200, body: file('a', { altText: 'A red bicycle' }) })
    const { trigger } = await openEditor(server)
    const store = useEditorStore()

    await fireEvent.click(trigger)
    await fireEvent.click(await screen.findByRole('button', { name: 'Choose a.png' }))

    const props = locate(store.root!, 'img')!.node.props
    expect(props.media).toEqual({ mediaId: ID.a })
    expect(props.alt).toBe('A red bicycle')
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())

    await fireEvent.click(screen.getByRole('button', { name: en.editor.history.undo }))
    const undone = locate(store.root!, 'img')!.node.props
    expect(undone.media).toBeUndefined()
    expect(undone.alt).toBeUndefined()
  })

  it.each([
    ['its own alternative text', { alt: 'Written by the editor' }, 'Written by the editor'],
    ['the decorative mark', { decorative: true }, undefined],
  ])('keeps %s', async (_case, own, expected) => {
    const server = stubServer(imageBlock(own))
    server.script(`GET ${FILES}`, page(file('a', { altText: 'From the library' })))
    const { trigger } = await openEditor(server)

    await fireEvent.click(trigger)
    await fireEvent.click(await screen.findByRole('button', { name: 'Choose a.png' }))

    const props = locate(useEditorStore().root!, 'img')!.node.props
    expect(props.media).toEqual({ mediaId: ID.a })
    expect(props.alt).toBe(expected)
  })

  it('leaves the alternative text for the editor to write when the library has none', async () => {
    const server = stubServer(imageBlock())
    server.script(`GET ${FILES}`, page(file('a')))
    const { trigger } = await openEditor(server)

    await fireEvent.click(trigger)
    await fireEvent.click(await screen.findByRole('button', { name: 'Choose a.png' }))

    expect(locate(useEditorStore().root!, 'img')!.node.props.alt).toBeUndefined()
    const field = within(panel()).getByLabelText<HTMLInputElement>(en.editor.panel.props.alt)
    expect(field.value).toBe('')
  })

  it('changes nothing when closed without choosing', async () => {
    const server = stubServer(imageBlock())
    server.script(`GET ${FILES}`, page(file('a')))
    const { trigger } = await openEditor(server)
    const store = useEditorStore()
    const before = store.root

    await fireEvent.click(trigger)
    await screen.findByText('a.png')
    await fireEvent.click(screen.getByRole('button', { name: en.editor.panel.media.close }))

    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(store.root).toBe(before)
  })

  it('uploads into the folder shown and chooses the upload', async () => {
    const server = stubServer(imageBlock())
    server.script(`GET ${FILES}`, page(file('a', { folderId: 'f1' })))
    FakeXhr.next = file('new', { folderId: 'f1' })
    const { trigger } = await openEditor(server, 'f1')

    await fireEvent.click(trigger)
    const dialog = await screen.findByRole('dialog')
    await within(dialog).findByText('a.png')

    const input = dialog.querySelector('input[type=file]') as HTMLInputElement
    Object.defineProperty(input, 'files', { value: [new File([new Uint8Array([1])], 'new.png', { type: 'image/png' })], configurable: true })
    await fireEvent.change(input)

    await waitFor(() => expect(locate(useEditorStore().root!, 'img')!.node.props.media).toEqual({ mediaId: ID.new }))
    expect(FakeXhr.urls[0]).toContain('folderId=f1')
  })
})
