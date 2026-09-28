<script setup lang="ts">
// A full-width band of the page. Every piece of content sits in one (FR-012). Its content is
// capped at one of the site's content widths and centred, or spans the page (FR-013).
import { computed } from 'vue'
import CanvasChildren from '../../canvas/CanvasChildren.vue'
import type { ContentNode } from '../../schema/schema'
import { useBlock } from '../useBlock'

const props = defineProps<{ node: ContentNode }>()
const { prop, token } = useBlock(() => props.node)

const style = computed(() => ({
  '--qb-section-bg': token('background', 'color', 'transparent'),
  '--qb-section-pad': token('paddingY', 'space'),
}))
</script>

<template>
  <section
    class="qb-section"
    :class="`qb-section--${prop('width')}`"
    :style="style"
  >
    <div class="qb-section__inner">
      <CanvasChildren :node="node" />
    </div>
  </section>
</template>
