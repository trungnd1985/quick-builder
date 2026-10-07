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

  history: (siteId: string, collectionId: string, before?: number): Promise<SchemaHistory> =>
    request<SchemaHistory>(`${one(siteId, collectionId)}/history${before ? `?before=${before}` : ''}`),
}
