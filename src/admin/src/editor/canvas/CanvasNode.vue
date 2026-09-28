<script setup lang="ts">
// One node on the canvas: its block component inside a wrapper that carries the editing aids
// — selection outline, type label and drag handle. The wrapper is an editing aid; the block
// inside is what the page shows (FR-002).
//
// The node arrives as a prop and nothing else: when an edit elsewhere keeps this node's
// object, Vue skips it entirely (research ED-10).
import { computed, onBeforeUpdate } from 'vue'
import { useI18n } from 'vue-i18n'
import { definitionOf } from '../blocks/registry'
import { endDrag, startDrag } from '../dnd/useDragDrop'
import type { ContentNode } from '../schema/schema'
import { useEditorStore } from '../state/editorStore'
import { renderCount } from './renderProbe'

const props = defineProps<{ node: ContentNode }>()
const { t } = useI18n()
const store = useEditorStore()

const definition = computed(() => definitionOf(props.node.type))
const selected = computed(() => store.selection === props.node.id)
const label = computed(() =>
  typeof props.node.props.layerLabel === 'string' ? props.node.props.layerLabel : definition.value ? t(definition.value.labelKey) : props.node.type,
)

function onClick(event: MouseEvent): void {
  event.stopPropagation()
  store.select(props.node.id)
}

function onDragStart(event: DragEvent): void {
  event.stopPropagation()
  startDrag(event, { source: 'node', id: props.node.id })
}

renderCount(props.node.id)
onBeforeUpdate(() => renderCount(props.node.id))
</script>

<template>
  <div
    class="qb-node"
    :class="{ 'qb-node--selected': selected }"
    :data-node-id="node.id"
    :data-node-type="node.type"
    @click="onClick"
  >
    <div
      v-if="selected"
      class="qb-node__bar"
      aria-hidden="true"
    >
      <span
        v-if="!store.readOnly"
        class="qb-node__handle"
        draggable="true"
        :title="t('editor.canvas.dragHandle')"
        @dragstart="onDragStart"
        @dragend="endDrag"
      />
      <span class="qb-node__label">{{ label }}</span>
    </div>
    <component
      :is="definition.component"
      v-if="definition"
      :node="node"
    />
    <div
      v-else
      class="qb-editor-placeholder"
    >
      {{ t('editor.canvas.unknownBlock', { type: node.type }) }}
    </div>
  </div>
</template>
