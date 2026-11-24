import { createRouter, createWebHistory, type Router, type RouterHistory } from 'vue-router'
import SitesView from '../features/sites/SitesView.vue'
import SiteDetailView from '../features/sites/SiteDetailView.vue'
import MediaLibraryView from '../features/media/MediaLibraryView.vue'
import PageTreeView from '../features/pages/PageTreeView.vue'
import ComponentsView from '../features/pages/ComponentsView.vue'
import SettingsView from '../features/sites/SettingsView.vue'
import StorageSettingsView from '../features/settings/StorageSettingsView.vue'
import SetupView from '../features/identity/SetupView.vue'
import SignInView from '../features/identity/SignInView.vue'
import SetPasswordView from '../features/identity/SetPasswordView.vue'
import AccountView from '../features/identity/AccountView.vue'
import UsersView from '../features/users/UsersView.vue'
import UserEditView from '../features/users/UserEditView.vue'
import { useAuthStore } from './authStore'
import { ADMIN_BASE } from './base'

declare module 'vue-router' {
  interface RouteMeta {
    /** Shown without the navigation frame, in a narrow column: the screens before anyone is signed in. */
    bare?: boolean
    /** Shown without the navigation frame, across the whole window: the editor (011). */
    fullscreen?: boolean
    /** Reachable without a session. */
    public?: boolean
    /** Offered only to administrators. A courtesy: the server enforces access (FR-024). */
    administrators?: boolean
  }
}

/**
 * Client-side routing for the admin shell (FR-042).
 *
 * The history base is the admin's own address (base.ts): `/admin/`, under the installation's
 * base path when one is set, matching the host's SPA fallback. They have to agree: the host returns index.html for any /admin/* path (FR-043), so a
 * deep link or a refresh lands here rather than 404ing, and the router then takes over.
 *
 * The guard decides which screen to show — setup while unclaimed (002 US1), sign-in while
 * signed out (002 US2), remembering where the user was going. The server decides every
 * request regardless (002 FR-024).
 */
