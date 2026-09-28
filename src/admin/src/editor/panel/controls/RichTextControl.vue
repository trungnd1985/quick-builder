<script setup lang="ts">
// Rich text is edited on the page, where it is seen as it will look; the panel says so and
// opens it there.
import { useI18n } from 'vue-i18n'

const props = defineProps<{ prop: string; id: string; label: string; nodeId: string; error?: string }>()
const { t } = useI18n()

function editOnPage(): void {
  // Node ids are [A-Za-z0-9_-], so they need no escaping in a selector.
  document.querySelector(`[data-node-id="${props.nodeId}"] .qb-rich-text`)?.dispatchEvent(new CustomEvent('qb-edit'))
}
</script>

<template>
  <div
    class="editor-field"
    :data-prop="prop"
  >
    <span class="editor-field__label">{{ label }}</span>
    <p class="help">
      {{ t('editor.panel.richText.help') }}
    </p>
    <button
      type="button"
      @click="editOnPage"
    >
      {{ t('editor.panel.richText.edit') }}
    </button>
    <p
      v-if="error"
      class="field-error"
      role="alert"
    >
      {{ error }}
    </p>
  </div>
</template>
