// Typed client for the Sites module (001 contracts/admin-api.yaml, extended additively by
// 009 contracts/sites-api.yaml).

import { request } from './request'

export { ApiError, type ProblemDetails } from './request'

export type SiteStatus = 'enabled' | 'disabled'
export type SiteScheme = 'https' | 'http'

export interface HostName {
  /** The stored ASCII form, e.g. `xn--bcher-kva.example`. */
  hostName: string
  /** The readable form, e.g. `bücher.example`. */
  displayName: string
  isPrimary: boolean
}

/**
 * What the signed-in user may do on this site, decided by the server for this request.
 * Controls are hidden or disabled from it; the server checks every action regardless.
 */
export interface SitePermissions {
  canManageSettings: boolean
  canManageHostNames: boolean
  canManageAvailability: boolean
  /** 017: title suffix, default sharing image, organisation. */
  canManageSearch?: boolean
  /** 017: discourage search engines, robots rules, host name redirect (Administrators). */
  canManageSearchVisibility?: boolean
  /** 020: export and import the site (Administrators). */
  canTransfer?: boolean
}

/** 017 FR-006: how the site presents itself to search engines and link previews. */
export interface SiteSearch {
  titleSuffix: string | null
  defaultImageId: string | null
  organisationName: string | null
  organisationLogoId: string | null
  updatedBy: string | null
}

/** 017 FR-014, FR-025, FR-027: whether search engines see the site, and how host names answer. */
export interface SiteSearchVisibility {
  discourageSearchEngines: boolean
  robotsRules: string | null
  redirectOtherHostNames: boolean
}

export interface Site {
  id: string
  name: string
  /** ASCII host names, the primary first. */
  hostNames: string[]
  createdAtUtc: string
  primaryHostName: string
  hostNameDetails: HostName[]
  scheme: SiteScheme
  baseUrl: string
  defaultLanguage: string
  status: SiteStatus
  /** Opaque. Send it back with a settings change. */
  version: number
  updatedAtUtc: string
  permissions: SitePermissions
  /** 017. Absent from servers before 017. */
  search?: SiteSearch
  searchVisibility?: SiteSearchVisibility
  /** 020 FR-028: the export or import holding the site's transfer slot, on the single-site read. */
  transfer?: SiteTransfer | null
}

/** 020: while `kind` is `import` and `state` is `running`, the site's changes are paused. */
export interface SiteTransfer {
  id: string
  kind: 'export' | 'import'
  mode: 'new_site' | 'merge' | 'restore' | null
  state: string
  startedAtUtc: string
}

export interface SiteSettings {
  name: string
  scheme: SiteScheme
  defaultLanguage: string
  primaryHostName: string
  version: number
}

export interface CreateSiteOptions {
  scheme?: SiteScheme
  defaultLanguage?: string
}

export interface Languages {
  languages: string[]
  default: string
}

/**
 * Host names are compared in their ASCII form, which the server derives. Its runtime has
 * no Unicode normalisation (009 research S-04), so a decomposed "ü" typed on some
 * keyboards would encode differently from the composed one. The browser can normalise;
 * every host name leaving this app goes through here.
 */
export function normaliseHostName(hostName: string): string {
  return hostName.trim().normalize('NFC')
}

export const sitesApi = {
  list: (): Promise<Site[]> => request<Site[]>('/sites'),

  get: (siteId: string): Promise<Site> => request<Site>(`/sites/${siteId}`),

  create: (name: string, hostNames: string[], options: CreateSiteOptions = {}): Promise<Site> =>
    request<Site>('/sites', {
      method: 'POST',
      body: JSON.stringify({ name, hostNames: hostNames.map(normaliseHostName), ...options }),
    }),

  putSettings: (siteId: string, settings: SiteSettings): Promise<Site> =>
    request<Site>(`/sites/${siteId}/settings`, {
      method: 'PUT',
      body: JSON.stringify({ ...settings, primaryHostName: normaliseHostName(settings.primaryHostName) }),
    }),

  addHostName: (siteId: string, hostName: string): Promise<HostName> =>
    request<HostName>(`/sites/${siteId}/host-names`, {
      method: 'POST',
      body: JSON.stringify({ hostName: normaliseHostName(hostName) }),
    }),

  removeHostName: (siteId: string, hostName: string): Promise<void> =>
    request<void>(`/sites/${siteId}/host-names/${encodeURIComponent(hostName)}`, { method: 'DELETE' }),

  disable: (siteId: string): Promise<Site> => request<Site>(`/sites/${siteId}/disable`, { method: 'POST' }),

  enable: (siteId: string): Promise<Site> => request<Site>(`/sites/${siteId}/enable`, { method: 'POST' }),

  languages: (): Promise<Languages> => request<Languages>('/sites/languages'),

  /** 017: needs sites.search.manage. */
  putSearchSettings: (
    siteId: string,
    settings: { titleSuffix: string | null; defaultImageId: string | null; organisationName: string | null; organisationLogoId: string | null; version: number },
  ): Promise<Site> => request<Site>(`/sites/${siteId}/search-settings`, { method: 'PUT', body: JSON.stringify(settings) }),

  /** 017: Administrators only. */
  putSearchVisibility: (
    siteId: string,
    visibility: { discourageSearchEngines: boolean; robotsRules: string | null; redirectOtherHostNames: boolean; version: number },
  ): Promise<Site> => request<Site>(`/sites/${siteId}/search-visibility`, { method: 'PUT', body: JSON.stringify(visibility) }),
}
