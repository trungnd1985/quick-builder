<script setup lang="ts">
// Empty space of a spacing step's height. The hatching is an editing aid only.
import { computed } from 'vue'
import type { ContentNode } from '../../schema/schema'
import { useBlock } from '../useBlock'
import { useBorder } from '../useBorder'

const props = defineProps<{ node: ContentNode }>()
const { token } = useBlock(() => props.node)

const { classes: borderClasses, style: borderStyle } = useBorder(() => props.node)
const style = computed(() => ({ '--qb-spacer-height': token('height', 'space'), ...borderStyle.value }))
</script>

<template>
  <div
    class="qb-spacer"
    :class="borderClasses"
    :style="style"
  />
</template>
