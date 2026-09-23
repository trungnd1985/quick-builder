import { createRouter, createWebHistory, type Router, type RouterHistory } from 'vue-router'
import SitesView from '../features/sites/SitesView.vue'
import SettingsView from '../features/sites/SettingsView.vue'
import SetupView from '../features/identity/SetupView.vue'
import { useAuthStore } from './authStore'

declare module 'vue-router' {
  interface RouteMeta {
    /** Shown without the navigation frame: the screens before anyone is signed in. */
    bare?: boolean
  }
}

/**
 * Client-side routing for the admin shell (FR-042).
 *
 * The history base is `/admin/`, matching Vite's `base` and the host's SPA fallback. All
 * three have to agree: the host returns index.html for any /admin/* path (FR-043), so a
 * deep link or a refresh lands here rather than 404ing, and the router then takes over.
 *
 * The guard sends a fresh installation to the setup wizard and keeps it away from the
 * wizard once claimed (002 US1). It decides which screen to show; the server decides
 * every request regardless (002 FR-024).
 */
export function createAppRouter(history: RouterHistory = createWebHistory('/admin/')): Router {
  const router = createRouter({
    history,
    routes: [
      { path: '/', redirect: '/sites' },
      { path: '/setup', name: 'setup', component: SetupView, meta: { bare: true } },
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

    if (auth.setupRequired && to.name !== 'setup') {
      return { name: 'setup' }
    }

    if (!auth.setupRequired && to.name === 'setup') {
      return { name: 'sites' }
    }

    return true
  })

  return router
}

export const router = createAppRouter()
