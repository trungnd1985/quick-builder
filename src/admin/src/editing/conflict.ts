// Who changed a draft, and when, as an out-of-date refusal reports it (023 FR-003, research CE-09).

import { ApiError } from '../api/request'

export interface ConflictDetails {
  /** The member who saved the newer version; null for a system change. */
  changedBy: { id: string; displayName: string } | null
  changedAtUtc?: string
  /** `schema_change`: the collection's fields were updated (items only). */
  changeKind: 'member' | 'schema_change'
}

/** The details of a 409 conflict, or null when the refusal carries none (a server from before 023). */
export function conflictFrom(error: unknown): ConflictDetails | null {
  if (!(error instanceof ApiError) || error.status !== 409 || !error.problem) return null
  const problem = error.problem
  if (problem.changedBy === undefined && problem.changedAtUtc === undefined) return null
  return {
    changedBy: problem.changedBy ?? null,
    changedAtUtc: problem.changedAtUtc,
    changeKind: problem.changeKind === 'schema_change' ? 'schema_change' : 'member',
  }
}
