// Typed client for the Identity module (002 contracts/identity-api.yaml).

import { request } from './request'

export type SiteRoleName = 'designer' | 'editor'

export interface SiteRole {
  siteId: string
  role: SiteRoleName
}

/** The signed-in user and what they may do. */
export interface Me {
  id: string
  displayName: string
  email: string
  isAdministrator: boolean
  siteRoles: SiteRole[]
}

export interface Status {
  setupRequired: boolean
}

export interface SetupInput {
  setupCode: string
  displayName: string
  email: string
  password: string
}

export const identityApi = {
  status: (): Promise<Status> => request<Status>('/identity/status'),

  setup: (input: SetupInput): Promise<Me> =>
    request<Me>('/identity/setup', { method: 'POST', body: JSON.stringify(input) }),

  me: (): Promise<Me> => request<Me>('/identity/me'),

  signIn: (email: string, password: string): Promise<Me> =>
    request<Me>('/identity/sign-in', { method: 'POST', body: JSON.stringify({ email, password }) }),

  signOut: (): Promise<void> => request<void>('/identity/sign-out', { method: 'POST' }),
}
