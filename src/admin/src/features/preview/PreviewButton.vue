<script setup lang="ts">
// Opens the rendered draft in a new tab (022 User Story 1, FR-003, research PV-14). Saves
// pending changes first, so the preview never shows an older state than the editor; when the
// save is refused, nothing opens and the editor's own save error says why.
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'

const props = defineProps<{
  /** The preview address (previewUrls.ts). */
  url: string
  /** Saves pending changes; resolves true when what is on screen is saved. */
  beforeOpen?: () => Promise<boolean>
  disabled?: boolean
  /** Why the button is disabled, shown as its description. */
  disabledReason?: string | null
}>()

const { t } = useI18n()
const busy = ref(false)

async function open(): Promise<void> {
  if (busy.value) return
  busy.value = true
  try {
    const saved = props.beforeOpen ? await props.beforeOpen() : true
    if (saved) window.open(props.url, '_blank', 'noopener')
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <button
    type="button"
    :disabled="disabled || busy"
    :title="disabled && disabledReason ? disabledReason : undefined"
    @click="open"
  >
    {{ t('preview.open') }}
  </button>
</template>
