<script setup lang="ts">
// Empty space of a spacing step's height. The hatching is an editing aid only.
import { computed } from 'vue'
import type { ContentNode } from '../../schema/schema'
import { useBlock } from '../useBlock'

const props = defineProps<{ node: ContentNode }>()
const { token } = useBlock(() => props.node)

const style = computed(() => ({ '--qb-spacer-height': token('height', 'space') }))
</script>

<template>
  <div
    class="qb-spacer"
    :style="style"
  />
</template>
