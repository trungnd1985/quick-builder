<script setup lang="ts">
// 017 US5 (FR-028, FR-030): the offer to keep a change's old addresses working. Shown only
// when the change moves live addresses, and ticked by default, because a broken link is the
// worse surprise. Any member who may make the change may accept it.
import { useI18n } from 'vue-i18n'

defineProps<{
  id: string
  /** How many live addresses the change moves, when known; 0 hides the offer. */
  count: number
}>()

const accepted = defineModel<boolean>({ required: true })
const { t } = useI18n()
</script>

<template>
  <label
    v-if="count > 0"
    class="checkbox redirect-offer"
  >
    <input
      :id="id"
      v-model="accepted"
      type="checkbox"
    >
    {{ t('redirects.offer.label', { count }) }}
  </label>
</template>
