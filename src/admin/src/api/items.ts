// Typed client for collection items (015 contracts/items-api.yaml).

import { ApiError, request } from './request'

export { ApiError } from './request'

export type ItemStatus = 'draft' | 'published' | 'changed'
export type ScheduleAction = 'publish' | 'unpublish'

/** An item values document (docs/contracts/collection-schema.md §3): values keyed by field id. */
export interface ValuesDocument {
  schemaVersion: number
  values: Record<string, unknown>
}

export interface Member {
  id: string
  name: string
}

export interface Schedule {
  action: ScheduleAction
  dueAtUtc: string
  setBy: Member
}

/** One problem of an item's report; `fieldId` is a field id or `slug` (015 data-model §2). */
export interface Problem {
  fieldId: string
  problem: string
  limit?: number | null
  /** 035: for `format_not_allowed`, the format group the field does not allow. */
  format?: 'headings' | 'images' | 'tables'
  /** 035: for a problem inside a rich text value, a JSON pointer into it. */
  path?: string
}

export interface Report {
  problems: Problem[]
}

export interface ItemSummary {
  id: string
  name: string | null
  slug: string
  status: ItemStatus
  attention: boolean
  schedules: Schedule[]
  createdAtUtc: string
  updatedAtUtc: string
  updatedBy: Member
  publishedAtUtc: string | null
}

export interface ItemList {
  items: ItemSummary[]
  total: number
  page: number
  pageSize: number
}

export interface ItemRef {
  id: string
  collectionId: string
  name: string | null
  slug: string
  status: ItemStatus
}

export interface Item extends ItemSummary {
  collectionId: string
  revision: number
  schemaRevision: number
  draft: ValuesDocument
  published: ValuesDocument | null
  publishedSlug: string | null
  liveVersion: number | null
  report: Report
  scheduledDiffersFromDraft: boolean
  unpublishedReferences: ItemRef[]
  /** Where visitors find the item, or why they cannot (016 FR-007). */
  address?: ItemAddress
  /** 017 FR-029, FR-032: set when the change asked for a redirect of the item's old address. */
  redirectRequested?: boolean
}

/**
 * An item's address under its collection's template (016 FR-007). `path` is set when the item
 * has an address; `reason` says why visitors cannot open it, if they cannot.
 */
export interface ItemAddress {
  path: string | null
  reason: 'no_template' | 'template_not_live' | 'not_published' | 'hidden_by_page' | null
}

export interface Referrer {
  itemId: string
  itemName: string | null
  itemSlug: string
  collectionId: string
  collectionName: string
  fieldId: string
  fieldName: string
  inDraft: boolean
  inPublished: boolean
  inScheduled: boolean
}

export interface ReferrerList {
  referrers: Referrer[]
  total: number
}

export type VersionKind = 'publish' | 'scheduled' | 'rollback'

export interface Version {
  number: number
  kind: VersionKind
  restoredFrom: number | null
  publishedAtUtc: string
  publishedBy: Member
  live: boolean
  /** 017: the slug this version published. */
  slug: string
}

export type HistoryKind =
  | 'created'
  | 'published'
  | 'rolledBack'
  | 'unpublished'
  | 'scheduled'
  | 'rescheduled'
  | 'scheduleCancelled'
  | 'scheduleFailed'
  | 'copiedToDraft'

export interface HistoryEvent {
  kind: HistoryKind
  versionNumber: number | null
  scheduleAction: ScheduleAction | null
  dueAtUtc: string | null
  failureCode: string | null
  atUtc: string
  by: Member
}

export interface ItemHistory {
  versions: Version[]
  events: HistoryEvent[]
}

export interface DraftInput {
  slug: string
  values: ValuesDocument
  schemaRevision: number
}

/** The list query of FR-029: search, filters, sort and page (research IT-14). */
export interface ListQuery {
  q?: string
  status?: ItemStatus
  attention?: boolean
  scheduled?: ScheduleAction | 'any'
  /** Field id → value: an option id, `true`/`false`, or an item id. */
  fields?: Record<string, string>
  sort?: string
  dir?: 'asc' | 'desc'
  page?: number
  pageSize?: 25 | 50 | 100
}

/** The extensions an item refusal carries (015 data-model §6). */
export interface ItemRefusal {
  report?: Report
  suggestion?: string
  currentRevision?: number
  schemaRevision?: number
  referrers?: Referrer[]
  total?: number
  schedule?: Schedule
  parameter?: string
}

/** The extra fields of an item refusal; empty when it has none. */
export function refusalOf(error: unknown): ItemRefusal {
  return error instanceof ApiError && error.problem ? (error.problem as unknown as ItemRefusal) : {}
}

export function listQueryString(query: ListQuery): string {
  const params = new URLSearchParams()
  if (query.q) params.set('q', query.q)
  if (query.status) params.set('status', query.status)
  if (query.attention) params.set('attention', 'true')
  if (query.scheduled) params.set('scheduled', query.scheduled)
  for (const [fieldId, value] of Object.entries(query.fields ?? {})) params.set(`field[${fieldId}]`, value)
  if (query.sort) params.set('sort', query.sort)
  if (query.dir) params.set('dir', query.dir)
  if (query.page && query.page > 1) params.set('page', String(query.page))
  if (query.pageSize) params.set('pageSize', String(query.pageSize))
  const text = params.toString()
  return text ? `?${text}` : ''
}

