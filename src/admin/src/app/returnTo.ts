import type { RouteLocationRaw, Router } from 'vue-router'

/** Screens that are never a destination after signing in. */
const NOT_A_DESTINATION = new Set(['sign-in', 'setup', 'set-password'])

/**
 * Where to go after signing in (002 FR-019): the admin route the user originally asked
 * for, and only that.
 *
 * `returnTo` arrives in the address bar, so anyone can craft it. It is honoured only when
 * it is a path inside this app that the router actually knows: an absolute URL, a
 * protocol-relative `//host/...` or an unknown path falls back to the site list. That is
 * what stops a sign-in link from bouncing a user to someone else's site.
 */
export function safeReturnTo(router: Router, returnTo: unknown): RouteLocationRaw {
  const fallback: RouteLocationRaw = { name: 'sites' }

  if (typeof returnTo !== 'string' || !returnTo.startsWith('/') || returnTo.startsWith('//') || returnTo.includes('\\')) {
    return fallback
  }

  const resolved = router.resolve(returnTo)
  const known = resolved.matched.length > 0 && resolved.name !== undefined
  if (!known || NOT_A_DESTINATION.has(String(resolved.name))) {
    return fallback
  }

  return resolved.fullPath
}
