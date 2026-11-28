<script setup lang="ts">
// A horizontal rule with spacing above and below.
import { computed } from 'vue'
import type { ContentNode } from '../../schema/schema'
import { useBlock } from '../useBlock'
import { useBorder } from '../useBorder'

const props = defineProps<{ node: ContentNode }>()
const { prop, token } = useBlock(() => props.node)

// 066 BB-05: the line is the hr's own top border, so a border frames a wrapper around it, drawn
// only when the block has one. The wrapper carries every value; the hr inherits them.
const { classes: borderClasses, style: borderStyle, has: bordered } = useBorder(() => props.node)

const style = computed(() => ({
  '--qb-divider-color': token('color', 'color'),
  '--qb-divider-spacing': token('spacing', 'space'),
}))
const frameStyle = computed(() => ({ ...style.value, ...borderStyle.value }))
</script>

<template>
  <div
    v-if="bordered"
    class="qb-divider-frame"
    :class="borderClasses"
    :style="frameStyle"
  >
    <hr
      class="qb-divider"
      :class="`qb-divider--${prop('thickness')}`"
    >
  </div>
  <hr
    v-else
    class="qb-divider"
    :class="`qb-divider--${prop('thickness')}`"
    :style="style"
  >
</template>
