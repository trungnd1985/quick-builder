<script setup lang="ts">
// Shows where a drop will land: a line before or after a block, or an outline around the
// block that will receive it — red with a "not here" label when the block may not go there.
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { dragState } from '../dnd/useDragDrop'

const props = defineProps<{ frame: HTMLElement | null }>()
const { t } = useI18n()

const geometry = computed(() => {
  const target = dragState.target
  if (!target || !props.frame) return null

  // Node ids are [A-Za-z0-9_-] (page-schema.md §1), so they need no escaping in a selector.
  const anchor = props.frame.querySelector<HTMLElement>(`[data-node-id="${target.anchorId}"]`)
  const frameRect = props.frame.getBoundingClientRect()
  const rect = anchor?.getBoundingClientRect() ?? frameRect
  const top = rect.top - frameRect.top
  const left = rect.left - frameRect.left

  if (target.position === 'inside' || !anchor) {
    return { kind: 'box', style: { top: `${top}px`, left: `${left}px`, width: `${rect.width}px`, height: `${rect.height}px` } }
  }
  const y = target.position === 'before' ? top : top + rect.height
  return { kind: 'line', style: { top: `${y - 2}px`, left: `${left}px`, width: `${rect.width}px` } }
})
</script>

<template>
  <div
    v-if="geometry"
    class="qb-drop-indicator"
    :class="[`qb-drop-indicator--${geometry.kind}`, { 'qb-drop-indicator--refused': !dragState.target?.allowed }]"
    :style="geometry.style"
    aria-hidden="true"
  >
    <span
      v-if="!dragState.target?.allowed"
      class="qb-drop-indicator__label"
    >{{ t('editor.canvas.notAllowedHere') }}</span>
  </div>
</template>
