<script setup lang="ts">
// A rich text field: the editor's TipTap set-up, standalone (015 FR-009, research IT-19). It
// edits and emits the contract's structured document only, so pasted content keeps only what
// rtDoc allows (Principle IX). An editor with no text is no value.
import { onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import type { Editor, JSONContent } from '@tiptap/core'
import type { SchemaField } from '../../../../api/cms'
import RichTextToolbar from '../../../../editor/richtext/RichTextToolbar.vue'
import { createRichTextEditor } from '../../../../editor/richtext/tiptap'
import { fromContractRichText, toContractRichText, type RichTextDoc } from '../../../../editor/richtext/toContractRichText'
import { richTextChars } from '../../../../editor/schema/documentValidator'
import type { Json } from '../../../../editor/schema/subsetValidator'

const props = defineProps<{ field: SchemaField; value: unknown; id: string; describedBy: string; invalid: boolean; siteId: string; label: string }>()
const emit = defineEmits<{ 'update:value': [value: unknown] }>()

const host = ref<HTMLElement | null>(null)
const editor = shallowRef<Editor | null>(null)

const isEmpty = (doc: RichTextDoc): boolean => richTextChars(doc as unknown as Json) === 0

onMounted(() => {
  if (!host.value) return
  editor.value = createRichTextEditor({
    element: host.value,
    content: props.value ?? null,
    label: props.label,
    onChange: (doc) => emit('update:value', isEmpty(doc) ? undefined : doc),
  })
  const body = host.value.querySelector<HTMLElement>('[role="textbox"]')
  body?.setAttribute('id', props.id)
  body?.setAttribute('aria-describedby', props.describedBy)
})

// The form put another value in (a reload, a copied version): show it.
watch(() => props.value, (next) => {
  const open = editor.value
  if (!open) return
  if (JSON.stringify(toContractRichText(open.getJSON())) !== JSON.stringify(fromContractRichText(next ?? null))) {
    open.commands.setContent(fromContractRichText(next ?? null) as JSONContent, { emitUpdate: false })
  }
})

watch(() => props.invalid, (invalid) => {
  host.value?.querySelector('[role="textbox"]')?.setAttribute('aria-invalid', String(invalid))
}, { flush: 'post' })

onBeforeUnmount(() => editor.value?.destroy())
</script>

<template>
  <div class="item-input item-rich-text">
    <RichTextToolbar
      v-if="editor"
      :editor="editor"
      :site-id="siteId"
    />
    <div
      ref="host"
      class="qb-rich-text item-rich-text__body"
    />
  </div>
</template>
