<script setup lang="ts">
// An item's status, beside its pending schedules and whether it needs attention (015 FR-004).
import { useI18n } from 'vue-i18n'
import type { ItemSummary } from '../../../api/items'

defineProps<{ item: ItemSummary & { scheduledDiffersFromDraft?: boolean } }>()
const { t, d } = useI18n()
</script>

<template>
  <span class="item-status">
    <span
      class="tag"
      :class="`tag--${item.status}`"
    >{{ t(`cms.items.status.${item.status}`) }}</span>
    <span
      v-if="item.attention"
      class="tag tag--danger"
    >{{ t('cms.items.status.attention') }}</span>
    <span
      v-for="schedule in item.schedules"
      :key="schedule.action"
      class="tag tag--scheduled"
    >{{ t(`cms.items.status.${schedule.action}Scheduled`, { when: d(new Date(schedule.dueAtUtc), 'short') }) }}</span>
    <span
      v-if="item.scheduledDiffersFromDraft"
      class="help"
    >{{ t('cms.items.status.differsFromScheduled') }}</span>
  </span>
</template>
