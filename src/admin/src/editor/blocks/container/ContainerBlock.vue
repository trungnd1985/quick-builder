<script setup lang="ts">
// A layout box: blocks side by side or stacked. A horizontal one stacks at mobile width
// unless told not to, through a container query on the page frame (research ED-09).
import { computed } from 'vue'
import CanvasChildren from '../../canvas/CanvasChildren.vue'
import type { ContentNode } from '../../schema/schema'
import { useBlock } from '../useBlock'

const props = defineProps<{ node: ContentNode }>()
const { prop, token } = useBlock(() => props.node)

const style = computed(() => ({
  '--qb-container-gap': token('gap', 'space'),
  '--qb-container-pad': token('padding', 'space'),
  '--qb-container-bg': token('background', 'color', 'transparent'),
}))
</script>

<template>
  <div
    class="qb-container"
    :class="[
      `qb-container--${prop('direction')}`,
      `qb-align--${prop('align')}`,
      { 'qb-container--stack-on-mobile': prop('stackOnMobile') },
    ]"
    :style="style"
  >
    <CanvasChildren :node="node" />
  </div>
</template>
