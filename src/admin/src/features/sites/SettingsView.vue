<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '../../app/authStore'
import TestEmailPanel from '../settings/TestEmailPanel.vue'

// FR-042 required a placeholder screen; 018 FR-037 gives it its first content: outgoing email,
// for Administrators.
const { t } = useI18n()
const auth = useAuthStore()
</script>

<template>
  <section aria-labelledby="settings-heading">
    <h1 id="settings-heading">
      {{ t('settings.title') }}
    </h1>
    <TestEmailPanel v-if="auth.isAdministrator" />
    <p
      v-else
      class="subtitle"
    >
      {{ t('settings.placeholder') }}
    </p>
  </section>
</template>
