// Shared set-up for the collection screens' tests (014): translations, a scripted server and
// the accessibility check every screen must pass (SC-007).

import { vi } from 'vitest'
import { createI18n } from 'vue-i18n'
import axe from 'axe-core'
import en from '../../src/i18n/en.json'
import type { Collection, SchemaDocument } from '../../src/api/cms'

export { en }

export const SITE = '55555555-5555-5555-5555-555555555555'
export const COLLECTION = '66666666-6666-6666-6666-666666666666'

export const i18n = createI18n({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages: { en },
  datetimeFormats: { en: { short: { year: 'numeric', month: 'short', day: 'numeric' } } },
})

export type Reply = { status: number; body?: unknown }
export type Call = { url: string; method: string; body?: Record<string, unknown> }

/** Replaces fetch: `reply` answers each call; every call is recorded. */
export function stubFetch(reply: (call: Call) => Reply): Call[] {
  const calls: Call[] = []

  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    const call: Call = {
      url,
      method: (init?.method ?? 'GET').toUpperCase(),
      body: init?.body ? JSON.parse(String(init.body)) as Record<string, unknown> : undefined,
    }
    calls.push(call)
    const answer = reply(call)

    return {
      ok: answer.status < 300,
      status: answer.status,
      statusText: 'x',
      json: async () => answer.body,
      text: async () => JSON.stringify(answer.body ?? {}),
    }
  }))

  return calls
}

export const problem = (status: number, errorCode: string, extra: Record<string, unknown> = {}): Reply =>
  ({ status, body: { title: 'x', status, errorCode, ...extra } })

export function collection(schema: SchemaDocument, extra: Partial<Collection> = {}): Collection {
  return {
    id: COLLECTION,
    name: 'Blog posts',
    slug: 'blog-posts',
    description: null,
    schema,
    revision: 2,
    itemCount: 300,
    canManage: true,
    createdAtUtc: '2026-10-07T08:00:00Z',
    updatedAtUtc: '2026-10-07T08:00:00Z',
    updatedBy: { id: 'u', displayName: 'Dana' },
    idMap: {},
    ...extra,
  }
}

export const blogSchema = (): SchemaDocument => ({
  schemaVersion: 1,
  titleFieldId: 'f1',
  fields: [
    { id: 'f1', key: 'title', name: 'Title', help: '', required: true, type: 'text', multiline: false, maxLength: 256 },
    { id: 'f2', key: 'summary', name: 'Summary', help: '', required: false, type: 'text', multiline: true, maxLength: 5000 },
    { id: 'f3', key: 'reading-time', name: 'Reading time', help: '', required: false, type: 'number', decimals: 0 },
  ],
})

export async function violations(container: Element): Promise<axe.Result[]> {
  const results = await axe.run(container, {
    runOnly: { type: 'rule', values: ['label', 'aria-valid-attr-value', 'aria-required-attr', 'button-name', 'heading-order', 'aria-allowed-attr', 'list', 'listitem'] },
  })
  return results.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical')
}
