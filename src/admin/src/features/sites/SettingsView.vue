<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '../../app/authStore'
import AboutInstallationPanel from '../settings/AboutInstallationPanel.vue'
import TestEmailPanel from '../settings/TestEmailPanel.vue'

// FR-042 required a placeholder screen; 018 FR-037 gives it its first content: outgoing email,
// for Administrators. 019 FR-006 adds the release this installation runs, also for Administrators.
const { t } = useI18n()
const auth = useAuthStore()
</script>

<template>
  <section aria-labelledby="settings-heading">
    <h1 id="settings-heading">
      {{ t('settings.title') }}
    </h1>
    <template v-if="auth.isAdministrator">
      <AboutInstallationPanel />
      <TestEmailPanel />
    </template>
    <p
      v-else
      class="subtitle"
    >
      {{ t('settings.placeholder') }}
    </p>
  </section>
</template>
