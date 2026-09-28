<script setup lang="ts">
// The blocks a member can add (FR-004, FR-009). Each can be dragged onto the canvas or the
// layer view, or added with the keyboard: arrows move, Enter adds the block where
// placementFor puts it (research ED-11).
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { definitionOf } from '../blocks/registry'
import { endDrag, startDrag } from '../dnd/useDragDrop'
import { insert } from '../ops'
import { placementFor } from '../ops/placement'
import { blockTypes } from '../schema/schema'
import { useEditorStore } from '../state/editorStore'
import { siteComponents } from '../state/componentCache'
import type { Component } from '../../api/pages'

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

// Shared components, for page documents only: a component never holds another (FR-027).
const components = ref<Component[]>([])
onMounted(async () => {
  if (store.kind === 'page') components.value = await siteComponents(store.siteId)
})

function add(type: string, initialProps?: Record<string, string>): void {
  const root = store.root
  if (!root) return
  const at = placementFor(root, store.selection, type, store.kind)
  if (!at) {
    emit('refused', type === 'component-ref' ? 'nesting_not_allowed' : 'needs_section')
    return
  }

  const refused = store.apply(insert(root, at.parentId, at.index, type, store.kind, initialProps ?? definitionOf(type)!.initialProps(t)))
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
    <template v-if="store.kind === 'page' && components.length > 0">
      <h2 class="editor-panel-title editor-palette__components-title">
        {{ t('editor.palette.components') }}
      </h2>
      <ul
        role="listbox"
        :aria-label="t('editor.palette.components')"
        class="editor-palette__list"
      >
        <li
          v-for="component in components"
          :key="component.id"
          role="option"
          aria-selected="false"
          tabindex="0"
          class="editor-palette__item"
          :data-component-id="component.id"
          :draggable="!store.readOnly"
          @keydown.enter.prevent.stop="add('component-ref', { componentId: component.id })"
          @click="add('component-ref', { componentId: component.id })"
          @dragstart="startDrag($event, { source: 'palette', type: 'component-ref', initialProps: { componentId: component.id } })"
          @dragend="endDrag"
        >
          <span
            class="editor-palette__icon"
            aria-hidden="true"
          >{{ definitionOf('component-ref')!.icon }}</span>
          {{ component.name }}
        </li>
      </ul>
    </template>
    <slot />
  </section>
</template>
