<script setup lang="ts">
// The formats a Rich Text block allows, and nothing else (FR-013): bold, italic, bulleted and
// numbered lists, and links to a page of the site or to an address.
import { ref, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Editor } from '@tiptap/core'
import LinkControl from '../panel/controls/LinkControl.vue'
import type { Json } from '../schema/subsetValidator'

const props = defineProps<{ editor: Editor; siteId: string }>()
const { t } = useI18n()

const linking = ref(false)
const linkValue = shallowRef<Json | undefined>(undefined)

function toggle(mark: 'bold' | 'italic' | 'bulletList' | 'orderedList'): void {
  const chain = props.editor.chain().focus()
  if (mark === 'bold') chain.toggleBold().run()
  else if (mark === 'italic') chain.toggleItalic().run()
  else if (mark === 'bulletList') chain.toggleBulletList().run()
  else chain.toggleOrderedList().run()
}

function openLink(): void {
  linkValue.value = (props.editor.getAttributes('link').to as Json | undefined) ?? undefined
  linking.value = true
}

function applyLink(value: Json | undefined): void {
  linkValue.value = value
  const chain = props.editor.chain().focus().extendMarkRange('link')
  if (value === undefined) chain.unsetMark('link').run()
  else chain.setMark('link', { to: value }).run()
}

const active = (name: string) => props.editor.isActive(name)
</script>

<template>
  <div
    class="qb-rt-toolbar"
    role="toolbar"
    :aria-label="t('editor.richText.toolbar')"
  >
    <button
      type="button"
      :aria-pressed="active('bold')"
      @mousedown.prevent
      @click="toggle('bold')"
    >
      {{ t('editor.richText.bold') }}
    </button>
    <button
      type="button"
      :aria-pressed="active('italic')"
      @mousedown.prevent
      @click="toggle('italic')"
    >
      {{ t('editor.richText.italic') }}
    </button>
    <button
      type="button"
      :aria-pressed="active('bulletList')"
      @mousedown.prevent
      @click="toggle('bulletList')"
    >
      {{ t('editor.richText.bulletList') }}
    </button>
    <button
      type="button"
      :aria-pressed="active('orderedList')"
      @mousedown.prevent
      @click="toggle('orderedList')"
    >
      {{ t('editor.richText.orderedList') }}
    </button>
    <button
      type="button"
      :aria-pressed="active('link')"
      :aria-expanded="linking"
      @mousedown.prevent
      @click="linking ? (linking = false) : openLink()"
    >
      {{ t('editor.richText.link') }}
    </button>
    <div
      v-if="linking"
      class="qb-rt-toolbar__link"
    >
      <LinkControl
        :site-id="siteId"
        :value="linkValue"
        :label="t('editor.richText.link')"
        @change="applyLink"
      />
    </div>
  </div>
</template>
