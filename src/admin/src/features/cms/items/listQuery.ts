// The item list's state lives in the page address (015 FR-030), so a view can be reloaded and
// shared, and returning from an item restores it. This maps the route query to the API's list
// query and back; anything it does not understand is dropped.

import type { LocationQuery, LocationQueryRaw } from 'vue-router'
import type { ItemStatus, ListQuery, ScheduleAction } from '../../../api/items'

export const PAGE_SIZES = [25, 50, 100] as const
export type PageSize = (typeof PAGE_SIZES)[number]

const FIELD_PREFIX = 'field.'
const STATUSES: readonly ItemStatus[] = ['draft', 'published', 'changed']
const SCHEDULED: readonly (ScheduleAction | 'any')[] = ['publish', 'unpublish', 'any']

const one = (value: LocationQuery[string]): string | undefined => {
  const first = Array.isArray(value) ? value[0] : value
  return typeof first === 'string' && first !== '' ? first : undefined
}

export function fromRoute(query: LocationQuery): ListQuery {
  const fields: Record<string, string> = {}
  for (const [key, value] of Object.entries(query)) {
    const text = one(value)
    if (key.startsWith(FIELD_PREFIX) && text) fields[key.slice(FIELD_PREFIX.length)] = text
  }

  const status = one(query.status) as ItemStatus | undefined
  const scheduled = one(query.scheduled) as ScheduleAction | 'any' | undefined
  const page = Number(one(query.page) ?? '1')
  const size = Number(one(query.size) ?? '25') as PageSize
  const dir = one(query.dir)

  return {
    q: one(query.q),
    status: status && STATUSES.includes(status) ? status : undefined,
    attention: one(query.attention) === '1' ? true : undefined,
    scheduled: scheduled && SCHEDULED.includes(scheduled) ? scheduled : undefined,
    fields,
    sort: one(query.sort),
    dir: dir === 'asc' || dir === 'desc' ? dir : undefined,
    page: Number.isInteger(page) && page > 1 ? page : 1,
    pageSize: PAGE_SIZES.includes(size) ? size : 25,
  }
}

export function toRoute(query: ListQuery): LocationQueryRaw {
  const route: LocationQueryRaw = {}
  if (query.q) route.q = query.q
  if (query.status) route.status = query.status
  if (query.attention) route.attention = '1'
  if (query.scheduled) route.scheduled = query.scheduled
  for (const [fieldId, value] of Object.entries(query.fields ?? {})) {
    if (value) route[`${FIELD_PREFIX}${fieldId}`] = value
  }
  if (query.sort) route.sort = query.sort
  if (query.dir) route.dir = query.dir
  if (query.page && query.page > 1) route.page = String(query.page)
  if (query.pageSize && query.pageSize !== 25) route.size = String(query.pageSize)
  return route
}

/** Whether any filter or search narrows the list, so "no results" differs from "no items". */
export const isNarrowed = (query: ListQuery): boolean =>
  Boolean(query.q || query.status || query.attention || query.scheduled || Object.keys(query.fields ?? {}).length > 0)
