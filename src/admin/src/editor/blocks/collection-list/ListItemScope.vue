<script setup lang="ts">
// One card of a Collection list on the canvas, with its item's scope open (016 US4): the card's
// bound blocks show that item's values. Only the first card is the card being edited; the
// others are drawn from it, so selecting and editing always land on the one real card.
import { inject, provide } from 'vue'
import CanvasNode from '../../canvas/CanvasNode.vue'
import { STATIC_CANVAS } from '../../canvas/staticCanvas'
import { CANVAS_SCOPES } from '../../bindings/canvasBindings'
import type { BoundItem } from '../../bindings/resolve'
import type { ContentNode } from '../../schema/schema'

const props = defineProps<{ card: ContentNode; listId: string; item: BoundItem | null; copy: boolean }>()

const outer = inject(CANVAS_SCOPES, () => [])
provide(CANVAS_SCOPES, () => (props.item ? [...outer(), { name: props.listId, item: props.item }] : outer()))
if (props.copy) provide(STATIC_CANVAS, true)
</script>

<template>
  <CanvasNode :node="card" />
</template>
