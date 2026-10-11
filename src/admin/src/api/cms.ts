// Typed client for the Cms module (014 contracts/cms-api.yaml).

import { request } from './request'

export { ApiError, type ProblemDetails } from './request'

export type FieldType =
  | 'text'
  | 'richText'
  | 'number'
  | 'boolean'
  | 'date'
  | 'image'
  | 'file'
  | 'option'
  | 'reference'
  | 'multiReference'

export interface FieldOption {
  id: string
  label: string
}

/** One field of a schema document (docs/contracts/collection-schema.md §2). Settings beside the common properties. */
export interface SchemaField {
  id: string
  key: string
  name: string
  help: string
  required: boolean
  type: FieldType
  multiline?: boolean
  maxLength?: number
  decimals?: number
  min?: number
  max?: number
  withTime?: boolean
  accept?: 'any' | 'documents' | 'images'
  /** Rich text: the format groups it allows; a missing one is allowed (035 FR-022). */
  formats?: { headings?: boolean; images?: boolean; tables?: boolean }
  options?: FieldOption[]
  target?: string
}

export interface SchemaDocument {
  schemaVersion: number
  titleFieldId?: string
  fields: SchemaField[]
}

export interface Person {
  id: string
  displayName: string
}

export interface CollectionSummary {
  id: string
  name: string
  slug: string
  fieldCount: number
  itemCount: number
  revision: number
  updatedAtUtc: string
}

export interface CollectionList {
  collections: CollectionSummary[]
  canManage: boolean
}

export interface Collection {
  id: string
  name: string
  slug: string
  description: string | null
  schema: SchemaDocument
  revision: number
  /** The revision at which the schema last changed; item forms send it back (015 research IT-07). */
  schemaRevision: number
  itemCount: number
  canManage: boolean
  createdAtUtc: string
  updatedAtUtc: string
  updatedBy: Person
  /** After a save: each temporary id of the proposal and the permanent id it became. */
  idMap: Record<string, string>
}

export interface CollectionDetailsInput {
  name: string
  slug: string
  description: string | null
}

export interface SaveRequest extends CollectionDetailsInput {
  revision: number
  schema: SchemaDocument
  fillIns?: Record<string, unknown>
  confirmation?: { digest: string }
}

export type ChangeRule = 'free' | 'reported' | 'lossless' | 'lossy' | 'forbidden'

export type Outcome = 'kept' | 'converted' | 'partlyLost' | 'cleared' | 'nowInvalid' | 'missing' | 'filled' | 'stillMissing'

export interface PreviewExample {
  itemId: string
  title: string | null
  outcome: Outcome
}

export interface PreviewChange {
  kind: string
  rule: ChangeRule
  fieldId: string | null
  key: string | null
  name: string | null
  setting: string | null
  optionId: string | null
  from: string | null
  to: string | null
  counts: Partial<Record<Outcome, number>>
  examples: PreviewExample[]
  fillInAllowed: boolean
}

export interface Preview {
  revision: number
  itemCount: number
  requiresConfirmation: boolean
  digest: string
  changes: PreviewChange[]
  /** 016 FR-011: pages and components reading each field the change removes or retypes. */
  boundPages?: Record<string, number>
}

/** 016: a page or component that keeps a collection from being deleted. */
export interface CollectionUse {
  kind: 'page' | 'component'
  id: string
  title: string
  states: ('draft' | 'live' | 'scheduled')[]
}

export interface HistoryChange {
  kind: string
  fieldId?: string
  key?: string
  name?: string
  from?: string | null
  to?: string | null
  setting?: string
  optionId?: string
  slugFrom?: string
  slugTo?: string
  counts?: Partial<Record<Outcome, number>>
}

export interface HistoryEntry {
  revision: number
  changedAtUtc: string
  changedBy: Person
  changes: HistoryChange[]
}

export interface SchemaHistory {
  entries: HistoryEntry[]
  nextBefore: number | null
}

/** A collection and field that refer to one being deleted (`cms.collection_referenced`). */
export interface CollectionReference {
  collectionId: string
  collectionName: string
  fieldId: string
  fieldName: string
}

/** One item of a list preview (016 contracts/templates-api.yaml, `list-preview`): published data. */
export interface ListPreviewItem {
  id: string
  collectionId: string
  slug: string
  name: string | null
  /** The published item values document. */
  values: unknown
  /** The item's page, when its collection's template is live. */
  address: string | null
  firstPublishedAt: string
  lastPublishedAt: string
}

export interface ListPreviewRequest {
  /** The collection-list props, without children. */
  list: unknown
  /** Enclosing scope items by name: the sample item for `page`, a card's item for a list id. */
  scopes?: Record<string, string>
  page?: number
  /** Reference fields whose referenced items come back too. */
  via?: string[]
}

export interface ListPreview {
  items: ListPreviewItem[]
  total: number
  referenced: Record<string, ListPreviewItem>
  /** Current schema documents by collection id. */
  schemas: Record<string, unknown>
}

const root = (siteId: string) => `/cms/sites/${encodeURIComponent(siteId)}/collections`
const one = (siteId: string, collectionId: string) => `${root(siteId)}/${encodeURIComponent(collectionId)}`

export const cmsApi = {
  list: (siteId: string): Promise<CollectionList> => request<CollectionList>(root(siteId)),

  create: (siteId: string, body: CollectionDetailsInput): Promise<Collection> =>
    request<Collection>(root(siteId), { method: 'POST', body: JSON.stringify(body) }),

  get: (siteId: string, collectionId: string): Promise<Collection> => request<Collection>(one(siteId, collectionId)),

  save: (siteId: string, collectionId: string, body: SaveRequest): Promise<Collection> =>
    request<Collection>(one(siteId, collectionId), { method: 'PUT', body: JSON.stringify(body) }),

  preview: (siteId: string, collectionId: string, body: SaveRequest): Promise<Preview> =>
    request<Preview>(`${one(siteId, collectionId)}/preview`, { method: 'POST', body: JSON.stringify(body) }),

  remove: (siteId: string, collectionId: string, revision: number): Promise<void> =>
    request<void>(`${one(siteId, collectionId)}?revision=${revision}`, { method: 'DELETE' }),

  /** 016: runs a Collection list against published data, for the editor's canvas. */
  listPreview: (siteId: string, body: ListPreviewRequest): Promise<ListPreview> =>
    request<ListPreview>(`/cms/sites/${encodeURIComponent(siteId)}/list-preview`, { method: 'POST', body: JSON.stringify(body) }),

  history: (siteId: string, collectionId: string, before?: number): Promise<SchemaHistory> =>
    request<SchemaHistory>(`${one(siteId, collectionId)}/history${before ? `?before=${before}` : ''}`),
}
