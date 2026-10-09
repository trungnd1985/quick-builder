<script setup lang="ts">
// The commands for the table the cursor is in (035 US5, FR-011): rows and columns added or
// removed, the header row on or off, the table removed. Adding stops at the table limits and
// says so, through the editor's notices (ContractTable).
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Editor } from '@tiptap/core'
import IconButton from '../../components/IconButton.vue'

const props = defineProps<{
  editor: Editor
  /** Bumped by the toolbar on every transaction, so the header row's state follows. */
  tick: number
}>()
const { t } = useI18n()

/** Whether the table's first row is a header row: its first cell is a header cell. */
const headerRow = computed(() => {
  void props.tick
  const { $from } = props.editor.state.selection
  for (let depth = $from.depth; depth > 0; depth--) {
    const node = $from.node(depth)
    if (node.type.name === 'table') return node.firstChild?.firstChild?.type.name === 'tableHeader'
  }
  return false
})

const commands = computed(() => [
  { id: 'rowAbove', icon: 'row-above' as const, run: () => props.editor.chain().focus().addContractRow('before').run() },
  { id: 'rowBelow', icon: 'row-below' as const, run: () => props.editor.chain().focus().addContractRow('after').run() },
  { id: 'columnBefore', icon: 'column-before' as const, run: () => props.editor.chain().focus().addContractColumn('before').run() },
  { id: 'columnAfter', icon: 'column-after' as const, run: () => props.editor.chain().focus().addContractColumn('after').run() },
  { id: 'deleteRow', icon: 'delete-row' as const, run: () => props.editor.chain().focus().deleteRow().run() },
  { id: 'deleteColumn', icon: 'delete-column' as const, run: () => props.editor.chain().focus().deleteColumn().run() },
])
</script>

<template>
  <div
    class="qb-rt-toolbar__table"
    role="group"
    :aria-label="t('editor.richText.table.group')"
  >
    <IconButton
      v-for="command in commands"
      :key="command.id"
      :icon="command.icon"
      :label="t(`editor.richText.table.${command.id}`)"
      @mousedown.prevent
      @click="command.run()"
    />
    <IconButton
      icon="header-row"
      :label="t('editor.richText.table.headerRow')"
      :aria-pressed="headerRow"
      @mousedown.prevent
      @click="editor.chain().focus().toggleHeaderRow().run()"
    />
    <IconButton
      icon="delete-table"
      :label="t('editor.richText.table.deleteTable')"
      danger
      @mousedown.prevent
      @click="editor.chain().focus().deleteTable().run()"
    />
  </div>
</template>
