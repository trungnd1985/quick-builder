<script setup lang="ts">
// What a check found in an archive, before anything changes (020 contracts/admin-transfer.md
// §3; FR-016): the archive, the versions to upgrade, problems (errors block, warnings need
// accepting), what will change, and past schedules. Lists show at most 1,000 entries each.
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { CheckResult } from '../../api/transfers'

const props = defineProps<{ result: CheckResult; mode: 'new_site' | 'merge' | 'restore' }>()
const { t, te, locale } = useI18n()

const dateTime = (iso: string) => new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(iso))
const errors = computed(() => props.result.problems.filter((p) => p.severity === 'error'))
const warnings = computed(() => props.result.problems.filter((p) => p.severity === 'warning'))
const counts = computed(() =>
  Object.entries(props.result.counts ?? {})
    .filter(([kind]) => te(`transfer.kinds.${kind}`))
    .map(([kind, count]) => t(`transfer.kinds.${kind}`, { count }, count)),
)

function problem(code: string, detail: string): string {
  const key = `transfer.problems.${code.slice(code.lastIndexOf('.') + 1)}`
  return te(key) ? t(key, { id: '' }) : detail
}
</script>

<template>
  <div class="import-check">
    <section aria-labelledby="check-archive">
      <h3 id="check-archive">
        {{ t('transfer.check.archive') }}
      </h3>
      <p v-if="result.archive">
        {{ t('transfer.check.archiveBody', { name: result.archive.source.siteName, release: result.archive.release, when: dateTime(result.archive.createdAtUtc) }) }}
      </p>
      <p v-if="result.archive?.sameSiteAsTarget">
        {{ t('transfer.check.sameSite') }}
      </p>
      <p v-if="counts.length">
        {{ counts.join(', ') }}
      </p>
    </section>

    <section
      v-if="result.versions.length"
      aria-labelledby="check-versions"
    >
      <h3 id="check-versions">
        {{ t('transfer.check.versions') }}
      </h3>
      <ul>
        <li
          v-for="v in result.versions"
          :key="v.contract"
        >
          {{ t('transfer.check.upgrade', { contract: v.contract, from: v.archiveVersion, to: v.currentVersion }) }}
        </li>
      </ul>
    </section>

    <section
      v-if="errors.length"
      aria-labelledby="check-errors"
    >
      <h3 id="check-errors">
        {{ t('transfer.check.errors', { count: errors.length }, errors.length) }}
      </h3>
      <ul class="error">
        <li
          v-for="(p, i) in errors"
          :key="`e${i}`"
        >
          <strong v-if="p.subject">{{ p.subject.label }}: </strong>{{ problem(p.code, p.detail) }}
        </li>
      </ul>
    </section>

    <section
      v-if="warnings.length"
      aria-labelledby="check-warnings"
    >
      <h3 id="check-warnings">
        {{ t('transfer.check.warnings', { count: warnings.length }, warnings.length) }}
      </h3>
      <ul>
        <li
          v-for="(p, i) in warnings"
          :key="`w${i}`"
        >
          <strong v-if="p.subject">{{ p.subject.label }}: </strong>{{ problem(p.code, p.detail) }}
        </li>
      </ul>
    </section>

    <section
      v-if="mode === 'new_site' && result.hostNamesLeftOut.length"
      aria-labelledby="check-hosts"
    >
      <h3 id="check-hosts">
        {{ t('transfer.check.hostNamesLeftOut') }}
      </h3>
      <ul>
        <li
          v-for="h in result.hostNamesLeftOut"
          :key="h"
        >
          {{ h }}
        </li>
      </ul>
    </section>

    <section
      v-if="result.conflicts.length"
      aria-labelledby="check-conflicts"
    >
      <h3 id="check-conflicts">
        {{ t('transfer.check.conflicts', { count: result.totals.conflicts ?? result.conflicts.length }, result.totals.conflicts ?? result.conflicts.length) }}
      </h3>
      <ul>
        <li
          v-for="c in result.conflicts"
          :key="c.archive.id"
        >
          {{ c.archive.label }}
        </li>
      </ul>
    </section>

    <section
      v-if="result.pastSchedules.length"
      aria-labelledby="check-schedules"
    >
      <h3 id="check-schedules">
        {{ t('transfer.check.pastSchedules') }}
      </h3>
      <ul>
        <li
          v-for="(s, i) in result.pastSchedules"
          :key="`s${i}`"
        >
          {{ t('transfer.check.pastSchedule', { label: s.subject.label, when: dateTime(s.atUtc) }) }}
        </li>
      </ul>
    </section>
  </div>
</template>

<style scoped>
.import-check {
  display: grid;
  gap: 1rem;
}

.import-check h3 {
  margin: 0 0 0.25rem;
}
</style>
