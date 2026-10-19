// Typed client for publishing (012 contracts/publishing-api.yaml): pages and components in the
// Pages module, the design in the Editor module.

import type { ContentDocumentJson, Draft, UserReference } from './pages'
import { request } from './request'

export type SubjectKind = 'page' | 'component' | 'menu'

export type ScheduleAction = 'publish' | 'unpublish'

export type ScheduleState = 'pending' | 'done' | 'cancelled' | 'failed'

export interface Schedule {
  action: ScheduleAction
  /** UTC ISO-8601; the admin shell shows it in the member's time zone. */
  dueAtUtc: string
  state: ScheduleState
  failureCode: string | null
  setBy: UserReference
  capturedDraftVersion: number | null
}

/** A page's or component's publication state (research PR-02). */
export interface Publication {
  state: 'draft' | 'published'
  liveNumber: number | null
  liveSinceUtc: string | null
  liveBy: UserReference | null
  /** The draft (or, for pages, the title) differs from what visitors get. */
  hasUnpublishedChanges: boolean
  /** Pages only: where visitors find it. */
  liveUrl: string | null
  pendingSchedules: Schedule[]
  failedSchedule: Schedule | null
}

export type LogAction =
  | 'publish'
  | 'scheduled_publish'
  | 'rollback'
  | 'unpublish'
  | 'scheduled_unpublish'
  | 'schedule_set'
  | 'schedule_changed'
  | 'schedule_cancelled'
  | 'schedule_failed'
  | 'copied_to_draft'
  | 'deleted'

export interface LogEntry {
  id: string
  subjectKind: SubjectKind | 'design'
  subjectId: string
  subjectLabel: string
  subjectPath: string | null
  /** False once the page or component has been deleted. */
  subjectExists: boolean
  action: LogAction
  versionNumber: number | null
  restoredFromNumber: number | null
  actor: UserReference | null
  scheduleOwner: UserReference | null
  reasonCode: string | null
  occurredAtUtc: string
}

export interface SubjectHistory {
  publication: Publication
  /** Versions that can be viewed, rolled back to or copied (FR-013). */
  retainedVersions: number[]
  entries: LogEntry[]
}

export interface VersionContent {
  number: number
  schemaVersion: number
  document: ContentDocumentJson
  title: string | null
  kind: 'publish' | 'scheduled' | 'rollback'
  restoredFromNumber: number | null
  publishedAtUtc: string
  publishedBy: UserReference
}

export interface PublishLogPage {
  items: LogEntry[]
  nextCursor: string | null
}

export interface PublishLogFilter {
  subjectKind?: SubjectKind | 'design'
  subjectId?: string
  userId?: string
  cursor?: string
  limit?: number
}

export interface DesignPublication {
  /** 0 while the site uses the built-in default design. */
  liveNumber: number
  liveSinceUtc: string | null
  liveBy: UserReference | null
  hasUnpublishedChanges: boolean
}

export interface DesignVersionSummary {
  number: number
  kind: 'publish' | 'rollback'
  restoredFromNumber: number | null
  publishedAtUtc: string
  publishedBy: UserReference
}

export interface DesignVersions {
  publication: DesignPublication
  versions: DesignVersionSummary[]
}

const SEGMENTS: Record<SubjectKind, string> = { page: 'pages', component: 'components', menu: 'menus' }
const site = (siteId: string) => `/pages/sites/${encodeURIComponent(siteId)}`
const subject = (siteId: string, kind: SubjectKind, id: string) =>
  `${site(siteId)}/${SEGMENTS[kind]}/${encodeURIComponent(id)}`
const design = (siteId: string) => `/editor/sites/${encodeURIComponent(siteId)}/design-tokens`
const post = (body?: unknown): RequestInit => ({ method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) })

export const publishingApi = {
  publish: (siteId: string, kind: SubjectKind, id: string, draftVersion: number): Promise<Publication> =>
    request<Publication>(`${subject(siteId, kind, id)}/publish`, post({ draftVersion })),

  /** 017 FR-032: with `redirectToPageId`, a page's address redirects there. */
  unpublish: (siteId: string, kind: SubjectKind, id: string, redirectToPageId: string | null = null): Promise<Publication> =>
    request<Publication>(
      `${subject(siteId, kind, id)}/unpublish`,
      redirectToPageId ? { method: 'POST', body: JSON.stringify({ redirectToPageId }) } : post(),
    ),

  rollback: (siteId: string, kind: SubjectKind, id: string, versionNumber: number): Promise<Publication> =>
    request<Publication>(`${subject(siteId, kind, id)}/rollback`, post({ versionNumber })),

  history: (siteId: string, kind: SubjectKind, id: string): Promise<SubjectHistory> =>
    request<SubjectHistory>(`${subject(siteId, kind, id)}/history`),

  version: (siteId: string, kind: SubjectKind, id: string, number: number): Promise<VersionContent> =>
    request<VersionContent>(`${subject(siteId, kind, id)}/versions/${number}`),

  copyToDraft: (siteId: string, kind: SubjectKind, id: string, number: number, draftVersion: number): Promise<Draft> =>
    request<Draft>(`${subject(siteId, kind, id)}/versions/${number}/copy-to-draft`, post({ draftVersion })),

  /**
   * Sets or moves the pending schedule for `action`. `draftVersion` is required to publish;
   * 017 FR-032: `redirectToPageId`, for a page unpublish, redirects its address when it runs.
   */
  schedule: (
    siteId: string, kind: SubjectKind, id: string, action: ScheduleAction, dueAtUtc: string, draftVersion?: number, redirectToPageId: string | null = null,
  ): Promise<Schedule> =>
    request<Schedule>(`${subject(siteId, kind, id)}/schedules/${action}`, {
      method: 'PUT',
      body: JSON.stringify({ dueAtUtc, draftVersion, redirectToPageId }),
    }),

  cancelSchedule: (siteId: string, kind: SubjectKind, id: string, action: ScheduleAction): Promise<void> =>
    request<void>(`${subject(siteId, kind, id)}/schedules/${action}`, { method: 'DELETE' }),

  log: (siteId: string, filter: PublishLogFilter = {}): Promise<PublishLogPage> => {
    const query = new URLSearchParams()
    for (const [key, value] of Object.entries(filter)) {
      if (value !== undefined && value !== '') query.set(key, String(value))
    }
    const suffix = query.size > 0 ? `?${query.toString()}` : ''
    return request<PublishLogPage>(`${site(siteId)}/publish-log${suffix}`)
  },

  // --- The design (Editor module) --------------------------------------------------------

  publishDesign: (siteId: string, version: number): Promise<DesignPublication> =>
    request<DesignPublication>(`${design(siteId)}/publish`, post({ version })),

  rollbackDesign: (siteId: string, versionNumber: number): Promise<DesignPublication> =>
    request<DesignPublication>(`${design(siteId)}/rollback`, post({ versionNumber })),

  designVersions: (siteId: string): Promise<DesignVersions> =>
    request<DesignVersions>(`${design(siteId)}/versions`),
}
