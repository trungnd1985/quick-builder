<script setup lang="ts">
// A Collection list on the canvas (016 research TB-07, TB-14): its card, drawn once as the
// pattern every listed item repeats, and its empty state below it. Both hold blocks like a
// container. Items from the collection fill the cards when the list has a source (US4).
import { computed } from 'vue'
import CanvasChildren from '../../canvas/CanvasChildren.vue'
import type { ContentNode } from '../../schema/schema'
import { useBlock } from '../useBlock'

const props = defineProps<{ node: ContentNode }>()
const { prop, token } = useBlock(() => props.node)

const style = computed(() => ({
  '--qb-cols': String(prop('columns')),
  '--qb-list-gap': token('gap', 'space'),
}))
</script>

<template>
  <div
    class="qb-list"
    :style="style"
  >
    <CanvasChildren :node="node" />
  </div>
</template>
