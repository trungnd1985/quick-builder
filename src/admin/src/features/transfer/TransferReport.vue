<script setup lang="ts">
// What a finished import did (020 data-model §4; FR-030): counts, what was left out and why,
// dropped schedules, and a link to the site.
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ImportReport } from '../../api/transfers'

const props = defineProps<{ report: ImportReport }>()
const { t, te } = useI18n()

const counts = computed(() =>
  Object.entries(props.report.imported ?? {})
    .filter(([kind]) => te(`transfer.kinds.${kind}`))
    .map(([kind, count]) => t(`transfer.kinds.${kind}`, { count }, count)),
)
</script>

<template>
  <section
    class="transfer-report"
    aria-labelledby="report-heading"
  >
    <h3 id="report-heading">
      {{ t('transfer.report.heading') }}
    </h3>
    <p v-if="counts.length">
      {{ t('transfer.report.imported', { what: counts.join(', ') }) }}
    </p>
    <template v-if="report.leftOut.length">
      <h4>{{ t('transfer.report.leftOut') }}</h4>
      <ul>
        <li
          v-for="(l, i) in report.leftOut"
          :key="`l${i}`"
        >
          {{ l.subject.label }} — {{ te(`transfer.report.reasons.${l.reason}`) ? t(`transfer.report.reasons.${l.reason}`) : l.reason }}
        </li>
      </ul>
    </template>
    <template v-if="report.droppedSchedules.length">
      <h4>{{ t('transfer.report.droppedSchedules') }}</h4>
      <ul>
        <li
          v-for="(d, i) in report.droppedSchedules"
          :key="`d${i}`"
        >
          {{ d.subject.label }}
        </li>
      </ul>
    </template>
    <p v-if="report.historicalAuthors > 0">
      {{ t('transfer.report.historicalAuthors', { count: report.historicalAuthors }, report.historicalAuthors) }}
    </p>
    <RouterLink
      class="button-link primary"
      :to="{ name: 'site', params: { siteId: report.siteId } }"
    >
      {{ t('transfer.report.openSite') }}
    </RouterLink>
  </section>
</template>

<style scoped>
.transfer-report {
  display: grid;
  gap: 0.5rem;
}
</style>
