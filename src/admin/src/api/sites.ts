// Typed client for the Sites module (001 contracts/admin-api.yaml).

import { request } from './request'

export { ApiError, type ProblemDetails } from './request'

export interface Site {
  id: string
  name: string
  hostNames: string[]
  createdAtUtc: string
}

export const sitesApi = {
  list: (): Promise<Site[]> => request<Site[]>('/sites'),

  create: (name: string, hostNames: string[]): Promise<Site> =>
    request<Site>('/sites', {
      method: 'POST',
      body: JSON.stringify({ name, hostNames }),
    }),
}
