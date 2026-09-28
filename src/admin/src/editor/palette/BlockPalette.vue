<script setup lang="ts">
// The blocks a member can add (FR-004, FR-009). Each can be dragged onto the canvas or the
// layer view, or added with the keyboard: arrows move, Enter adds the block where
// placementFor puts it (research ED-11).
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { definitionOf } from '../blocks/registry'
import { endDrag, startDrag } from '../dnd/useDragDrop'
import { insert } from '../ops'
import { placementFor } from '../ops/placement'
import { blockTypes } from '../schema/schema'
import { useEditorStore } from '../state/editorStore'

const emit = defineEmits<{ inserted: [type: string]; refused: [code: string] }>()

const { t } = useI18n()
const store = useEditorStore()

const items = computed(() =>
  blockTypes
    .map((type) => ({ type, definition: definitionOf(type) }))
    .filter((item): item is { type: string; definition: NonNullable<typeof item.definition> } => item.definition !== undefined),
)
const active = ref(0)
const list = ref<HTMLElement | null>(null)

function add(type: string): void {
  const root = store.root
  if (!root) return
  const at = placementFor(root, store.selection, type, store.kind)
  if (!at) {
    emit('refused', 'needs_section')
    return
  }

  const refused = store.apply(insert(root, at.parentId, at.index, type, store.kind, definitionOf(type)!.initialProps(t)))
  if (refused) emit('refused', refused.code)
  else emit('inserted', type)
}

function focusItem(index: number): void {
  active.value = (index + items.value.length) % items.value.length
  list.value?.querySelectorAll<HTMLElement>('[role="option"]')[active.value]?.focus()
}

function onKeydown(event: KeyboardEvent, index: number): void {
  switch (event.key) {
    case 'ArrowDown':
    case 'ArrowRight':
      event.preventDefault()
      focusItem(index + 1)
      break
    case 'ArrowUp':
    case 'ArrowLeft':
      event.preventDefault()
      focusItem(index - 1)
      break
    case 'Home':
      event.preventDefault()
      focusItem(0)
      break
    case 'End':
      event.preventDefault()
      focusItem(items.value.length - 1)
      break
    case 'Enter':
    case ' ':
      event.preventDefault()
      add(items.value[index].type)
      break
    default:
      return
  }
  event.stopPropagation()
}
</script>

<template>
  <section
    class="editor-palette"
    :aria-label="t('editor.palette.title')"
  >
    <h2 class="editor-panel-title">
      {{ t('editor.palette.title') }}
    </h2>
    <p class="help">
      {{ t('editor.palette.help') }}
    </p>
    <ul
      ref="list"
      role="listbox"
      :aria-label="t('editor.palette.title')"
      class="editor-palette__list"
    >
      <li
        v-for="(item, index) in items"
        :key="item.type"
        role="option"
        :aria-selected="index === active"
        :tabindex="index === active ? 0 : -1"
        :data-block-type="item.type"
        class="editor-palette__item"
        :draggable="!store.readOnly"
        @keydown="onKeydown($event, index)"
        @click="add(item.type)"
        @focus="active = index"
        @dragstart="startDrag($event, { source: 'palette', type: item.type })"
        @dragend="endDrag"
      >
        <span
          class="editor-palette__icon"
          aria-hidden="true"
        >{{ item.definition.icon }}</span>
        {{ t(item.definition.labelKey) }}
      </li>
    </ul>
    <slot />
  </section>
</template>
