<script setup lang="ts">
import { useI18n } from 'vue-i18n'

const { t } = useI18n()
</script>

<template>
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
