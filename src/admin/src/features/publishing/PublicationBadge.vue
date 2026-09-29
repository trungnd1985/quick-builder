<script setup lang="ts">
// Whether visitors see a page or component, at a glance (012 FR-007, research PR-20): draft
// only, published, published with changes, scheduled, or a schedule that failed.
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Publication } from '../../api/publishing'

const props = defineProps<{ publication: Publication | null | undefined }>()

const { t, d } = useI18n()

type Tone = 'draft' | 'live' | 'changed' | 'scheduled' | 'failed'

const badges = computed((): { tone: Tone; label: string; title?: string }[] => {
  const publication = props.publication
  if (!publication) return []

  const result: { tone: Tone; label: string; title?: string }[] = []

  if (publication.state === 'draft') {
    result.push({ tone: 'draft', label: t('publishing.badge.draft') })
  } else if (publication.hasUnpublishedChanges) {
    result.push({ tone: 'changed', label: t('publishing.badge.changed') })
  } else {
    result.push({ tone: 'live', label: t('publishing.badge.published') })
  }

  for (const schedule of publication.pendingSchedules) {
    const when = d(new Date(schedule.dueAtUtc), 'short')
    const overdue = new Date(schedule.dueAtUtc).getTime() < Date.now()
    result.push({
      tone: 'scheduled',
      label: t(overdue ? `publishing.badge.overdue.${schedule.action}` : `publishing.badge.scheduled.${schedule.action}`, { when }),
    })
  }

  if (publication.failedSchedule) {
    result.push({
      tone: 'failed',
      label: t('publishing.badge.failed'),
      title: t(`publishing.errors.pages.${(publication.failedSchedule.failureCode ?? 'pages.unknown').replace(/^pages\./, '')}`, { count: 0 }),
    })
  }

  return result
})
</script>

<template>
  <span class="publication-badges">
    <span
      v-for="badge in badges"
      :key="badge.label"
      class="publication-badge"
      :class="`publication-badge--${badge.tone}`"
      :title="badge.title"
    >{{ badge.label }}</span>
  </span>
</template>

<style scoped>
.publication-badges {
  display: inline-flex;
  flex-wrap: wrap;
  gap: 0.25rem;
}

.publication-badge {
  display: inline-block;
  padding: 0.05rem 0.45rem;
  border-radius: 999px;
  font-size: 0.75rem;
  line-height: 1.4;
  border: 1px solid currentColor;
  white-space: nowrap;
}

.publication-badge--draft { color: #4b5563; }
.publication-badge--live { color: #047857; }
.publication-badge--changed { color: #b45309; }
.publication-badge--scheduled { color: #1d4ed8; }
.publication-badge--failed { color: #b91c1c; }
</style>
