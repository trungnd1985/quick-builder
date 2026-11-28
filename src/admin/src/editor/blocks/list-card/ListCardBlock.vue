<script setup lang="ts">
// A list card on the canvas: the blocks every listed item repeats (016 research TB-07).
import CanvasChildren from '../../canvas/CanvasChildren.vue'
import type { ContentNode } from '../../schema/schema'
import { useBorder } from '../useBorder'

const props = defineProps<{ node: ContentNode }>()
// 066: a bordered card frames every listed item.
const { classes: borderClasses, style: borderStyle } = useBorder(() => props.node)
</script>

<template>
  <div
    class="qb-list__card"
    :class="borderClasses"
    :style="borderStyle"
  >
    <CanvasChildren :node="node" />
  </div>
</template>
