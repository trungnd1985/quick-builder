// The site's trash (specs/021-soft-delete-trash/contracts/trash-api.yaml).
//
// Each kind lives in the module that owns it — pages and components in Pages, items in Cms,
// files in Media — so each tab of the trash view calls only its own endpoints (research TR-01).

import { request } from './request'

/** What a delete answers: until when the content can be restored (FR-002). */
export interface TrashReceipt {
  trashedAtUtc: string
  purgeOnUtc: string
}

export type TrashKind = 'page' | 'component' | 'item' | 'file'

export interface TrashActor {
  userId: string
  displayName: string
}

/** Where an entry was (FR-005). Pages say whether the parent is live, in the trash, or gone. */
export interface TrashPlace {
  parentId?: string | null
  parentLabel?: string | null
  parentState?: 'live' | 'trashed' | 'gone' | 'none'
  collectionId?: string
  collectionName?: string | null
  folderId?: string | null
  folderName?: string | null
  folderState?: 'live' | 'gone' | 'none'
}

export interface TrashEntry {
  kind: TrashKind
  id: string
  label: string
  address: string | null
  place: TrashPlace | null
  trashedAtUtc: string
  trashedBy: TrashActor
  purgeOnUtc: string
  wasLive?: boolean
  hadSchedule?: boolean
  sizeBytes?: number
  canRestore: boolean
  canPurge: boolean
}

export interface TrashList {
  entries: TrashEntry[]
  nextCursor: string | null
  total: number
  /** Media only: the storage the trashed files take (FR-012). */
  totalBytes?: number
}

export interface TrashFilter {
  q?: string
  trashedBy?: string
  from?: string
  to?: string
  collectionId?: string
  folderId?: string
  after?: string | null
}

/** What a restore will do, shown before confirming (TR-10). */
export interface RestorePlan {
  placement: string
  placementOptions?: string[]
  target?: { parentId: string | null; label: string; position: number }
  parentsToRestore?: { id: string; label: string; address: string }[]
  address: string
  previousAddress?: string | null
  addressChanged?: boolean
  becomesHome?: boolean
  templateDropped?: boolean
  schemaIssues?: string[]
  folderId?: string | null
  folderName?: string | null
  folderGone?: boolean
  wasLive?: boolean
  hadSchedule?: boolean
  canRestoreAndPublish?: boolean
}

export interface RestoreChoice {
  placement?: string | null
  parentId?: string | null
  folderId?: string | null
  publish?: boolean
}

export interface RestoreResult {
  restored: { kind: TrashKind; id: string; address: string | null }[]
  published: boolean
}

/** The tab a kind belongs to, and the module path that serves it. */
const base = (siteId: string, kind: TrashKind): string => {
  const site = encodeURIComponent(siteId)
  switch (kind) {
    case 'page':
      return `/pages/sites/${site}/trash/pages`
    case 'component':
      return `/pages/sites/${site}/trash/components`
    case 'item':
      return `/cms/sites/${site}/trash/items`
    case 'file':
      return `/media/sites/${site}/trash/files`
  }
}

const query = (filter: TrashFilter): string => {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(filter)) {
    if (value !== undefined && value !== null && value !== '') params.set(key, String(value))
  }
  const text = params.toString()
  return text ? `?${text}` : ''
}

export const trashApi = {
  list: (siteId: string, kind: TrashKind, filter: TrashFilter = {}): Promise<TrashList> =>
    request<TrashList>(`${base(siteId, kind)}${query(filter)}`),

  /** One entry. Pages, components and items carry their draft for the read-only preview (FR-011). */
  get: (siteId: string, kind: TrashKind, id: string): Promise<TrashEntry | { entry: TrashEntry; draft: string | null }> =>
    request(`${base(siteId, kind)}/${encodeURIComponent(id)}`),

  restorePlan: (siteId: string, kind: TrashKind, id: string, choice: RestoreChoice = {}): Promise<RestorePlan> =>
    request<RestorePlan>(
      `${base(siteId, kind)}/${encodeURIComponent(id)}/restore-plan${query({ placement: choice.placement ?? undefined, parentId: choice.parentId ?? undefined, folderId: choice.folderId ?? undefined } as TrashFilter)}`,
    ),

  /** Restores as planned; `confirmedAddress` is the address the member saw (TR-10). */
  restore: (siteId: string, kind: TrashKind, id: string, confirmedAddress: string, choice: RestoreChoice = {}): Promise<RestoreResult> =>
    request<RestoreResult>(`${base(siteId, kind)}/${encodeURIComponent(id)}/restore`, {
      method: 'POST',
      body: JSON.stringify({ ...choice, confirmedAddress }),
    }),
}

