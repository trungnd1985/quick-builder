// What the editor knows about a document's bindings (016 research TB-14): whether the page is a
// template and of which collection, the site's collections, their schemas, and the template's
// sample item with the items it refers to. The canvas resolves bindings against it.

import { computed, ref, shallowRef } from 'vue'
import { cmsApi, type ListPreview } from '../../api/cms'
import { itemsApi, type Item } from '../../api/items'
import { pagesApi } from '../../api/pages'
import type { Json } from '../schema/subsetValidator'
import { parseSchema, type SchemaInfo } from './bindingRules'
import type { BoundItem, ResolveData } from './resolve'

/** Every media id exists, as far as the canvas knows: a missing file shows the image block's own placeholder. */
const ANY_MEDIA: Record<string, string> = new Proxy({}, { has: () => true, get: () => '' })

const sampleKey = (siteId: string, pageId: string) => `qb.sample.${siteId}.${pageId}`

/** The sample item chosen for a template page, kept per page in this browser (TB-14). */
export function rememberedSample(siteId: string, pageId: string): string | null {
  try {
    return localStorage.getItem(sampleKey(siteId, pageId))
  } catch {
    return null
  }
}

function rememberSample(siteId: string, pageId: string, itemId: string | null): void {
  try {
    if (itemId) localStorage.setItem(sampleKey(siteId, pageId), itemId)
    else localStorage.removeItem(sampleKey(siteId, pageId))
  } catch {
    // Storage may be unavailable (private windows); the choice then lasts for this visit.
  }
}

/** A draft item as the canvas binds to it: its draft values, marked by the caller as a draft. */
export function toBoundItem(item: Item): BoundItem {
  return {
    id: item.id,
    collectionId: item.collectionId,
    slug: item.slug,
    name: item.name ?? null,
    firstPublishedAt: item.publishedAtUtc ?? item.createdAtUtc,
    lastPublishedAt: item.publishedAtUtc ?? item.updatedAtUtc,
    values: item.draft as unknown as Json,
  }
}

