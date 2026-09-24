import { createRouter, createWebHistory, type Router, type RouterHistory } from 'vue-router'
import SitesView from '../features/sites/SitesView.vue'
import SettingsView from '../features/sites/SettingsView.vue'
import SetupView from '../features/identity/SetupView.vue'
import SignInView from '../features/identity/SignInView.vue'
import { useAuthStore } from './authStore'

declare module 'vue-router' {
  interface RouteMeta {
    /** Shown without the navigation frame: the screens before anyone is signed in. */
    bare?: boolean
    /** Reachable without a session. */
    public?: boolean
  }
}

/**
 * Client-side routing for the admin shell (FR-042).
 *
 * The history base is `/admin/`, matching Vite's `base` and the host's SPA fallback. All
 * three have to agree: the host returns index.html for any /admin/* path (FR-043), so a
 * deep link or a refresh lands here rather than 404ing, and the router then takes over.
 *
 * The guard decides which screen to show — setup while unclaimed (002 US1), sign-in while
 * signed out (002 US2), remembering where the user was going. The server decides every
 * request regardless (002 FR-024).
 */
export function createAppRouter(history: RouterHistory = createWebHistory('/admin/')): Router {
  const router = createRouter({
    history,
    routes: [
      { path: '/', redirect: '/sites' },
      { path: '/setup', name: 'setup', component: SetupView, meta: { bare: true, public: true } },
      { path: '/sign-in', name: 'sign-in', component: SignInView, meta: { bare: true, public: true } },
      { path: '/sites', name: 'sites', component: SitesView },
      { path: '/settings', name: 'settings', component: SettingsView },
      // Anything unrecognised goes to the list rather than showing a dead end.
      { path: '/:pathMatch(.*)*', redirect: '/sites' },
    ],
  })

  router.beforeEach(async (to) => {
    const auth = useAuthStore()

    try {
      await auth.bootstrap()
    } catch {
      // The server could not be reached. Let the screen load: it shows its own error
      // state, which says more than a blank redirect would.
      return true
    }

    if (auth.setupRequired) {
      return to.name === 'setup' ? true : { name: 'setup' }
    }

    if (to.name === 'setup') {
      return { name: 'sites' }
    }

    if (!auth.isSignedIn && !to.meta.public) {
      return { name: 'sign-in', query: { returnTo: to.fullPath } }
    }

    if (auth.isSignedIn && to.name === 'sign-in') {
      return { name: 'sites' }
    }

    return true
  })

  return router
}

export const router = createAppRouter()
