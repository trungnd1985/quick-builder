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
}