export function useBindingContext() {
  const siteId = ref('')
  const pageId = ref('')
  const language = ref('en')
  const templateCollectionId = ref<string | null>(null)
  /** The site's collection ids, for rule 14; undefined until known (the server checks regardless). */
  const collectionIds = shallowRef<ReadonlySet<string> | undefined>(undefined)
  const schemas = shallowRef<Record<string, SchemaInfo>>({})
  const sampleItem = shallowRef<BoundItem | null>(null)
  const referencedItems = shallowRef<Record<string, BoundItem>>({})
  /** Items that list previews returned through a reference, for cards' one-step bindings (US4). */
  const previewItems = shallowRef<Record<string, BoundItem>>({})
  /**
   * List previews by request, for this document only: opening a document starts afresh, so a
   * list never shows items from before an edit made elsewhere.
   */
  const previews = new Map<string, Promise<ListPreview>>()
  /** Live template patterns by collection, learnt from list previews' item addresses (T070). */
  const templates = shallowRef<Record<string, string>>({})

  const data = computed<ResolveData>(() => ({
    pageItem: sampleItem.value,
    items: { ...previewItems.value, ...referencedItems.value },
    schemas: schemas.value,
    templates: templates.value,
    media: ANY_MEDIA,
    language: language.value,
    basePath: '',
  }))

  /** Loads a collection's schema once. */
  async function schemaOf(collectionId: string): Promise<SchemaInfo | null> {
    if (schemas.value[collectionId]) return schemas.value[collectionId]
    try {
      const collection = await cmsApi.get(siteId.value, collectionId)
      const info = parseSchema(collectionId, collection.schema as unknown as Json)
      schemas.value = { ...schemas.value, [collectionId]: info }
      return info
    } catch {
      return null
    }
  }

  /** Opens the context for a document: a page's template state and the site's collections. */
  async function load(site: string, kind: 'page' | 'component', id: string, siteLanguage: string): Promise<void> {
    siteId.value = site
    pageId.value = id
    language.value = siteLanguage
    templateCollectionId.value = null
    schemas.value = {}
    sampleItem.value = null
    referencedItems.value = {}
    previewItems.value = {}
    templates.value = {}
    previews.clear()
    collectionIds.value = undefined

    const [page, list] = await Promise.all([
      kind === 'page' ? pagesApi.get(site, id).catch(() => null) : Promise.resolve(null),
      cmsApi.list(site).catch(() => null),
    ])
    if (list) collectionIds.value = new Set(list.collections.map((c) => c.id))
    templateCollectionId.value = page?.templateCollectionId ?? null

    // FR-022: a new list takes the first collection; its card shows each item's name field.
    if (list?.collections[0]) await schemaOf(list.collections[0].id)

    if (templateCollectionId.value) {
      await schemaOf(templateCollectionId.value)
      const remembered = rememberedSample(site, id)
      if (remembered) await chooseSample(remembered)
    }
  }

  /** Shows the template with an item's draft values; null shows it with none (FR-020). */
  async function chooseSample(itemId: string | null): Promise<boolean> {
    const collectionId = templateCollectionId.value
    if (!collectionId || !itemId) {
      sampleItem.value = null
      referencedItems.value = {}
      rememberSample(siteId.value, pageId.value, null)
      return itemId === null
    }

    let item: Item
    try {
      item = await itemsApi.get(siteId.value, collectionId, itemId)
    } catch {
      // The item is gone (spec Edge Cases): the template shows with empty values.
      sampleItem.value = null
      rememberSample(siteId.value, pageId.value, null)
      return false
    }

    sampleItem.value = toBoundItem(item)
    rememberSample(siteId.value, pageId.value, itemId)
    await loadReferences(sampleItem.value)
    return true
  }

  /** The items the sample item refers to through single references, for one-step bindings. */
  async function loadReferences(item: BoundItem): Promise<void> {
    const schema = await schemaOf(item.collectionId)
    if (!schema) return
    const values = (item.values as { values?: Record<string, Json> }).values ?? {}
    const found: Record<string, BoundItem> = {}
    await Promise.all(Object.values(schema.fields)
      .filter((f) => f.type === 'reference' && f.target && typeof values[f.id] === 'string')
      .map(async (f) => {
        const target = values[f.id] as string
        await schemaOf(f.target!)
        try {
          found[target] = toBoundItem(await itemsApi.get(siteId.value, f.target!, target))
        } catch {
          // A reference to an item that is gone reads as empty.
        }
      }))
    referencedItems.value = found
  }

  /** A loaded collection's name field, null when it has none or its schema is not loaded (FR-022). */
  function nameFieldOf(collectionId: string): string | null {
    return schemas.value[collectionId]?.titleFieldId ?? null
  }

  /** What a list preview brought: the schemas of its collections and the items its cards refer to. */
  function addPreview(
    previewSchemas: Readonly<Record<string, Json>>,
    referenced: Readonly<Record<string, BoundItem>>,
    patterns: Readonly<Record<string, string>> = {},
  ): void {
    if (Object.entries(patterns).some(([id, pattern]) => templates.value[id] !== pattern)) {
      templates.value = { ...templates.value, ...patterns }
    }
    const missing = Object.entries(previewSchemas).filter(([id]) => !schemas.value[id])
    if (missing.length > 0) {
      schemas.value = { ...schemas.value, ...Object.fromEntries(missing.map(([id, schema]) => [id, parseSchema(id, schema)])) }
    }
    if (Object.keys(referenced).some((id) => !previewItems.value[id])) {
      previewItems.value = { ...previewItems.value, ...referenced }
    }
  }

  return {
    nameFieldOf,
    templateCollectionId,
    collectionIds,
    schemas,
    sampleItem,
    data,
    load,
    chooseSample,
    schemaOf,
    addPreview,
    previews,
    siteId,
  }
}
