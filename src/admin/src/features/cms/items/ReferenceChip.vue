<script setup lang="ts">
// A chosen item, by name, marked when it is not published or no longer exists (015 FR-017).
import { useI18n } from 'vue-i18n'
import type { RefState } from './itemRefs'

defineProps<{ id: string; state: RefState }>()
const { t } = useI18n()
</script>

<template>
  <span class="reference-chip">
    <template v-if="state.kind === 'found'">
      <span class="reference-chip__name">{{ state.item.name ?? state.item.slug }}</span>
      <span
        v-if="state.item.status === 'draft'"
        class="tag"
      >{{ t('cms.items.status.notPublished') }}</span>
    </template>
    <template v-else-if="state.kind === 'missing'">
      <code>{{ id }}</code>
      <span class="tag tag--danger">{{ t('cms.items.status.missing') }}</span>
    </template>
    <code v-else>{{ id }}</code>
  </span>
</template>
