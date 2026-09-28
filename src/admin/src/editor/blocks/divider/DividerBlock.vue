<script setup lang="ts">
// A horizontal rule with spacing above and below.
import { computed } from 'vue'
import type { ContentNode } from '../../schema/schema'
import { useBlock } from '../useBlock'

const props = defineProps<{ node: ContentNode }>()
const { prop, token } = useBlock(() => props.node)

const style = computed(() => ({
  '--qb-divider-color': token('color', 'color'),
  '--qb-divider-spacing': token('spacing', 'space'),
}))
</script>

<template>
  <hr
    class="qb-divider"
    :class="`qb-divider--${prop('thickness')}`"
    :style="style"
  >
</template>
