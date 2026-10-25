<script setup lang="ts">
// A small marker in a list: someone is editing this document, and since when (023 FR-012).
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ListHolder } from './usePresenceList'
import './editing.css'

const props = defineProps<{ holder: ListHolder }>()
const { t, locale } = useI18n()

const since = computed(() => new Intl.DateTimeFormat(locale.value, { timeStyle: 'short' }).format(new Date(props.holder.since)))
const label = computed(() =>
  t(props.holder.stale ? 'editing.marker.stale' : 'editing.marker.editing', { name: props.holder.holder.displayName, since: since.value }),
)
</script>

<template>
  <span
    class="editing-marker"
    :class="{ 'editing-marker--stale': holder.stale }"
    :title="label"
  >
    <span class="editing-marker__text">{{ label }}</span>
  </span>
</template>
