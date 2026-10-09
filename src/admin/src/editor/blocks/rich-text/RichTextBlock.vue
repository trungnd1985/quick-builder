<script setup lang="ts">
// Formatted text: paragraphs, bold, italic, links and lists (FR-013). Drawn from the stored
// structure; double-click it, or press Enter on it, to edit it in place with TipTap (research
// ED-03). Each change is committed as an operation grouped with the typing before it, so undo
// takes back words, not keystrokes; undo from elsewhere flows back into the open editor.
import { computed, inject, nextTick, onBeforeUnmount, ref, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Editor, JSONContent } from '@tiptap/core'
import RichTextView from '../../richtext/RichTextView'
import RichTextToolbar, { type RichTextHistory } from '../../richtext/RichTextToolbar.vue'
import { noticeText } from '../../richtext/notices'
import { createRichTextEditor } from '../../richtext/tiptap'
import { fromContractRichText, toContractRichText } from '../../richtext/toContractRichText'
import { setProp } from '../../ops'
import type { ContentNode } from '../../schema/schema'
import { isObject, type Json } from '../../schema/subsetValidator'
import { pageHistoryKey } from '../../state/history'
import { useBrokenLinks } from '../brokenLinks'
import { useBlock } from '../useBlock'

const props = defineProps<{ node: ContentNode }>()
const { t, te } = useI18n()

// The toolbar's status line: what the last paste or drop left out (035 FR-012).
const notice = ref('')
const { store, prop, token, editable } = useBlock(() => props.node)

const root = ref<HTMLElement | null>(null)
const host = ref<HTMLElement | null>(null)
const editor = shallowRef<Editor | null>(null)
const editing = computed(() => editor.value !== null)
/** True from the moment editing starts, so the editor's host element renders. */
const mounting = ref(false)

// Undo and redo are the page's, so the toolbar's buttons and Ctrl/Cmd+Z take back the same steps
// (035 FR-009, 011 research ED-12).
const pageHistory = inject(pageHistoryKey, null)
const history = computed<RichTextHistory | undefined>(() => {
  const page = pageHistory?.value
  if (!page) return undefined
  return { undo: page.undo, redo: page.redo, canUndo: page.canUndo.value && !store.readOnly, canRedo: page.canRedo.value && !store.readOnly }
})

const content = computed(() => props.node.props.content ?? null)
const empty = computed(() => {
  const doc = content.value
  return !isObject(doc) || !Array.isArray(doc.content) || doc.content.length === 0
})
const style = computed(() => ({ '--qb-text-color': token('color', 'color') }))
const isBroken = useBrokenLinks(() => store.siteId)
const links = computed(() => ({ isBroken: isBroken.value, label: t('editor.canvas.brokenLink') }))

async function startEditing(): Promise<void> {
  if (editing.value || !editable.value) return
  store.select(props.node.id)
  // The host element renders once `mounting` flips the template; wait for it.
  mounting.value = true
  await nextTick()
  if (!host.value) return

  editor.value = createRichTextEditor({
    element: host.value,
    content: content.value,
    label: t('editor.blocks.richText'),
    siteId: store.siteId,
    onNotice: (next) => {
      notice.value = noticeText(t, te, next)
    },
    imageLabels: {
      missing: t('editor.richText.image.missing'),
      uploading: t('editor.richText.image.uploading'),
      addAlt: t('editor.richText.image.addAlt'),
    },
    onChange: (doc) => {
      const refused = store.apply(setProp(store.root!, props.node.id, 'content', doc as unknown as Json), {
        coalesce: `${props.node.id}:content`,
      })
      // Refused (over 20,000 characters, say): put the stored text back (FR-021).
      if (refused) editor.value?.commands.setContent(fromContractRichText(content.value) as JSONContent, { emitUpdate: false })
    },
  })
  editor.value.commands.focus('end')
}

function stopEditing(): void {
  editor.value?.destroy()
  editor.value = null
  mounting.value = false
}

function onFocusOut(event: FocusEvent): void {
  const next = event.relatedTarget as Node | null
  // The toolbar's own dialogs (the media picker, 035) sit outside the block but belong to it.
  if (next instanceof Element && next.closest('[data-qb-dialog]')) return
  if (!root.value?.contains(next)) stopEditing()
}

function onKeydown(event: KeyboardEvent): void {
  if (!editing.value) return
  if (event.key === 'Escape') {
    event.preventDefault()
    stopEditing()
    root.value?.closest<HTMLElement>('.editor-canvas-scroll')?.focus()
  }
  // Typing belongs to the text, apart from the editor's undo, redo and save.
  const mod = event.ctrlKey || event.metaKey
  if (!(mod && ['z', 'y', 's'].includes(event.key.toLowerCase()))) event.stopPropagation()
}

// Undo, redo or the panel changed the stored text: show it in the open editor.
watch(content, (next) => {
  const open = editor.value
  if (!open) return
  if (JSON.stringify(toContractRichText(open.getJSON())) !== JSON.stringify(fromContractRichText(next))) {
    open.commands.setContent(fromContractRichText(next) as JSONContent, { emitUpdate: false })
  }
})

watch(editable, (can) => {
  if (!can) stopEditing()
})

onBeforeUnmount(stopEditing)
</script>

<template>
  <div
    ref="root"
    class="qb-rich-text"
    :class="`qb-align--${prop('align')}`"
    :style="style"
    :data-placeholder="empty && !mounting ? t('editor.blocks.placeholder.richText') : undefined"
    @dblclick="startEditing"
    @qb-edit="startEditing"
    @focusout="onFocusOut"
    @keydown="onKeydown"
  >
    <template v-if="mounting">
      <RichTextToolbar
        v-if="editor"
        :editor="editor"
        :site-id="store.siteId"
        :history="history"
        :notice="notice"
      />
      <div
        ref="host"
        class="qb-rich-text__editor"
      />
    </template>
    <RichTextView
      v-else
      :doc="content"
      :links="links"
      :site-id="store.siteId"
    />
  </div>
</template>
