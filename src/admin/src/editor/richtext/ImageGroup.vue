<script setup lang="ts">
// The selected rich text image's settings (035 US3, FR-010, research RT-17): its alternative
// text, or "decorative" for an image that adds nothing a screen reader should say; replacing
// its file; removing it. Publishing refuses an image with neither (FR-014).
import { computed, getCurrentInstance } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Editor } from '@tiptap/core'

const props = defineProps<{
  editor: Editor
  /** Bumped by the toolbar on every transaction, so the fields follow the selection. */
  tick: number
}>()
const emit = defineEmits<{ replace: [] }>()
const { t } = useI18n()
const id = `rt-image-${getCurrentInstance()?.uid ?? 0}`

const attrs = computed(() => {
  void props.tick
  return props.editor.getAttributes('image')
})
const alt = computed(() => (typeof attrs.value.alt === 'string' ? attrs.value.alt : ''))
const decorative = computed(() => attrs.value.decorative === true)

function setAlt(event: Event): void {
  const value = (event.target as HTMLInputElement).value
  props.editor.commands.updateAttributes('image', { alt: value === '' ? null : value })
}

function setDecorative(event: Event): void {
  props.editor.commands.updateAttributes('image', { decorative: (event.target as HTMLInputElement).checked })
}

function remove(): void {
  props.editor.chain().focus().deleteSelection().run()
}
</script>

<template>
  <div
    class="qb-rt-toolbar__image"
    role="group"
    :aria-label="t('editor.richText.image.settings')"
  >
    <label :for="`${id}-alt`">{{ t('editor.richText.image.alt') }}</label>
    <input
      :id="`${id}-alt`"
      type="text"
      maxlength="300"
      :value="alt"
      :disabled="decorative"
      :aria-describedby="`${id}-help`"
      @input="setAlt"
    >
    <span
      :id="`${id}-help`"
      class="help"
    >{{ t('editor.richText.image.altHelp') }}</span>
    <label class="qb-rt-toolbar__check">
      <input
        type="checkbox"
        :checked="decorative"
        @change="setDecorative"
      >
      {{ t('editor.richText.image.decorative') }}
    </label>
    <button
      type="button"
      @mousedown.prevent
      @click="emit('replace')"
    >
      {{ t('editor.richText.image.replace') }}
    </button>
    <button
      type="button"
      class="danger"
      @mousedown.prevent
      @click="remove"
    >
      {{ t('editor.richText.image.remove') }}
    </button>
  </div>
</template>
