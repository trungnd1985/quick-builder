<script setup lang="ts">
// Which release this installation runs, and which the database records (019 FR-006).
// Administrators only: version numbers are never shown to anyone else.
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { installationApi, type Installation } from '../../api/installation'

const { t } = useI18n()
const installation = ref<Installation | null>(null)

onMounted(async () => {
  installation.value = await installationApi.get().catch(() => null)
})
</script>

<template>
  <section
    class="card"
    aria-labelledby="about-installation-heading"
  >
    <h2 id="about-installation-heading">
      {{ t('settings.about.heading') }}
    </h2>
    <dl v-if="installation">
      <dt>{{ t('settings.about.version') }}</dt>
      <dd>
        {{ installation.revision
          ? t('settings.about.runningWithRevision', { version: installation.version, revision: installation.revision })
          : t('settings.about.running', { version: installation.version }) }}
      </dd>
      <dt>{{ t('settings.about.database') }}</dt>
      <dd>{{ installation.databaseVersion ?? t('settings.about.databaseNone') }}</dd>
    </dl>
    <p
      v-if="installation?.upgradingTo"
      role="status"
    >
      {{ t('settings.about.upgrading', { version: installation.upgradingTo }) }}
    </p>
    <p v-if="installation?.releaseNotesUrl">
      <a
        :href="installation.releaseNotesUrl"
        target="_blank"
        rel="noopener noreferrer"
      >{{ t('settings.about.releaseNotes') }}</a>
    </p>
  </section>
</template>
