import { createRouter, createWebHistory, type Router, type RouterHistory } from 'vue-router'
import SitesView from '../features/sites/SitesView.vue'
import SiteDetailView from '../features/sites/SiteDetailView.vue'
import MediaLibraryView from '../features/media/MediaLibraryView.vue'
import PageTreeView from '../features/pages/PageTreeView.vue'
import ComponentsView from '../features/pages/ComponentsView.vue'
import SettingsView from '../features/sites/SettingsView.vue'
import SetupView from '../features/identity/SetupView.vue'
import SignInView from '../features/identity/SignInView.vue'
import SetPasswordView from '../features/identity/SetPasswordView.vue'
import AccountView from '../features/identity/AccountView.vue'
import UsersView from '../features/users/UsersView.vue'
import UserEditView from '../features/users/UserEditView.vue'
import { useAuthStore } from './authStore'

declare module 'vue-router' {
  interface RouteMeta {
    /** Shown without the navigation frame: the screens before anyone is signed in, and the editor. */
    bare?: boolean
    /** Reachable without a session. */
    public?: boolean
    /** Offered only to administrators. A courtesy: the server enforces access (FR-024). */
    administrators?: boolean
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
      { path: '/set-password', name: 'set-password', component: SetPasswordView, meta: { bare: true, public: true } },
      { path: '/sites', name: 'sites', component: SitesView },
      { path: '/sites/:siteId', name: 'site', component: SiteDetailView },
      { path: '/sites/:siteId/media', name: 'site-media', component: MediaLibraryView },
      { path: '/sites/:siteId/pages', name: 'site-pages', component: PageTreeView },
      { path: '/sites/:siteId/components', name: 'site-components', component: ComponentsView },
      // 011 US6: the site's design tokens. Loaded on demand, like the editor.
      { path: '/sites/:siteId/design', name: 'site-design', component: () => import('../features/design/DesignTokensView.vue') },
      // 011 ED-22: the editor loads on demand, so TipTap and the canvas stay out of the shell's
      // main chunk. It uses the whole window, without the navigation frame.
      {
        path: '/sites/:siteId/pages/:pageId/edit',
        name: 'page-editor',
        component: () => import('../editor/EditorView.vue'),
        props: (route) => ({ siteId: route.params.siteId, kind: 'page', documentId: route.params.pageId }),
        meta: { bare: true },
      },
      { path: '/settings', name: 'settings', component: SettingsView },
      { path: '/account', name: 'account', component: AccountView },
      { path: '/users', name: 'users', component: UsersView, meta: { administrators: true } },
      { path: '/users/:id', name: 'user', component: UserEditView, meta: { administrators: true } },
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

    if (to.meta.administrators && !auth.isAdministrator) {
      return { name: 'sites' }
    }

    return true
  })

  return router
}

export const router = createAppRouter()
