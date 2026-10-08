// Shared set-up for the item screens' tests (015): the 014 harness, a collection with one field
// of the types the form renders, item fixtures, and a router with the item routes.

import { render, screen, waitFor } from '@testing-library/vue'
import { createMemoryHistory, createRouter, type RouteRecordRaw } from 'vue-router'
import { expect } from 'vitest'
import type { Component } from 'vue'
import type { SchemaDocument } from '../../../src/api/cms'
import type { Item, ItemSummary } from '../../../src/api/items'
import { collection, COLLECTION, en, i18n, SITE, stubFetch, type Call, type Reply } from '../harness'

export { COLLECTION, en, i18n, SITE, stubFetch, type Call, type Reply }
export { problem, violations } from '../harness'

export const PEOPLE = '77777777-7777-7777-7777-777777777777'
export const ITEM = '88888888-8888-8888-8888-888888888888'

export const postSchema = (): SchemaDocument => ({
  schemaVersion: 1,
  titleFieldId: 'f1',
  fields: [
    { id: 'f1', key: 'title', name: 'Title', help: 'Shown as the heading.', required: true, type: 'text', multiline: false, maxLength: 20 },
    { id: 'f2', key: 'summary', name: 'Summary', help: '', required: false, type: 'text', multiline: true, maxLength: 50 },
    { id: 'f3', key: 'reading-time', name: 'Reading time', help: '', required: false, type: 'number', decimals: 0, min: 0 },
    { id: 'f4', key: 'featured', name: 'Featured', help: '', required: false, type: 'boolean' },
    { id: 'f5', key: 'published-on', name: 'Published on', help: '', required: false, type: 'date', withTime: false },
    { id: 'f6', key: 'category', name: 'Category', help: '', required: false, type: 'option', options: [{ id: 'o1', label: 'News' }, { id: 'o2', label: 'Guides' }] },
    { id: 'f7', key: 'author', name: 'Author', help: '', required: false, type: 'reference', target: PEOPLE },
  ],
})

export const postCollection = (schema = postSchema(), schemaRevision = 2) =>
  ({ ...collection(schema), schemaRevision })

export function summary(extra: Partial<ItemSummary> = {}): ItemSummary {
  return {
    id: ITEM,
    name: 'Hello world',
    slug: 'hello-world',
    status: 'draft',
    attention: false,
    schedules: [],
    createdAtUtc: '2026-10-08T08:00:00Z',
    updatedAtUtc: '2026-10-08T09:00:00Z',
    updatedBy: { id: 'u', name: 'Dana' },
    publishedAtUtc: null,
    ...extra,
  }
}

export function item(extra: Partial<Item> = {}): Item {
  return {
    ...summary(),
    collectionId: COLLECTION,
    revision: 3,
    schemaRevision: 2,
    draft: { schemaVersion: 1, values: { f1: 'Hello world' } },
    published: null,
    publishedSlug: null,
    liveVersion: null,
    report: { problems: [] },
    scheduledDiffersFromDraft: false,
    unpublishedReferences: [],
    ...extra,
  }
}

const stub = (name: string): Component => ({ name, template: `<div>${name}</div>` })

/** Renders a component at a path with the item routes; returns the router and recorded calls. */
export async function renderAt(component: Component, path: string, reply: (call: Call) => Reply, routes: RouteRecordRaw[] = []) {
  const calls = stubFetch(reply)
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/sites/:siteId/collections/:collectionId', name: 'site-collection', component: stub('builder') },
      { path: '/sites/:siteId/collections/:collectionId/items', name: 'site-collection-items', component: stub('items') },
      { path: '/sites/:siteId/collections/:collectionId/items/new', name: 'site-collection-item-new', component: stub('new') },
      { path: '/sites/:siteId/collections/:collectionId/items/:itemId', name: 'site-collection-item', component: stub('item') },
      ...routes,
    ],
  })
  await router.push(path)
  await router.isReady()
  const result = render(component, { global: { plugins: [i18n, router] } })
  return { ...result, calls, router }
}

/** Waits until a loading message is gone. */
export const loaded = (text: string) => waitFor(() => expect(screen.queryByText(text)).toBeNull())
