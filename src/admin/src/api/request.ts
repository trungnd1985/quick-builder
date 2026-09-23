// The one way the admin shell talks to the server (contracts/identity-api.yaml).
//
// Endpoints carry their version from the first one (FR-010a): /api/v1/<module>/...
// Everything cross-cutting about a call lives here, once, so no screen can forget it:
// the request header that proves a call came from this app (002 research R-11), sending
// the session cookie, and what to do when the server says "sign in" or "set up first".

const API_ROOT = '/api/v1'

export interface ProblemDetails {
  title: string
  status: number
  detail?: string
  errorCode: string
  traceId?: string
  errors?: Record<string, string[]>
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

export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const method = (init?.method ?? 'GET').toUpperCase()

  const response = await fetch(`${API_ROOT}${path}`, {
    ...init,
    credentials: 'same-origin',
    headers: {
      'content-type': 'application/json',
      // Browsers cannot add this to a cross-site request without a CORS preflight, which
      // the server never grants, so its presence proves the call came from this app.
      ...(SAFE_METHODS.has(method) ? {} : { 'X-QuickBuilder-Request': '1' }),
      ...(init?.headers ?? {}),
    },
  })

  if (!response.ok) {
    // A problem-details body is expected, but a proxy or a crash can produce something
    // else. Falling back to the status text keeps the screen useful either way.
    let problem: ProblemDetails | undefined
    try {
      problem = (await response.json()) as ProblemDetails
    } catch {
      problem = undefined
    }

    const retryAfter = Number(response.headers?.get?.('Retry-After'))
    const error = new ApiError(
      problem?.detail ?? problem?.title ?? response.statusText,
      response.status,
      problem,
      Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : undefined,
    )

    if (response.status === 401) {
      hooks.onUnauthenticated?.()
    } else if (response.status === 428) {
      hooks.onSetupRequired?.()
    }

    throw error
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}
