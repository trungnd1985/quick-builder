<script setup lang="ts">
// Desktop, tablet or mobile width for the canvas (011 US5, FR-036). Three toggle buttons in
// a group, each saying whether it is the one in use.
import { useI18n } from 'vue-i18n'
import { PREVIEW_WIDTHS, type PreviewWidth } from './state/usePreviewWidth'

const model = defineModel<PreviewWidth>({ required: true })
const { t } = useI18n()

const options = Object.keys(PREVIEW_WIDTHS) as PreviewWidth[]
</script>

<template>
  <div
    class="editor-preview-switch"
    role="group"
    :aria-label="t('editor.preview.label')"
  >
    <button
      v-for="option in options"
      :key="option"
      type="button"
      :aria-pressed="model === option"
      :title="t('editor.preview.width', { width: PREVIEW_WIDTHS[option] })"
      @click="model = option"
    >
      {{ t(`editor.preview.${option}`) }}
    </button>
  </div>
</template>
