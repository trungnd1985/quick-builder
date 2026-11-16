<script setup lang="ts">
// What a delete says afterwards (021 FR-002, contracts/admin-trash.md §1): until when the
// content can be restored, from the server's receipt, and where to find it.
import { useI18n } from 'vue-i18n'

defineProps<{ siteId: string; purgeOnUtc: string }>()
const { t, d } = useI18n()
</script>

<template>
  <p
    class="notice"
    role="status"
  >
    {{ t('trash.movedToTrash', { date: d(new Date(purgeOnUtc), 'short') }) }}
    <RouterLink :to="{ name: 'site-trash', params: { siteId } }">
      {{ t('trash.openTrash') }}
    </RouterLink>
  </p>
</template>
