<script setup lang="ts">
// A list's empty state on the canvas: what visitors see when nothing is listed (016 TB-07).
import CanvasChildren from '../../canvas/CanvasChildren.vue'
import type { ContentNode } from '../../schema/schema'

defineProps<{ node: ContentNode }>()
</script>

<template>
  <div class="qb-list__empty">
    <CanvasChildren :node="node" />
  </div>
</template>
