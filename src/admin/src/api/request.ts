// The one way the admin shell talks to the server (contracts/identity-api.yaml).
//
// Endpoints carry their version from the first one (FR-010a): /api/v1/<module>/...
// Everything cross-cutting about a call lives here, once, so no screen can forget it:
// the request header that proves a call came from this app (002 research R-11), sending
// the session cookie, and what to do when the server says "sign in" or "set up first".

import { APP_ROOT } from '../app/base'

/** Under the installation's base path, like everything the host serves (#51). */
export const API_ROOT = `${APP_ROOT}api/v1`

export interface ProblemDetails {
  title: string
  status: number
  detail?: string
  errorCode: string
  traceId?: string
  errors?: Record<string, string[]>
  /** A document refusal's failed rule (010 DocumentProblem). */
  rule?: string
  /** A JSON Pointer to where a document or token set failed. */
  pointer?: string
  /** An out-of-date draft save: who saved the newer version, and when (023 research CE-09). */
  changedBy?: { id: string; displayName: string } | null
  changedAtUtc?: string
  /** For items: `member` or `schema_change`. */
  changeKind?: string
  /** A `media.in_use` refusal's usage summary (013 contracts/media-api.yaml). */
  usage?: {
    places: number
    draftPlaces: number
    livePlaces: number
    historyOnlyPlaces: number
    inLiveUse: boolean
  }
}

/** An error carrying the server's problem details, so screens can show the real reason. */
export class ApiError extends Error {
  // Declared explicitly rather than as constructor parameter properties: the TypeScript
  // config runs with erasableSyntaxOnly, which rules out syntax that emits runtime code.
  readonly problem?: ProblemDetails
  readonly status: number
  readonly retryAfterSeconds?: number

  constructor(message: string, status: number, problem?: ProblemDetails, retryAfterSeconds?: number) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
    this.retryAfterSeconds = retryAfterSeconds
  }

  /** The stable, machine-readable reason, e.g. `identity.throttled`. */
  get errorCode(): string | undefined {
    return this.problem?.errorCode
  }

  /** Per-field validation messages, when the failure was a validation failure. */
  get fieldErrors(): Record<string, string[]> {
    return this.problem?.errors ?? {}
  }
}

/** What the shell does when any call is refused for a reason the whole app handles. */
export interface ApiHooks {
  /** 401: no valid session. */
  onUnauthenticated?: () => void
  /** 428: the installation has not been set up. */
  onSetupRequired?: () => void
}

const hooks: ApiHooks = {}

export function setApiHooks(next: ApiHooks): void {
  Object.assign(hooks, next)
}

const SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS'])

/** The header that proves a state-changing call came from this app (002 research R-11). */
export const REQUEST_HEADER = 'X-QuickBuilder-Request'

/**
 * Which rich text formats this admin keeps when it saves (035 research RT-13). The server
 * refuses a save from an admin that does not say so, when the stored draft holds formats an
 * older admin would drop.
 */
export const RICH_TEXT_HEADER = 'X-QB-Rich-Text'
export const RICH_TEXT_LEVEL = '2'

/**
 * Turns a refused response into an ApiError and runs the app-wide reaction to it. Shared
 * with calls that cannot use fetch — uploads use XMLHttpRequest for progress (003 M-10).
 */
export function refusal(status: number, statusText: string, body: string, retryAfterHeader?: string | null): ApiError {
  // A problem-details body is expected, but a proxy or a crash can produce something
  // else. Falling back to the status text keeps the screen useful either way.
  let problem: ProblemDetails | undefined
  try {
    problem = JSON.parse(body) as ProblemDetails
  } catch {
    problem = undefined
  }

  const retryAfter = Number(retryAfterHeader)
  const error = new ApiError(
    problem?.detail ?? problem?.title ?? statusText,
    status,
    problem,
    Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : undefined,
  )

  if (status === 401) {
    hooks.onUnauthenticated?.()
  } else if (status === 428) {
    hooks.onSetupRequired?.()
  }

  return error
}

export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const method = (init?.method ?? 'GET').toUpperCase()

  const response = await fetch(`${API_ROOT}${path}`, {
    ...init,
    credentials: 'same-origin',
    headers: {
      'content-type': 'application/json',
      // Browsers cannot add this to a cross-site request without a CORS preflight, which
      // the server never grants, so its presence proves the call came from this app.
      ...(SAFE_METHODS.has(method) ? {} : { [REQUEST_HEADER]: '1' }),
      [RICH_TEXT_HEADER]: RICH_TEXT_LEVEL,
      ...(init?.headers ?? {}),
    },
  })

  if (!response.ok) {
    let body: string
    try {
      body = response.text ? await response.text() : JSON.stringify(await response.json())
    } catch {
      body = ''
    }

    throw refusal(response.status, response.statusText, body, response.headers?.get?.('Retry-After'))
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}
