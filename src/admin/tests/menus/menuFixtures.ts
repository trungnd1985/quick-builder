// Shared test data and a scripted fetch for the menu screens (024).
import { vi } from 'vitest'
import type { Menu, MenuDocument, MenuList, MenuPermissions } from '../../src/api/menus'

export const SITE = '44444444-4444-4444-4444-444444444444'
export const MENU = '55555555-5555-5555-5555-555555555555'
export const PAGE_HOME = '0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e10'
export const PAGE_ABOUT = '0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e11'
export const COLLECTION = '0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e20'
export const ITEM = '0192f5a0-1b2c-7d3e-8f40-5a6b7c8d9e21'

export const designer: MenuPermissions = { canManage: true, canPublish: true }
export const editor: MenuPermissions = { canManage: false, canPublish: false }

const draftPublication = { state: 'draft', liveNumber: null, liveSinceUtc: null, liveBy: null, hasUnpublishedChanges: false, liveUrl: null, pendingSchedules: [], failedSchedule: null } as const

export function menu(document: MenuDocument, permissions: MenuPermissions = designer, overrides: Partial<Menu> = {}): Menu {
  return {
    id: MENU,
    name: 'Main',
    entryCount: 0,
    brokenCount: 0,
    usedBy: 0,
    version: 7,
    draftVersion: 9,
    createdAtUtc: '2026-10-14T08:00:00Z',
    updatedAtUtc: '2026-10-14T08:00:00Z',
    publication: { ...draftPublication, pendingSchedules: [] },
    draft: document,
    targets: [],
    permissions,
    ...overrides,
  }
}

export function list(items: Menu[], permissions: MenuPermissions = designer): MenuList {
  return { items, limit: 50, permissions }
}

export const pageTree = {
  pages: [
    { id: PAGE_HOME, parentId: null, position: 0, depth: 1, title: 'Home', slug: 'home', path: '/', isHome: true, childCount: 0, version: 1, publication: draftPublication, templateCollectionId: null },
    { id: PAGE_ABOUT, parentId: null, position: 1, depth: 1, title: 'About', slug: 'about', path: '/about', isHome: false, childCount: 0, version: 1, publication: draftPublication, templateCollectionId: null },
    { id: 'tpl', parentId: PAGE_ABOUT, position: 0, depth: 2, title: 'Post template', slug: 'post', path: '/about/{slug}', isHome: false, childCount: 0, version: 1, publication: draftPublication, templateCollectionId: COLLECTION },
  ],
  permissions: { canManageStructure: true, canEditPageDrafts: true, canManageComponents: true, canPublish: true, canViewPublishLog: false },
}

export type Reply = { status: number; body?: unknown }
export type Call = { url: string; method: string; body?: unknown }

/** Scripted replies by `METHOD path`, else a default from <paramref name="fallback"/>. */
export function stubFetch(fallback: (method: string, url: string, body: unknown) => Reply, replies: Record<string, Reply[]> = {}): Call[] {
  const calls: Call[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    const method = (init?.method ?? 'GET').toUpperCase()
    const body = init?.body ? JSON.parse(String(init.body)) : undefined
    calls.push({ url, method, body })
    const reply = replies[`${method} ${url.split('?')[0]}`]?.shift() ?? fallback(method, url, body)
    return {
      ok: reply.status < 300,
      status: reply.status,
      statusText: 'x',
      json: async () => reply.body,
      text: async () => JSON.stringify(reply.body ?? {}),
    }
  }))
  return calls
}

export const problem = (status: number, errorCode: string, extra: Record<string, unknown> = {}): Reply => ({
  status,
  body: { type: 'x', title: 'x', status, errorCode, ...extra },
})
