<script setup lang="ts">
// A menu's entries as an ARIA tree (024 research NV-17), with a roving tabindex: ↑ ↓ move the
// selection, Home and End go to the ends, Enter edits, Delete removes. Alt with the arrows moves
// the selected entry with its children, as 011's layer view moves blocks (ED-11): Alt+↑ and
// Alt+↓ among its siblings, Alt+→ into the previous sibling, Alt+← out after its parent.
// Entries also move by native drag and drop, before, after or inside another (FR-012).
// menuOps decides what is allowed — never past the third level, never inside itself (FR-014) —
// and a refused move changes nothing. Each move is announced (FR-013).
import { computed, nextTick, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { MenuDocument, MenuEntry, MenuTargetStatus } from '../../api/menus'
import { canMove, flatten, indent, locate, move, moveDown, moveUp, outdent, type DropPosition } from './menuOps'

const props = defineProps<{
  document: MenuDocument
  /** The state of each page and item target, by id. */
  states: ReadonlyMap<string, MenuTargetStatus>
  /** Current titles of pages, by id: for entries added since the menu was loaded. */
  titles: ReadonlyMap<string, string>
  /** Each entry's problem, by entry id (FR-011). */
  problems: ReadonlyMap<string, string>
  readOnly: boolean
  label: string
}>()

const selected = defineModel<string | null>('selected', { required: true })
const emit = defineEmits<{
  change: [document: MenuDocument]
  edit: [id: string]
  remove: [id: string]
  announce: [message: string]
}>()
const { t } = useI18n()

const list = ref<HTMLElement | null>(null)
const dragging = ref<string | null>(null)
const dropTarget = ref<{ id: string; position: DropPosition; allowed: boolean } | null>(null)

const rows = computed(() => {
  const flat = flatten(props.document)
  return flat.map((row) => {
    const siblings = flat.filter((r) => r.parentId === row.parentId)
    return { ...row, position: siblings.indexOf(row) + 1, setSize: siblings.length }
  })
})

const focusId = computed(() => (selected.value && rows.value.some((r) => r.entry.id === selected.value) ? selected.value : rows.value[0]?.entry.id ?? null))

function stateOf(entry: MenuEntry): MenuTargetStatus | undefined {
  const target = entry.target
  return target.type === 'page' ? props.states.get(target.pageId) : target.type === 'item' ? props.states.get(target.itemId) : undefined
}

/** What the entry shows: its own label, else its page's title or item's name. */
function labelOf(entry: MenuEntry): string {
  const target = entry.target
  const known = target.type === 'page' ? props.titles.get(target.pageId) : undefined
  return entry.label?.trim() || stateOf(entry)?.title || known || t('menus.tree.untitled')
}

function select(id: string): void {
  selected.value = id
}

watch(selected, async (id) => {
  if (!id) return
  await nextTick()
  const row = list.value?.querySelector<HTMLElement>(`[data-entry-id="${id}"]`)
  row?.scrollIntoView?.({ block: 'nearest' })
  if (list.value?.contains(document.activeElement)) row?.focus()
})

/** Applies a move; announces it, or that it was not allowed. */
function apply(next: MenuDocument, id: string): void {
  if (next === props.document) {
    emit('announce', t('menus.tree.moveRefused'))
    return
  }
  emit('change', next)
  const where = locate(next, id)
  const entry = where?.entry
  if (where && entry) {
    emit('announce', t('menus.tree.moved', {
      label: labelOf(entry),
      position: where.index + 1,
      count: where.siblings.length,
      level: where.level,
    }))
  }
}

function onKeydown(event: KeyboardEvent, id: string): void {
  select(id)
  const index = rows.value.findIndex((r) => r.entry.id === id)

  if (event.altKey && !props.readOnly) {
    const ops: Record<string, (d: MenuDocument, i: string) => MenuDocument> = {
      ArrowUp: moveUp,
      ArrowDown: moveDown,
      ArrowRight: indent,
      ArrowLeft: outdent,
    }
    const op = ops[event.key]
    if (!op) return
    event.preventDefault()
    apply(op(props.document, id), id)
    return
  }

  if (event.ctrlKey || event.metaKey) return

  switch (event.key) {
    case 'ArrowDown':
      if (rows.value[index + 1]) select(rows.value[index + 1].entry.id)
      break
    case 'ArrowUp':
      if (rows.value[index - 1]) select(rows.value[index - 1].entry.id)
      break
    case 'Home':
      if (rows.value[0]) select(rows.value[0].entry.id)
      break
    case 'End':
      if (rows.value.length > 0) select(rows.value[rows.value.length - 1].entry.id)
      break
    case 'Enter':
      if (props.readOnly) return
      emit('edit', id)
      break
    case 'Delete':
      if (props.readOnly) return
      emit('remove', id)
      break
    default:
      return
  }
  event.preventDefault()
}

// --- Drag and drop (native, as 011 ED-02) --------------------------------------------------

const MIME = 'application/x-quickbuilder-menu-entry'

function onDragStart(event: DragEvent, id: string): void {
  dragging.value = id
  dropTarget.value = null
  if (event.dataTransfer) {
    event.dataTransfer.effectAllowed = 'move'
    // Firefox starts a drag only when some data is set.
    event.dataTransfer.setData(MIME, id)
  }
}

/** Where over a row a drop lands: its middle is inside it, its edges before or after. */
function positionOver(event: DragEvent): DropPosition {
  const rect = (event.currentTarget as HTMLElement).getBoundingClientRect()
  if (rect.height === 0) return 'after'
  const offset = (event.clientY - rect.top) / rect.height
  if (offset > 0.25 && offset < 0.75) return 'inside'
  return offset < 0.5 ? 'before' : 'after'
}

function onDragOver(event: DragEvent, id: string): void {
  const moving = dragging.value
  if (!moving || props.readOnly) return
  const position = positionOver(event)
  const allowed = moving !== id && canMove(props.document, moving, id, position)
  dropTarget.value = { id, position, allowed }
  if (allowed) event.preventDefault()
}

function onDrop(event: DragEvent): void {
  event.preventDefault()
  const moving = dragging.value
  const target = dropTarget.value
  endDrag()
  if (!moving || !target) return
  if (!target.allowed) {
    emit('announce', t('menus.tree.moveRefused'))
    return
  }
  apply(move(props.document, moving, target.id, target.position), moving)
}

function endDrag(): void {
  dragging.value = null
  dropTarget.value = null
}

const dropClass = (id: string) => {
  const target = dropTarget.value
  if (!target || target.id !== id) return undefined
  return [`menu-entry--drop-${target.position}`, { 'menu-entry--drop-refused': !target.allowed }]
}
</script>

<template>
  <ul
    ref="list"
    role="tree"
    class="menu-tree"
    :aria-label="label"
  >
    <li
      v-for="row in rows"
      :key="row.entry.id"
      role="treeitem"
      class="menu-entry"
      :class="[dropClass(row.entry.id), { 'menu-entry--hidden': row.entry.hidden }]"
      :data-entry-id="row.entry.id"
      :aria-level="row.level"
      :aria-setsize="row.setSize"
      :aria-posinset="row.position"
      :aria-selected="selected === row.entry.id"
      :aria-describedby="problems.has(row.entry.id) ? `menu-entry-problem-${row.entry.id}` : undefined"
      :tabindex="focusId === row.entry.id ? 0 : -1"
      :draggable="!readOnly"
      :style="{ paddingInlineStart: `${(row.level - 1) * 1.5}rem` }"
      @click="select(row.entry.id)"
      @dblclick="!readOnly && emit('edit', row.entry.id)"
      @keydown="onKeydown($event, row.entry.id)"
      @dragstart="onDragStart($event, row.entry.id)"
      @dragend="endDrag"
      @dragover="onDragOver($event, row.entry.id)"
      @drop="onDrop"
    >
      <span class="menu-entry__label">{{ labelOf(row.entry) }}</span>
      <span class="menu-entry__kind">{{ t(`menus.tree.kinds.${row.entry.target.type}`) }}</span>
      <span
        v-if="stateOf(row.entry)"
        class="badge"
        :class="`menu-entry__state--${stateOf(row.entry)!.state}`"
      >{{ t(`menus.tree.states.${stateOf(row.entry)!.state}`) }}</span>
      <span
        v-if="row.entry.newTab"
        class="badge"
      >{{ t('menus.tree.newTab') }}</span>
      <span
        v-if="row.entry.hidden"
        class="badge"
      >{{ t('menus.tree.hidden') }}</span>
      <span
        v-if="problems.has(row.entry.id)"
        :id="`menu-entry-problem-${row.entry.id}`"
        class="field-error"
      >{{ t('menus.tree.problem', { problem: t(`menus.entry.problems.${problems.get(row.entry.id)}`) }) }}</span>
    </li>
  </ul>
</template>
