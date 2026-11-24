// Where member previews are served (022 research PV-05, public-preview.md §2). Root-relative,
// on the admin's own origin, where the session cookie lives; the server answers /_preview on
// every host name and takes the site from the address, never from the host. Under the
// installation's base path, like everything the host serves (#51).

import { APP_ROOT } from '../../app/base'

/** A page's draft, one of its versions, or a template page with a sample item. */
export function pagePreviewUrl(siteId: string, pageId: string, options: { version?: number; item?: string | null } = {}): string {
  const query = new URLSearchParams()
  if (options.version !== undefined) query.set('version', String(options.version))
  if (options.item) query.set('item', options.item)
  const search = query.toString()
  return `${APP_ROOT}_preview/sites/${siteId}/pages/${pageId}${search ? `?${search}` : ''}`
}

/** An item's draft or one of its versions, through its collection's template. */
export function itemPreviewUrl(siteId: string, itemId: string, version?: number): string {
  return `${APP_ROOT}_preview/sites/${siteId}/items/${itemId}${version !== undefined ? `?version=${version}` : ''}`
}
