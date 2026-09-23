<script setup lang="ts">
import { useI18n } from 'vue-i18n'

// FR-042 requires at least one placeholder screen. This is it: a second route proves
// client-side routing and the deep-link fallback work, without inventing functionality
// no spec asks for.
const { t } = useI18n()
</script>

<template>
  <section aria-labelledby="settings-heading">
    <h1 id="settings-heading">
      {{ t('settings.title') }}
    </h1>
    <p class="subtitle">
      {{ t('settings.placeholder') }}
    </p>
  </section>
</template>
