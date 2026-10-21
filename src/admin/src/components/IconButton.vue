<script setup lang="ts">
// A button shown as an icon, whose text is its accessible name and appears as a tooltip on
// hover and on keyboard focus, so it is never only a picture. The icons are drawn here as
// plain strokes, 24 × 24, using the current text colour.
import { computed } from 'vue'

export type IconName =
  | 'folder-plus' | 'pencil' | 'folder-move' | 'trash'
  // The rich text toolbar (035 research RT-07).
  | 'undo' | 'redo' | 'bold' | 'italic' | 'underline' | 'strike' | 'code'
  | 'bullet-list' | 'numbered-list' | 'quote' | 'link' | 'image' | 'table'
  | 'align-start' | 'align-center' | 'align-end'
  | 'row-above' | 'row-below' | 'column-before' | 'column-after' | 'delete-row' | 'delete-column' | 'header-row' | 'delete-table'
  // 018: moving a form's fields and options.
  | 'arrow-up' | 'arrow-down'

const props = defineProps<{
  icon: IconName
  /** The button's name: read by screen readers and shown as the tooltip. */
  label: string
  /**
   * A keyboard shortcut, written with `Mod` for Ctrl (⌘ on macOS), `Alt` and `Shift`, for
   * example `Mod+Shift+X`. It is shown in the tooltip and announced as `aria-keyshortcuts`.
   */
  shortcut?: string
  danger?: boolean
  disabled?: boolean
}>()

defineEmits<{ click: [event: MouseEvent] }>()

const isMac = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform)

/** The shortcut as people read it on this platform: "Ctrl+Shift+X", or "⌘⇧X" on macOS. */
const shown = computed(() => {
  if (!props.shortcut) return null
  const keys = props.shortcut.split('+')
  if (isMac) return keys.map((k) => ({ Mod: '⌘', Alt: '⌥', Shift: '⇧' })[k] ?? k).join('')
  return keys.map((k) => (k === 'Mod' ? 'Ctrl' : k)).join('+')
})

/** The shortcut in WAI-ARIA's form, for `aria-keyshortcuts`. */
const announced = computed(() =>
  props.shortcut?.split('+').map((k) => ({ Mod: isMac ? 'Meta' : 'Control' })[k] ?? k).join('+'))

const tooltip = computed(() => (shown.value ? `${props.label} (${shown.value})` : props.label))

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
  'undo': ['M9 14L4 9l5-5', 'M4 9h10a6 6 0 0 1 0 12h-3'],
  'redo': ['M15 14l5-5-5-5', 'M20 9H10a6 6 0 0 0 0 12h3'],
  'bold': ['M7 5h6a3.5 3.5 0 0 1 0 7H7z', 'M7 12h7a3.5 3.5 0 0 1 0 7H7z'],
  'italic': ['M11 5h6', 'M7 19h6', 'M14 5l-4 14'],
  'underline': ['M7 4v7a5 5 0 0 0 10 0V4', 'M5 20h14'],
  'strike': ['M4 12h16', 'M16 7a4 3 0 0 0-4-2c-2.2 0-4 1.2-4 3 0 1.4 1 2.3 2.5 3', 'M8 17a4 3 0 0 0 4 2c2.2 0 4-1.2 4-3 0-.8-.3-1.5-.9-2'],
  'code': ['M9 8l-4 4 4 4', 'M15 8l4 4-4 4'],
  'bullet-list': ['M9 6h11', 'M9 12h11', 'M9 18h11', 'M4 6h.01', 'M4 12h.01', 'M4 18h.01'],
  'numbered-list': ['M10 6h10', 'M10 12h10', 'M10 18h10', 'M4 4h1v4', 'M4 8h2', 'M4 14h2l-2 3h2'],
  'quote': ['M6 7h4v4H6z', 'M6 11c0 3 1 5 3 6', 'M14 7h4v4h-4z', 'M14 11c0 3 1 5 3 6'],
  'link': ['M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1', 'M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1'],
  'image': ['M4 5h16v14H4z', 'M8.5 10.5h.01', 'M20 15l-5-5-9 9'],
  'table': ['M4 5h16v14H4z', 'M4 10h16', 'M4 15h16', 'M10 5v14', 'M15 5v14'],
  'align-start': ['M4 6h16', 'M4 10h10', 'M4 14h16', 'M4 18h10'],
  'align-center': ['M4 6h16', 'M7 10h10', 'M4 14h16', 'M7 18h10'],
  'align-end': ['M4 6h16', 'M10 10h10', 'M4 14h16', 'M10 18h10'],
  'row-above': ['M4 12h16v8H4z', 'M12 3v6', 'M9 6h6'],
  'row-below': ['M4 4h16v8H4z', 'M12 15v6', 'M9 18h6'],
  'column-before': ['M12 4h8v16h-8z', 'M3 12h6', 'M6 9v6'],
  'column-after': ['M4 4h8v16H4z', 'M15 12h6', 'M18 9v6'],
  'delete-row': ['M4 8h16v8H4z', 'M9 12h6'],
  'delete-column': ['M8 4h8v16H8z', 'M10 12h4'],
  'header-row': ['M4 5h16v14H4z', 'M4 10h16', 'M7 7.5h10'],
  'delete-table': ['M4 5h16v14H4z', 'M9 9l6 6', 'M15 9l-6 6'],
  'arrow-up': ['M12 19V5', 'M6 11l6-6 6 6'],
  'arrow-down': ['M12 5v14', 'M6 13l6 6 6-6'],
}
</script>

<template>
  <button
    type="button"
    class="icon-button"
    :class="{ danger }"
    :aria-label="label"
    :data-tooltip="tooltip"
    :aria-keyshortcuts="announced"
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
