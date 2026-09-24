// Typed client for the Identity module (002 contracts/identity-api.yaml).

import { request } from './request'

export type SiteRoleName = 'designer' | 'editor'

export interface SiteRole {
  siteId: string
  role: SiteRoleName
}

/** A user's complete set of roles. */
export interface Grants {
  administrator: boolean
  siteRoles: SiteRole[]
}

/** The signed-in user and what they may do. */
export interface Me {
  id: string
  displayName: string
  email: string
  isAdministrator: boolean
  siteRoles: SiteRole[]
}

/** An account as administrators see it. */
export interface User {
  id: string
  displayName: string
  email: string
  status: 'active' | 'deactivated'
  hasPassword: boolean
  grants: Grants
  createdAtUtc: string
  lastSignInAtUtc: string | null
}

/** A set-password link's token, shown once. The shell builds the URL (002 research R-10). */
export interface IssuedLink {
  token: string
  expiresAtUtc: string
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

const json = (method: string, body?: unknown): RequestInit => ({
  method,
  body: body === undefined ? undefined : JSON.stringify(body),
})

export const identityApi = {
  status: (): Promise<Status> => request<Status>('/identity/status'),

  setup: (input: SetupInput): Promise<Me> => request<Me>('/identity/setup', json('POST', input)),

  me: (): Promise<Me> => request<Me>('/identity/me'),

  signIn: (email: string, password: string): Promise<Me> =>
    request<Me>('/identity/sign-in', json('POST', { email, password })),

  signOut: (): Promise<void> => request<void>('/identity/sign-out', json('POST')),

  changeOwnPassword: (currentPassword: string, newPassword: string): Promise<void> =>
    request<void>('/identity/me/password', json('POST', { currentPassword, newPassword })),

  revokeOtherSessions: (): Promise<void> => request<void>('/identity/me/sessions/revoke-others', json('POST')),

  checkLink: (token: string): Promise<{ expiresAtUtc: string }> =>
    request<{ expiresAtUtc: string }>('/identity/set-password-links/check', json('POST', { token })),

  setPassword: (token: string, newPassword: string): Promise<void> =>
    request<void>('/identity/set-password', json('POST', { token, newPassword })),
}

export const usersApi = {
  list: (): Promise<User[]> => request<User[]>('/identity/users'),

  get: (id: string): Promise<User> => request<User>(`/identity/users/${id}`),

  create: (displayName: string, email: string, grants: Grants): Promise<{ user: User; setPasswordLink: IssuedLink }> =>
    request('/identity/users', json('POST', { displayName, email, grants })),

  update: (id: string, patch: { displayName?: string; email?: string }): Promise<User> =>
    request<User>(`/identity/users/${id}`, json('PATCH', patch)),

  replaceGrants: (id: string, grants: Grants): Promise<User> =>
    request<User>(`/identity/users/${id}/grants`, json('PUT', grants)),

  deactivate: (id: string): Promise<User> => request<User>(`/identity/users/${id}/deactivate`, json('POST')),

  reactivate: (id: string): Promise<User> => request<User>(`/identity/users/${id}/reactivate`, json('POST')),

  issueLink: (id: string): Promise<IssuedLink> => request<IssuedLink>(`/identity/users/${id}/set-password-links`, json('POST')),

  revokeSessions: (id: string): Promise<void> => request<void>(`/identity/users/${id}/sessions/revoke`, json('POST')),
}

/**
 * The link to hand to a user: this admin's own address, never one the server made up
 * (002 FR-011b). The token goes in the fragment, which browsers never send to a server.
 */
export function setPasswordUrl(token: string): string {
  return `${window.location.origin}${import.meta.env.BASE_URL}set-password#token=${encodeURIComponent(token)}`
}
