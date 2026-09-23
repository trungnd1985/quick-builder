// Typed client for the admin surface (contracts/admin-api.yaml).
//
// Endpoints carry their version from the first one (FR-010a): /api/v1/<module>/...
// The version lives here, once, so a later bump is a one-line change rather than a
// find-and-replace across every screen.

const API_ROOT = '/api/v1'

export interface Site {
  id: string
  name: string
  hostNames: string[]
  createdAtUtc: string
}

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
  // Declared explicitly rather than as a constructor parameter property: the TypeScript
  // config runs with erasableSyntaxOnly, which rules out syntax that emits runtime code.
  readonly problem?: ProblemDetails

  constructor(message: string, problem?: ProblemDetails) {
    super(message)
    this.name = 'ApiError'
    this.problem = problem
  }

  /** Per-field validation messages, when the failure was a validation failure. */
  get fieldErrors(): Record<string, string[]> {
    return this.problem?.errors ?? {}
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_ROOT}${path}`, {
    ...init,
    headers: {
      'content-type': 'application/json',
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
    throw new ApiError(problem?.detail ?? problem?.title ?? response.statusText, problem)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

export const sitesApi = {
  list: (): Promise<Site[]> => request<Site[]>('/sites'),

  create: (name: string, hostNames: string[]): Promise<Site> =>
    request<Site>('/sites', {
      method: 'POST',
      body: JSON.stringify({ name, hostNames }),
    }),
}
