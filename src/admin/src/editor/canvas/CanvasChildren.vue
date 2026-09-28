<script setup lang="ts">
// The children of a Section, a Container or the root, in order. An empty one shows where a
// first block can go (drag or keyboard); the hint is an editing aid, never page content.
import { useI18n } from 'vue-i18n'
import type { ContentNode } from '../schema/schema'
import CanvasNode from './CanvasNode.vue'

defineProps<{ node: ContentNode }>()

const { t } = useI18n()
</script>

<template>
  <CanvasNode
    v-for="child in node.children"
    :key="child.id"
    :node="child"
  />
  <div
    v-if="node.children.length === 0 && node.type !== 'root'"
    class="qb-editor-empty-slot"
    :data-empty-parent="node.id"
    aria-hidden="true"
  >
    {{ t('editor.canvas.emptySlot') }}
  </div>
</template>
