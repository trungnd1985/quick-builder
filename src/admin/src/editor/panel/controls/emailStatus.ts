import { formsApi } from '../../../api/forms'

let status: Promise<boolean> | null = null

/**
 * Whether the installation sends email (018 FR-037), asked once per page load: it changes only
 * when an operator restarts the server with new settings. A failed call answers true, so the
 * editor never warns on a guess.
 */
export function emailConfigured(): Promise<boolean> {
  status ??= formsApi.emailStatus().then((s) => s.configured, () => true)
  return status
}

/** Forgets the answer, for tests. */
export function resetEmailStatus(): void {
  status = null
}