/** 021 US5: what an Administrator's empty removed, per module. */
export interface EmptyResult {
  pages?: number
  components?: number
  items?: number
  files?: number
  failed: { id: string; code: string; detail: string }[]
}

/** The counts the Administrator confirmed, per kind, at `trashedUpTo` (FR-021). */
export interface TrashCounts {
  page: number
  component: number
  item: number
  file: number
}

export const trashPurgeApi = {
  /** Deletes one entry permanently (Administrators). */
  purge: (siteId: string, kind: TrashKind, id: string): Promise<void> =>
    request<void>(`${base(siteId, kind)}/${encodeURIComponent(id)}`, { method: 'DELETE' }),

  /** The counts per kind of entries trashed at or before `trashedUpTo`. */
  counts: async (siteId: string, trashedUpTo: string): Promise<TrashCounts> => {
    const [page, component, item, file] = await Promise.all(
      (['page', 'component', 'item', 'file'] as const).map((kind) =>
        trashApi.list(siteId, kind, { to: trashedUpTo }).then((list) => list.total).catch(() => 0),
      ),
    )
    return { page: page ?? 0, component: component ?? 0, item: item ?? 0, file: file ?? 0 }
  },

  /** Empties one module's trash; refused with `trash.count_changed` when the counts moved. */
  empty: (siteId: string, module: 'pages' | 'cms' | 'media', trashedUpTo: string, counts: TrashCounts): Promise<EmptyResult> => {
    const site = encodeURIComponent(siteId)
    const body =
      module === 'pages'
        ? { trashedUpTo, expectedPages: counts.page, expectedComponents: counts.component }
        : module === 'cms'
          ? { trashedUpTo, expectedItems: counts.item }
          : { trashedUpTo, expectedFiles: counts.file }
    return request<EmptyResult>(`/${module}/sites/${site}/trash/empty`, { method: 'POST', body: JSON.stringify(body) })
  },
}

export interface BulkOutcome {
  succeeded: string[]
  failed: { id: string; code: string; detail: string }[]
}

/** Restores several entries of one kind, each on its own (FR-019). */
export function restoreMany(siteId: string, kind: TrashKind, entries: { id: string; confirmedAddress: string }[]): Promise<BulkOutcome> {
  const site = encodeURIComponent(siteId)
  const module = kind === 'item' ? 'cms' : kind === 'file' ? 'media' : 'pages'
  const body = module === 'pages' ? entries.map((e) => ({ ...e, kind })) : entries
  return request<BulkOutcome>(`/${module}/sites/${site}/trash/restore`, { method: 'POST', body: JSON.stringify({ entries: body }) })
}

/** Deletes several entries of one kind permanently (Administrators, FR-021). */
export function purgeMany(siteId: string, kind: TrashKind, ids: string[]): Promise<BulkOutcome> {
  const site = encodeURIComponent(siteId)
  const module = kind === 'item' ? 'cms' : kind === 'file' ? 'media' : 'pages'
  const body = module === 'pages' ? { entries: ids.map((id) => ({ kind, id })) } : { ids }
  return request<BulkOutcome>(`/${module}/sites/${site}/trash/purge`, { method: 'POST', body: JSON.stringify(body) })
}

/** The entry of a trash `get`, whatever the module returned. */
export function entryOf(response: TrashEntry | { entry: TrashEntry }): TrashEntry {
  return 'entry' in response ? response.entry : response
}
