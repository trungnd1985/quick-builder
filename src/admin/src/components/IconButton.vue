<script setup lang="ts">
// A button shown as an icon, whose text is its accessible name and appears as a tooltip on
// hover and on keyboard focus, so it is never only a picture. The icons are drawn here as
// plain strokes, 24 × 24, using the current text colour.

export type IconName = 'folder-plus' | 'pencil' | 'folder-move' | 'trash'

defineProps<{
  icon: IconName
  /** The button's name: read by screen readers and shown as the tooltip. */
  label: string
  danger?: boolean
  disabled?: boolean
}>()

defineEmits<{ click: [event: MouseEvent] }>()

const paths: Record<IconName, string[]> = {
  'folder-plus': [
    'M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z',
    'M12 10v6',
    'M9 13h6',
  ],
  'pencil': [
    'M15 5l4 4',
    'M4 20l1-5L16 4a1.4 1.4 0 0 1 2 0l2 2a1.4 1.4 0 0 1 0 2L9 19z',
  ],
  'folder-move': [
    'M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z',
    'M8 13h7',
    'M12 10l3 3-3 3',
  ],
  'trash': [
    'M4 7h16',
    'M10 4h4',
    'M6 7l1 12a2 2 0 0 0 2 2h6a2 2 0 0 0 2-2l1-12',
    'M10 11v6',
    'M14 11v6',
  ],
}
</script>

<template>
  <button
    type="button"
    class="icon-button"
    :class="{ danger }"
    :aria-label="label"
    :data-tooltip="label"
    :disabled="disabled"
    @click="$emit('click', $event)"
  >
    <svg
      viewBox="0 0 24 24"
      width="20"
      height="20"
      fill="none"
      stroke="currentColor"
      stroke-width="2"
      stroke-linecap="round"
      stroke-linejoin="round"
      aria-hidden="true"
      focusable="false"
    >
      <path
        v-for="d in paths[icon]"
        :key="d"
        :d="d"
      />
    </svg>
  </button>
</template>
