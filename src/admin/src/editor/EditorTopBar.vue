<script setup lang="ts">
// The editor's own top bar: where you came from, what you are editing, whether it is saved,
// and the document-wide actions.
import { useI18n } from 'vue-i18n'
import SaveStatus from './SaveStatus.vue'
import type { SaveState } from './state/autosave'

defineProps<{
  title: string
  backTo: { name: string; params: Record<string, string> }
  backLabel: string
  saveState: SaveState
  canSave: boolean
}>()
const emit = defineEmits<{ save: []; retry: [] }>()
const { t } = useI18n()
</script>

<template>
  <header class="editor-top-bar">
    <RouterLink
      :to="backTo"
      class="editor-top-bar__back"
    >
      {{ backLabel }}
    </RouterLink>
    <h1 class="editor-top-bar__title">
      {{ title }}
    </h1>
    <div class="editor-top-bar__actions">
      <slot />
      <SaveStatus
        :state="saveState"
        @retry="emit('retry')"
      />
      <button
        type="button"
        class="primary"
        :disabled="!canSave"
        :aria-keyshortcuts="'Control+S'"
        @click="emit('save')"
      >
        {{ t('editor.save.saveNow') }}
      </button>
    </div>
  </header>
</template>
