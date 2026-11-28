<script setup lang="ts">
// A layout box: blocks side by side or stacked. A horizontal one stacks at mobile width
// unless told not to, through a container query on the page frame (research ED-09).
//
// A horizontal one with a column pattern other than Equal lays its blocks out on a grid, so
// blocks beyond the pattern start a new row with the same pattern. Its maximum width caps
// the box itself, placed by its alignment (FR-013).
import { computed } from 'vue'
import CanvasChildren from '../../canvas/CanvasChildren.vue'
import type { ContentNode } from '../../schema/schema'
import { useBlock } from '../useBlock'
import { useBorder } from '../useBorder'

const props = defineProps<{ node: ContentNode }>()
const { prop, token } = useBlock(() => props.node)

const { classes: borderClasses, style: borderStyle } = useBorder(() => props.node)

const style = computed(() => ({
  '--qb-container-gap': token('gap', 'space'),
  '--qb-container-pad': token('padding', 'space'),
  '--qb-container-bg': token('background', 'color', 'transparent'),
  ...borderStyle.value,
}))

const grid = computed(() => prop('direction') === 'horizontal' && prop('columns') !== 'equal')
</script>

<template>
  <div
    class="qb-container"
    :class="[
      `qb-container--${prop('direction')}`,
      `qb-align--${prop('align')}`,
      `qb-container--max-${prop('maxWidth')}`,
      {
        'qb-container--stack-on-mobile': prop('stackOnMobile'),
        'qb-container--grid': grid,
        [`qb-columns--${prop('columns')}`]: grid,
      },
      borderClasses,
    ]"
    :style="style"
  >
    <CanvasChildren :node="node" />
  </div>
</template>
