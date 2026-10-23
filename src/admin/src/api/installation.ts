// Typed client for the installation's release (019 contracts/installation-version.md §4).

import { request } from './request'

/** The running release and the one the database records. Administrators only. */
export interface Installation {
  version: string
  revision: string | null
  databaseVersion: string | null
  upgradingTo: string | null
  releaseNotesUrl: string | null
}

export const installationApi = {
  get: (): Promise<Installation> => request<Installation>('/identity/installation'),
}
