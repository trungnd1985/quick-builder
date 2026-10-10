<script setup lang="ts">
// A list card on the canvas: the blocks every listed item repeats (016 research TB-07).
import CanvasChildren from '../../canvas/CanvasChildren.vue'
import type { ContentNode } from '../../schema/schema'

defineProps<{ node: ContentNode }>()
</script>

<template>
  <div class="qb-list__card">
    <CanvasChildren :node="node" />
  </div>
</template>
