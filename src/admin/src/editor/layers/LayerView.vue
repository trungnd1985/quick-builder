<script setup lang="ts">
// An outline of every block of the page (011 US4, FR-022, FR-023), in step with the canvas:
// what is selected in one is selected in the other, and scrolled into view. It is the most
// direct way to reach small or deeply nested blocks, and the keyboard's way to arrange a page.
//
// An ARIA tree (WAI-ARIA tree pattern) with a roving tabindex: ↑ ↓ move, → opens or goes to
// the first child, ← closes or goes to the parent, Home and End go to the ends, F2 renames.
// Alt with the arrows moves the block — those keys bubble to the editor's own commands, so
// they work the same here as on the canvas.
import { computed, nextTick, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { definitionOf } from '../blocks/registry'
import { dragState, dropResult, endDrag, resolveTarget, setTarget, sourceOf, startDrag, type DropPosition } from '../dnd/useDragDrop'
import { setLabel } from '../ops'
import { takesChildren, type ContentNode } from '../schema/schema'
import { useEditorStore } from '../state/editorStore'

const emit = defineEmits<{ refused: [code: string] }>()
const { t } = useI18n()
const store = useEditorStore()

const collapsed = ref(new Set<string>())
const renaming = ref<string | null>(null)
const draftName = ref('')
const list = ref<HTMLElement | null>(null)

interface Row {
  node: ContentNode
  depth: number
  position: number
  siblings: number
  expanded: boolean | undefined
}

const rows = computed<Row[]>(() => {
  const out: Row[] = []
  const visit = (nodes: readonly ContentNode[], depth: number) => {
    nodes.forEach((node, index) => {
      const hasChildren = node.children.length > 0
      const expanded = hasChildren ? !collapsed.value.has(node.id) : undefined
      out.push({ node, depth, position: index + 1, siblings: nodes.length, expanded })
      if (expanded) visit(node.children, depth + 1)
    })
  }
  if (store.root) visit(store.root.children, 1)
  return out
})

const focusId = computed(() => (store.selection && rows.value.some((r) => r.node.id === store.selection) ? store.selection : rows.value[0]?.node.id ?? null))

const nameOf = (node: ContentNode): string => {
  if (typeof node.props.layerLabel === 'string') return node.props.layerLabel
  const definition = definitionOf(node.type)
  return definition ? t(definition.labelKey) : node.type
}

const typeOf = (node: ContentNode): string => {
  const definition = definitionOf(node.type)
  return definition ? t(definition.labelKey) : node.type
}

function toggle(id: string, open?: boolean): void {
  const next = new Set(collapsed.value)
  const shouldOpen = open ?? next.has(id)
  if (shouldOpen) next.delete(id)
  else next.add(id)
  collapsed.value = next
}

function select(id: string): void {
  store.select(id)
}

/** Keeps the keyboard focus on the selected row while the tree has it. */
watch(() => store.selection, async (id) => {
  if (!id) return
  // A block chosen on the canvas inside a closed branch opens the branch.
  const ancestors = findAncestors(id)
  if (ancestors.some((a) => collapsed.value.has(a))) {
    const next = new Set(collapsed.value)
    ancestors.forEach((a) => next.delete(a))
    collapsed.value = next
  }
  await nextTick()
  const row = list.value?.querySelector<HTMLElement>(`[data-node-id="${id}"]`)
  row?.scrollIntoView?.({ block: 'nearest' })
  // Follow the selection with the focus — but never out of an open rename field.
  if (renaming.value === null && list.value?.contains(document.activeElement)) row?.focus()
})

function findAncestors(id: string): string[] {
  const path: string[] = []
  const walk = (node: ContentNode): boolean => {
    if (node.id === id) return true
    for (const child of node.children) {
      if (walk(child)) {
        path.push(node.id)
        return true
      }
    }
    return false
  }
  if (store.root) walk(store.root)
  return path
}

function onKeydown(event: KeyboardEvent, row: Row): void {
  if (renaming.value) return
  select(row.node.id)
  // Alt with arrows, and the editing shortcuts, are the editor's (keyboard.ts).
  if (event.altKey || event.ctrlKey || event.metaKey) return

  const index = rows.value.findIndex((r) => r.node.id === row.node.id)
  const go = (target: Row | undefined) => {
    if (target) select(target.node.id)
  }

  switch (event.key) {
    case 'ArrowDown':
      go(rows.value[index + 1])
      break
    case 'ArrowUp':
      go(rows.value[index - 1])
      break
    case 'Home':
      go(rows.value[0])
      break
    case 'End':
      go(rows.value[rows.value.length - 1])
      break
    case 'ArrowRight':
      if (row.expanded === false) toggle(row.node.id, true)
      else if (row.expanded) go(rows.value[index + 1])
      break
    case 'ArrowLeft':
      if (row.expanded) toggle(row.node.id, false)
      else {
        const parentId = findAncestors(row.node.id)[0]
        if (parentId && parentId !== store.root?.id) select(parentId)
      }
      break
    case 'F2':
      startRename(row.node)
      break
    default:
      return
  }
  event.preventDefault()
  event.stopPropagation()
}

async function startRename(node: ContentNode): Promise<void> {
  if (store.readOnly) return
  renaming.value = node.id
  draftName.value = typeof node.props.layerLabel === 'string' ? node.props.layerLabel : ''
  await nextTick()
  list.value?.querySelector<HTMLInputElement>('.editor-layer__rename')?.focus()
}

function finishRename(commit: boolean): void {
  const id = renaming.value
  renaming.value = null
  if (!commit || !id || !store.root) return
  const refused = store.apply(setLabel(store.root, id, draftName.value))
  if (refused) emit('refused', refused.code)
  list.value?.querySelector<HTMLElement>(`[data-node-id="${id}"]`)?.focus()
}

function onRenameKey(event: KeyboardEvent): void {
  event.stopPropagation()
  if (event.key === 'Enter') {
    event.preventDefault()
    finishRename(true)
  } else if (event.key === 'Escape') {
    event.preventDefault()
    finishRename(false)
  }
}

/** Where over a row a drop lands: its middle is into it (if it holds blocks), its edges beside it. */
function positionOver(event: DragEvent, row: Row): DropPosition {
  const rect = (event.currentTarget as HTMLElement).getBoundingClientRect()
  if (rect.height === 0) return takesChildren(row.node.type) ? 'inside' : 'after'
  const offset = (event.clientY - rect.top) / rect.height
  if (takesChildren(row.node.type) && offset > 0.25 && offset < 0.75) return 'inside'
  return offset < 0.5 ? 'before' : 'after'
}

function onDragOver(event: DragEvent, row: Row): void {
  const source = sourceOf(event)
  if (!source || !store.root || store.readOnly) return
  event.stopPropagation()
  const target = setTarget(resolveTarget(store.root, source, row.node.id, positionOver(event, row), store.kind))
  if (target?.allowed) event.preventDefault()
}

function onDrop(event: DragEvent): void {
  event.preventDefault()
  event.stopPropagation()
  const source = sourceOf(event)
  const target = dragState.target
  endDrag()
  if (!source || !target || !store.root) return
  if (!target.allowed) return emit('refused', 'nesting_not_allowed')
  const refused = store.apply(dropResult(store.root, source, target, store.kind, t))
  if (refused) emit('refused', refused.code)
}

const dropClass = (row: Row) => {
  const target = dragState.target
  if (!target || target.anchorId !== row.node.id) return undefined
  return [`editor-layer--drop-${target.position}`, { 'editor-layer--drop-refused': !target.allowed }]
}
</script>

<template>
  <section
    class="editor-layers"
    :aria-labelledby="'editor-layers-title'"
  >
    <h2
      id="editor-layers-title"
      class="editor-panel-title"
    >
      {{ t('editor.layers.title') }}
    </h2>
    <p
      v-if="rows.length === 0"
      class="help"
    >
      {{ t('editor.layers.empty') }}
    </p>
    <ul
      v-else
      ref="list"
      role="tree"
      :aria-label="t('editor.layers.title')"
      class="editor-layers__tree"
    >
      <li
        v-for="row in rows"
        :key="row.node.id"
        role="treeitem"
        class="editor-layer"
        :class="dropClass(row)"
        :data-node-id="row.node.id"
        :aria-level="row.depth"
        :aria-setsize="row.siblings"
        :aria-posinset="row.position"
        :aria-expanded="row.expanded"
        :aria-selected="store.selection === row.node.id"
        :tabindex="focusId === row.node.id ? 0 : -1"
        :draggable="!store.readOnly && renaming !== row.node.id"
        :style="{ paddingInlineStart: `${(row.depth - 1) * 1}rem` }"
        @click.stop="select(row.node.id)"
        @dblclick.stop="startRename(row.node)"
        @keydown="onKeydown($event, row)"
        @dragstart.stop="startDrag($event, { source: 'node', id: row.node.id })"
        @dragend="endDrag"
        @dragover="onDragOver($event, row)"
        @drop="onDrop"
      >
        <span
          class="editor-layer__twisty"
          :class="{ 'editor-layer__twisty--open': row.expanded === true, 'editor-layer__twisty--closed': row.expanded === false }"
          aria-hidden="true"
          @click.stop="row.expanded !== undefined && toggle(row.node.id)"
        />
        <input
          v-if="renaming === row.node.id"
          v-model="draftName"
          class="editor-layer__rename"
          type="text"
          maxlength="60"
          :aria-label="t('editor.layers.rename')"
          :placeholder="typeOf(row.node)"
          @keydown="onRenameKey"
          @blur="finishRename(true)"
          @click.stop
        >
        <span
          v-else
          class="editor-layer__name"
        >{{ nameOf(row.node) }}</span>
        <span
          v-if="typeof row.node.props.layerLabel === 'string' && renaming !== row.node.id"
          class="editor-layer__type"
        >{{ typeOf(row.node) }}</span>
      </li>
    </ul>
  </section>
</template>
