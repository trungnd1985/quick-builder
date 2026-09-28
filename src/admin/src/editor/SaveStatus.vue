<script setup lang="ts">
// Whether the document is saved, saving, has unsaved changes, or could not be saved — in
// words, in a polite live region (FR-026). While retrying it offers Retry now (FR-027).
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { SaveState } from './state/autosave'

const props = defineProps<{ state: SaveState }>()
const emit = defineEmits<{ retry: [] }>()
const { t } = useI18n()

const message = computed(() => t(`editor.save.state.${props.state}`))
</script>

<template>
  <div
    class="editor-save-status"
    :class="`editor-save-status--${state}`"
  >
    <span
      role="status"
      aria-live="polite"
    >{{ message }}</span>
    <button
      v-if="state === 'retrying'"
      type="button"
      class="link"
      @click="emit('retry')"
    >
      {{ t('editor.save.retryNow') }}
    </button>
  </div>
</template>
