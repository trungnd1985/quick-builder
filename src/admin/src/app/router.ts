import { createRouter, createWebHistory } from 'vue-router'
import SitesView from '../features/sites/SitesView.vue'
import SettingsView from '../features/sites/SettingsView.vue'

/**
 * Client-side routing for the admin shell (FR-042).
 *
 * The history base is `/admin/`, matching Vite's `base` and the host's SPA fallback. All
 * three have to agree: the host returns index.html for any /admin/* path (FR-043), so a
 * deep link or a refresh lands here rather than 404ing, and the router then takes over.
 */
export const router = createRouter({
  history: createWebHistory('/admin/'),
  routes: [
    { path: '/', redirect: '/sites' },
    { path: '/sites', name: 'sites', component: SitesView },
    { path: '/settings', name: 'settings', component: SettingsView },
    // Anything unrecognised goes to the list rather than showing a dead end.
    { path: '/:pathMatch(.*)*', redirect: '/sites' },
  ],
})
