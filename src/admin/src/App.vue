<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { identityApi } from './api/identity'
import { useAuthStore } from './app/authStore'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

async function signOut(): Promise<void> {
  try {
    await identityApi.signOut()
  } finally {
    // Signed out locally whatever the server said: the session is gone either way.
    auth.signedOut()
    await router.push({ name: 'sign-in' })
  }
}
</script>

<template>
  <!--
    Screens before anyone is signed in (setup, and later sign-in and set-password) render
    without the navigation frame: there is nothing to navigate to yet.
  -->
  <main
    v-if="route.meta.bare"
    id="main"
    class="bare"
    tabindex="-1"
  >
    <RouterView />
  </main>

  <template v-else>
    <!--
      The navigation frame every later admin screen inherits (FR-042).

      Accessibility baseline (FR-045b, docs/admin-accessibility-baseline.md):
      a skip link ahead of the navigation, landmark elements, aria-current on the active
      link, and a visible focus indicator defined in the stylesheet rather than suppressed.
    -->
    <a
      class="skip-link"
      href="#main"
    >{{ t('app.skipToContent') }}</a>

    <div class="shell">
      <header class="shell-header">
        <span class="brand">{{ t('app.title') }}</span>
        <div
          v-if="auth.user"
          class="user-menu"
        >
          <span>{{ t('app.userMenu.signedInAs', { name: auth.user.displayName }) }}</span>
          <RouterLink to="/account">
            {{ t('app.userMenu.account') }}
          </RouterLink>
          <button
            type="button"
            @click="signOut"
          >
            {{ t('app.userMenu.signOut') }}
          </button>
        </div>
      </header>

      <div class="shell-body">
        <nav
          class="shell-nav"
          :aria-label="t('app.nav.sites')"
        >
          <ul>
            <li>
              <RouterLink to="/sites">
                {{ t('app.nav.sites') }}
              </RouterLink>
            </li>
            <li>
              <RouterLink to="/settings">
                {{ t('app.nav.settings') }}
              </RouterLink>
            </li>
            <!-- Courtesy only: the server refuses non-administrators regardless (FR-024). -->
            <li v-if="auth.isAdministrator">
              <RouterLink to="/users">
                {{ t('app.nav.users') }}
              </RouterLink>
            </li>
          </ul>
        </nav>

        <main
          id="main"
          class="shell-main"
          tabindex="-1"
        >
          <RouterView />
        </main>
      </div>
    </div>
  </template>
</template>