export function createAppRouter(history: RouterHistory = createWebHistory(ADMIN_BASE)): Router {
  const router = createRouter({
    history,
    routes: [
      { path: '/', redirect: '/sites' },
      { path: '/setup', name: 'setup', component: SetupView, meta: { bare: true, public: true } },
      { path: '/sign-in', name: 'sign-in', component: SignInView, meta: { bare: true, public: true } },
      { path: '/set-password', name: 'set-password', component: SetPasswordView, meta: { bare: true, public: true } },
      { path: '/sites', name: 'sites', component: SitesView },
      // 020: import an archive as a new site; before /sites/:siteId so 'import' is not an id.
      { path: '/sites/import', name: 'site-import', component: () => import('../features/transfer/ImportSiteView.vue') },
      { path: '/sites/:siteId', name: 'site', component: SiteDetailView },
      { path: '/sites/:siteId/media', name: 'site-media', component: MediaLibraryView },
      { path: '/sites/:siteId/pages', name: 'site-pages', component: PageTreeView },
      { path: '/sites/:siteId/components', name: 'site-components', component: ComponentsView },
      // 014: collections and the schema builder.
      { path: '/sites/:siteId/collections', name: 'site-collections', component: () => import('../features/cms/CollectionsView.vue') },
      { path: '/sites/:siteId/collections/:collectionId', name: 'site-collection', component: () => import('../features/cms/CollectionBuilderView.vue') },
      // 015: collection items.
      { path: '/sites/:siteId/collections/:collectionId/items', name: 'site-collection-items', component: () => import('../features/cms/items/ItemListView.vue') },
      { path: '/sites/:siteId/collections/:collectionId/items/new', name: 'site-collection-item-new', component: () => import('../features/cms/items/ItemFormView.vue') },
      { path: '/sites/:siteId/collections/:collectionId/items/:itemId', name: 'site-collection-item', component: () => import('../features/cms/items/ItemFormView.vue') },
      // 011 US6: the site's design tokens. Loaded on demand, like the editor.
      { path: '/sites/:siteId/design', name: 'site-design', component: () => import('../features/design/DesignTokensView.vue') },
      // 017: Designers and Administrators manage redirects; the server checks regardless.
      { path: '/sites/:siteId/redirects', name: 'site-redirects', component: () => import('../features/seo/RedirectsView.vue') },
      // 021: the site's trash, one tab per kind of content.
      { path: '/sites/:siteId/trash', name: 'site-trash', component: () => import('../features/trash/TrashView.vue') },
      // 024: menus and their entries; Editors see them read-only, the server checks regardless.
      { path: '/sites/:siteId/menus', name: 'site-menus', component: () => import('../features/menus/MenusView.vue') },
      { path: '/sites/:siteId/menus/:menuId', name: 'site-menu', component: () => import('../features/menus/MenuEditorView.vue') },
      // 018: every site role reads submissions; the server decides who may delete them.
      { path: '/sites/:siteId/submissions', name: 'site-submissions', component: () => import('../features/forms/SubmissionsView.vue') },
      { path: '/sites/:siteId/submissions/spam', name: 'site-spam', component: () => import('../features/forms/SpamView.vue') },
      { path: '/sites/:siteId/submissions/:submissionId', name: 'site-submission', component: () => import('../features/forms/SubmissionDetail.vue') },
      // 011 ED-22: the editor loads on demand, so TipTap and the canvas stay out of the shell's
      // main chunk. It uses the whole window, without the navigation frame.
      {
        path: '/sites/:siteId/pages/:pageId/edit',
        name: 'page-editor',
        component: () => import('../editor/EditorView.vue'),
        props: (route) => ({ siteId: route.params.siteId, kind: 'page', documentId: route.params.pageId }),
        meta: { fullscreen: true },
      },
      {
        path: '/sites/:siteId/components/:componentId/edit',
        name: 'component-editor',
        component: () => import('../editor/EditorView.vue'),
        props: (route) => ({ siteId: route.params.siteId, kind: 'component', documentId: route.params.componentId }),
        meta: { fullscreen: true },
      },
      // 012 US3: publishing history, and a retained version opened read-only in the editor.
      {
        path: '/sites/:siteId/pages/:pageId/history',
        name: 'page-history',
        component: () => import('../features/publishing/HistoryView.vue'),
        props: (route) => ({ siteId: route.params.siteId, kind: 'page', subjectId: route.params.pageId }),
      },
      {
        path: '/sites/:siteId/components/:componentId/history',
        name: 'component-history',
        component: () => import('../features/publishing/HistoryView.vue'),
        props: (route) => ({ siteId: route.params.siteId, kind: 'component', subjectId: route.params.componentId }),
      },
      // 024: a menu's publishing history; its versions are restored, not opened in the editor.
      {
        path: '/sites/:siteId/menus/:menuId/history',
        name: 'menu-history',
        component: () => import('../features/publishing/HistoryView.vue'),
        props: (route) => ({ siteId: route.params.siteId, kind: 'menu', subjectId: route.params.menuId }),
      },
      {
        path: '/sites/:siteId/pages/:pageId/versions/:number(\\d+)',
        name: 'page-version',
        component: () => import('../editor/EditorView.vue'),
        props: (route) => ({ siteId: route.params.siteId, kind: 'page', documentId: route.params.pageId, versionNumber: Number(route.params.number) }),
        meta: { fullscreen: true },
      },
      {
        path: '/sites/:siteId/components/:componentId/versions/:number(\\d+)',
        name: 'component-version',
        component: () => import('../editor/EditorView.vue'),
        props: (route) => ({ siteId: route.params.siteId, kind: 'component', documentId: route.params.componentId, versionNumber: Number(route.params.number) }),
        meta: { fullscreen: true },
      },
      // 012 FR-017: the site's publish log. Administrators only; the server checks regardless.
      {
        path: '/sites/:siteId/publish-log',
        name: 'site-publish-log',
        component: () => import('../features/publishing/PublishLogView.vue'),
        meta: { administrators: true },
      },
      // 022 FR-015: every active preview link of the site. Administrators only; the server checks regardless.
      {
        path: '/sites/:siteId/preview-links',
        name: 'site-preview-links',
        component: () => import('../features/preview/PreviewLinksView.vue'),
        meta: { administrators: true },
      },
      { path: '/settings', name: 'settings', component: SettingsView },
      // 013 MV-21: the storage provider and its consistency check. Administrators only.
      { path: '/settings/storage', name: 'settings-storage', component: StorageSettingsView, meta: { administrators: true } },
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
