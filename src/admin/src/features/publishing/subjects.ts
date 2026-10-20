// What differs between the things that publish — pages, components and menus (024 NV-01): where
// their history and list live, and how their current draft version and publication are read.
// The publishing views ask here, so a new kind is added in one place.
import { menusApi } from '../../api/menus'
import { pagesApi } from '../../api/pages'
import type { Publication, SubjectKind } from '../../api/publishing'

const HISTORY: Record<SubjectKind, { name: string; param: string }> = {
  page: { name: 'page-history', param: 'pageId' },
  component: { name: 'component-history', param: 'componentId' },
  menu: { name: 'menu-history', param: 'menuId' },
}

const LIST: Record<SubjectKind, string> = { page: 'site-pages', component: 'site-components', menu: 'site-menus' }

export function historyRouteName(kind: SubjectKind): string {
  return HISTORY[kind].name
}

export function historyRoute(kind: SubjectKind, siteId: string, id: string) {
  return { name: HISTORY[kind].name, params: { siteId, [HISTORY[kind].param]: id } }
}

export function listRoute(kind: SubjectKind, siteId: string) {
  return { name: LIST[kind], params: { siteId } }
}

/** A retained version opened read-only in the editor; menus have no such view. */
export function versionRoute(kind: SubjectKind, siteId: string, id: string, number: number) {
  if (kind === 'menu') return null
  return kind === 'page'
    ? { name: 'page-version', params: { siteId, pageId: id, number: String(number) } }
    : { name: 'component-version', params: { siteId, componentId: id, number: String(number) } }
}

export async function currentDraftVersion(kind: SubjectKind, siteId: string, id: string): Promise<number> {
  if (kind === 'page') return (await pagesApi.pageDraft(siteId, id)).version
  if (kind === 'component') return (await pagesApi.componentDraft(siteId, id)).version
  return (await menusApi.get(siteId, id)).draftVersion
}

export async function currentPublication(kind: SubjectKind, siteId: string, id: string): Promise<Publication> {
  if (kind === 'page') return (await pagesApi.get(siteId, id)).publication
  if (kind === 'component') return (await pagesApi.getComponent(siteId, id)).publication
  return (await menusApi.get(siteId, id)).publication
}
