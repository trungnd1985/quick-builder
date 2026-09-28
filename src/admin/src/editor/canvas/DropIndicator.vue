<script setup lang="ts">
// Shows where a drop will land: a line before or after a block, or an outline around the
// block that will receive it — red with a "not here" label when the block may not go there.
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { dragState } from '../dnd/useDragDrop'

const props = withDefaults(defineProps<{ frame: HTMLElement | null; scale?: number }>(), { scale: 1 })
const { t } = useI18n()

const geometry = computed(() => {
  const target = dragState.target
  if (!target || !props.frame) return null

  // Node ids are [A-Za-z0-9_-] (page-schema.md §1), so they need no escaping in a selector.
  const anchor = props.frame.querySelector<HTMLElement>(`[data-node-id="${target.anchorId}"]`)
  // Screen rectangles are scaled with the frame; the indicator is placed inside the frame,
  // in its own unscaled pixels.
  const s = props.scale || 1
  const frameRect = props.frame.getBoundingClientRect()
  const box = anchor?.getBoundingClientRect() ?? frameRect
  const rect = { width: box.width / s, height: box.height / s }
  const top = (box.top - frameRect.top) / s
  const left = (box.left - frameRect.left) / s

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
