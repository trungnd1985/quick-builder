<script setup lang="ts">
// A rich text field: the editor's TipTap set-up, standalone (015 FR-009, research IT-19). It
// edits and emits the contract's structured document only, so pasted content keeps only what
// rtDoc allows (Principle IX). An editor with no text and no images is no value. It has its own
// undo history, since the item form has no other (035 FR-009, research RT-14).
import { computed, onBeforeUnmount, onMounted, ref, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Editor, JSONContent } from '@tiptap/core'
import { UndoRedo } from '@tiptap/extensions'
import type { SchemaField } from '../../../../api/cms'
import RichTextToolbar, { type RichTextHistory } from '../../../../editor/richtext/RichTextToolbar.vue'
import { createRichTextEditor } from '../../../../editor/richtext/tiptap'
import { fromContractRichText, toContractRichText, type RichTextDoc } from '../../../../editor/richtext/toContractRichText'
import { isEmptyRichText } from '../../../../editor/schema/richTextRules'
import type { Json } from '../../../../editor/schema/subsetValidator'

const props = defineProps<{ field: SchemaField; value: unknown; id: string; describedBy: string; invalid: boolean; siteId: string; label: string }>()
const emit = defineEmits<{ 'update:value': [value: unknown] }>()
const { t } = useI18n()

const host = ref<HTMLElement | null>(null)
const editor = shallowRef<Editor | null>(null)

const isEmpty = (doc: RichTextDoc): boolean => isEmptyRichText(doc as unknown as Json)

// Count transactions, so the undo and redo buttons follow the editor's history.
const changes = ref(0)
const history = computed<RichTextHistory | undefined>(() => {
  void changes.value
  const open = editor.value
  if (!open) return undefined
  return {
    undo: () => open.chain().focus().undo().run(),
    redo: () => open.chain().focus().redo().run(),
    canUndo: open.can().undo(),
    canRedo: open.can().redo(),
  }
})

onMounted(() => {
  if (!host.value) return
  editor.value = createRichTextEditor({
    element: host.value,
    content: props.value ?? null,
    label: props.label,
    siteId: props.siteId,
    imageLabels: {
      missing: t('editor.richText.image.missing'),
      uploading: t('editor.richText.image.uploading'),
      addAlt: t('editor.richText.image.addAlt'),
    },
    extensions: [UndoRedo],
    onChange: (doc) => emit('update:value', isEmpty(doc) ? undefined : doc),
  })
  editor.value.on('transaction', () => {
    changes.value++
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
    // Not an edit of the member's: no undo step.
    open.chain().setMeta('addToHistory', false).setContent(fromContractRichText(next ?? null) as JSONContent, { emitUpdate: false }).run()
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
      :history="history"
    />
    <div
      ref="host"
      class="qb-rich-text item-rich-text__body"
    />
  </div>
</template>