const items = (siteId: string, collectionId: string) =>
  `/cms/sites/${encodeURIComponent(siteId)}/collections/${encodeURIComponent(collectionId)}/items`
const one = (siteId: string, collectionId: string, itemId: string) => `${items(siteId, collectionId)}/${encodeURIComponent(itemId)}`
const post = (body: unknown): RequestInit => ({ method: 'POST', body: JSON.stringify(body) })

export const itemsApi = {
  list: (siteId: string, collectionId: string, query: ListQuery = {}): Promise<ItemList> =>
    request<ItemList>(items(siteId, collectionId) + listQueryString(query)),

  create: (siteId: string, collectionId: string, body: DraftInput): Promise<Item> =>
    request<Item>(items(siteId, collectionId), post(body)),

  slugAvailability: (siteId: string, collectionId: string, slug: string, itemId?: string): Promise<{ available: boolean; suggestion: string }> =>
    request(`${items(siteId, collectionId)}/slug-availability?slug=${encodeURIComponent(slug)}${itemId ? `&itemId=${encodeURIComponent(itemId)}` : ''}`),

  get: (siteId: string, collectionId: string, itemId: string): Promise<Item> => request<Item>(one(siteId, collectionId, itemId)),

  save: (siteId: string, collectionId: string, itemId: string, body: DraftInput & { revision: number }): Promise<Item> =>
    request<Item>(one(siteId, collectionId, itemId), { method: 'PUT', body: JSON.stringify(body) }),

  /** 017 FR-032: with `redirectToPageId`, a live item's address redirects there. */
  remove: (siteId: string, collectionId: string, itemId: string, revision: number, redirectToPageId: string | null = null): Promise<void> =>
    request<void>(`${one(siteId, collectionId, itemId)}?revision=${revision}${redirectToPageId ? `&redirectToPageId=${encodeURIComponent(redirectToPageId)}` : ''}`, { method: 'DELETE' }),

  duplicate: (siteId: string, collectionId: string, itemId: string): Promise<Item> =>
    request<Item>(`${one(siteId, collectionId, itemId)}/duplicate`, post({})),

  /** 017 FR-029: `redirectOldAddress` redirects the old address when the published slug changes. */
  publish: (siteId: string, collectionId: string, itemId: string, revision: number, cancelSchedule = false, redirectOldAddress = false): Promise<Item> =>
    request<Item>(`${one(siteId, collectionId, itemId)}/publish`, post({ revision, cancelSchedule, redirectOldAddress })),

  /** 017 FR-032: with `redirectToPageId`, the item's address redirects there. */
  unpublish: (siteId: string, collectionId: string, itemId: string, revision: number, cancelSchedule = false, redirectToPageId: string | null = null): Promise<Item> =>
    request<Item>(`${one(siteId, collectionId, itemId)}/unpublish`, post({ revision, cancelSchedule, redirectToPageId })),

  /** 017 FR-029, FR-032: the redirect choice is kept on the schedule and applied when it runs. */
  schedule: (
    siteId: string, collectionId: string, itemId: string, action: ScheduleAction,
    body: { dueAtUtc: string; revision: number; recapture?: boolean; redirectOldAddress?: boolean; redirectToPageId?: string | null },
  ): Promise<Item> =>
    request<Item>(`${one(siteId, collectionId, itemId)}/schedules/${action}`, { method: 'PUT', body: JSON.stringify(body) }),

  cancelSchedule: (siteId: string, collectionId: string, itemId: string, action: ScheduleAction, revision: number): Promise<Item> =>
    request<Item>(`${one(siteId, collectionId, itemId)}/schedules/${action}?revision=${revision}`, { method: 'DELETE' }),

  history: (siteId: string, collectionId: string, itemId: string): Promise<ItemHistory> =>
    request<ItemHistory>(`${one(siteId, collectionId, itemId)}/history`),

  referrers: (siteId: string, collectionId: string, itemId: string, publishedOnly = false): Promise<ReferrerList> =>
    request<ReferrerList>(`${one(siteId, collectionId, itemId)}/referrers${publishedOnly ? '?published=true' : ''}`),

  /** 017 FR-029: `redirectOldAddress` redirects the old address when the version's slug differs from the live one. */
  rollback: (siteId: string, collectionId: string, itemId: string, number: number, revision: number, cancelSchedule = false, redirectOldAddress = false): Promise<Item> =>
    request<Item>(`${one(siteId, collectionId, itemId)}/versions/${number}/rollback`, post({ revision, cancelSchedule, redirectOldAddress })),

  copyToDraft: (siteId: string, collectionId: string, itemId: string, number: number, revision: number): Promise<Item> =>
    request<Item>(`${one(siteId, collectionId, itemId)}/versions/${number}/copy-to-draft`, post({ revision })),

  lookup: (siteId: string, ids: string[]): Promise<{ items: ItemRef[]; missing: string[] }> =>
    request(`/cms/sites/${encodeURIComponent(siteId)}/items/lookup`, post({ ids })),
}
