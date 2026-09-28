// Test harness for the editor: a scripted API behind fetch, and a mounted EditorView.
import { expect, vi } from 'vitest'
import { render, waitFor } from '@testing-library/vue'
import { createPinia, setActivePinia, type Pinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import { createMemoryHistory, createRouter } from 'vue-router'
import EditorView from '../../src/editor/EditorView.vue'
import en from '../../src/i18n/en.json'
import { defaultTokens } from '../../src/editor/tokens/defaultTokens'

export const SITE = '33333333-3333-3333-3333-333333333333'
export const PAGE = '44444444-4444-4444-4444-444444444444'
export const COMPONENT = '55555555-5555-5555-5555-555555555555'

export type Json = unknown
export type Reply = { status: number; body?: Json } | (() => Promise<never>)
export interface Call {
  url: string
  method: string
  body?: { document?: Json; version?: number } & Record<string, unknown>
}

export const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: { en } })

export const emptyDocument = () => ({ schemaVersion: 1, root: { id: 'root', type: 'root', props: {}, children: [] } })

/** A root holding the given nodes. */
export const documentWith = (...children: Json[]) => ({ schemaVersion: 1, root: { id: 'root', type: 'root', props: {}, children } })

export interface Server {
  calls: Call[]
  /** Replies queued for `METHOD /path`, used before the defaults. */
  script: (key: string, ...replies: Reply[]) => void
  /** The document the fake server holds for the page now. */
  stored: () => Json
  version: () => number
}

/**
 * Serves one page (or component) draft, the site's tokens and the page itself. A save stores
 * the document and bumps the version, as the server does.
 */
export function stubServer(initial: Json = emptyDocument(), options: { version?: number; title?: string; components?: Json[]; usage?: number } = {}): Server {
  const calls: Call[] = []
  const queued = new Map<string, Reply[]>()
  let stored = initial
  let version = options.version ?? 7

  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string, init?: RequestInit) => {
      const method = (init?.method ?? 'GET').toUpperCase()
      const body = init?.body ? JSON.parse(String(init.body)) : undefined
      calls.push({ url, method, body })
      const path = url.split('?')[0]

      const scripted = queued.get(`${method} ${path}`)?.shift()
      let reply: { status: number; body?: Json }
      if (typeof scripted === 'function') {
        return scripted()
      } else if (scripted) {
        reply = scripted
      } else if (path.endsWith('/design-tokens')) {
        reply = { status: 200, body: { siteId: SITE, version: 0, isDefault: true, document: defaultTokens, updatedAtUtc: null, updatedBy: null, permissions: { canManage: true } } }
      } else if (path.endsWith('/draft') && method === 'GET') {
        reply = { status: 200, body: { schemaVersion: 1, version, updatedAtUtc: '2026-09-28T08:00:00Z', updatedBy: { id: 'u', displayName: 'Dana' }, document: stored } }
      } else if (path.endsWith('/draft') && method === 'PUT') {
        stored = body.document
        version += 1
        reply = { status: 200, body: { schemaVersion: 1, version, updatedAtUtc: '2026-09-28T08:01:00Z', updatedBy: { id: 'u', displayName: 'Dana' }, document: stored } }
      } else if (path.endsWith('/components')) {
        reply = { status: 200, body: { components: options.components ?? [], permissions: { canManageStructure: true, canEditPageDrafts: true, canManageComponents: true } } }
      } else if (path.includes('/components/')) {
        reply = { status: 200, body: { id: COMPONENT, name: options.title ?? 'Header', usageCount: 0, version: 1 } }
      } else if (path.includes('/token-usages/')) {
        reply = { status: 200, body: { tokenId: path.split('/').at(-1), blockCount: options.usage ?? 0, documentCount: options.usage ? 1 : 0 } }
      } else if (path.endsWith('/pages') && method === 'GET') {
        reply = { status: 200, body: { pages: [{ id: PAGE, parentId: null, title: 'About', path: '/about', slug: 'about', depth: 1, position: 0, isHome: false, childCount: 0, version: 1 }], permissions: { canManageStructure: true, canEditPageDrafts: true, canManageComponents: true } } }
      } else if (path.includes('/pages/')) {
        reply = { status: 200, body: { id: PAGE, title: options.title ?? 'About', slug: 'about', path: '/about' } }
      } else {
        reply = { status: 404, body: { title: 'Not found', status: 404, errorCode: 'pages.not_found' } }
      }

      return {
        ok: reply.status < 300,
        status: reply.status,
        statusText: 'x',
        headers: { get: () => null },
        json: async () => reply.body,
        text: async () => JSON.stringify(reply.body ?? {}),
      }
    }),
  )

  return {
    calls,
    script: (key, ...replies) => queued.set(key, [...(queued.get(key) ?? []), ...replies]),
    stored: () => stored,
    version: () => version,
  }
}

export interface Mounted {
  container: Element
  pinia: Pinia
  editorRoot: () => HTMLElement
}

/** Mounts the editor on the page (or component) and waits until it has opened. */
export async function mountEditor(kind: 'page' | 'component' = 'page', existing?: Pinia): Promise<Mounted> {
  const pinia = existing ?? createPinia()
  setActivePinia(pinia)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/sites/:siteId/pages', name: 'site-pages', component: { template: '<div />' } },
      { path: '/sites/:siteId/components', name: 'site-components', component: { template: '<div />' } },
      { path: '/sites/:siteId/pages/:pageId/edit', name: 'page-editor', component: EditorView },
      { path: '/sites/:siteId/components/:componentId/edit', name: 'component-editor', component: EditorView },
    ],
  })
  await router.push(kind === 'page' ? `/sites/${SITE}/pages/${PAGE}/edit` : `/sites/${SITE}/components/${COMPONENT}/edit`)
  await router.isReady()

  const result = render(EditorView, {
    props: { siteId: SITE, kind, documentId: kind === 'page' ? PAGE : COMPONENT },
    global: { plugins: [i18n, router, pinia] },
  })
  await waitFor(() => expect(result.container.querySelector('.editor-body, .editor-message[role="alert"]')).not.toBeNull())

  return { container: result.container, pinia, editorRoot: () => result.container.querySelector<HTMLElement>('.editor-canvas-scroll')! }
}

/** Presses a key on the editor, as if the canvas had the focus. */
export function press(target: Element, key: string, modifiers: { ctrl?: boolean; alt?: boolean; shift?: boolean } = {}): void {
  target.dispatchEvent(
    new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true, ctrlKey: modifiers.ctrl, altKey: modifiers.alt, shiftKey: modifiers.shift }),
  )
}

/** A drag event with no DataTransfer, as jsdom gives; the editor keeps its own drag state. */
export function drag(target: Element, type: 'dragstart' | 'dragover' | 'drop' | 'dragend'): void {
  target.dispatchEvent(new Event(type, { bubbles: true, cancelable: true }))
}

