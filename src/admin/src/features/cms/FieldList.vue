<script setup lang="ts">
import { nextTick, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { SchemaField } from '../../api/cms'

// 014 US1, US2: the fields of a collection, in order. Selecting one opens its settings.
// Order changes by dragging or, for every drag, by keyboard: Alt with the up or down arrow
// moves the focused field (FR-035, SC-007). The parent announces each move.

const props = defineProps<{
  fields: SchemaField[]
  selectedId: string | null
  titleFieldId: string | undefined
  invalidIndex: number | null
  readOnly: boolean
}>()
const emit = defineEmits<{ select: [id: string]; move: [from: number, to: number] }>()
const { t } = useI18n()

const dragging = ref<number | null>(null)
const over = ref<number | null>(null)

function buttonId(field: SchemaField): string {
  return `field-${field.id.replace('~', 't')}`
}

async function move(from: number, to: number): Promise<void> {
  if (props.readOnly || to < 0 || to >= props.fields.length || from === to) return

  const field = props.fields[from]
  emit('move', from, to)
  await nextTick()
  if (field) document.getElementById(buttonId(field))?.focus()
}

function onKeydown(event: KeyboardEvent, index: number): void {
  if (!event.altKey || (event.key !== 'ArrowUp' && event.key !== 'ArrowDown')) return

  event.preventDefault()
  void move(index, event.key === 'ArrowUp' ? index - 1 : index + 1)
}

function onDragStart(event: DragEvent, index: number): void {
  dragging.value = index
  if (event.dataTransfer) {
    event.dataTransfer.effectAllowed = 'move'
    // Firefox starts a drag only when some data is set.
    event.dataTransfer.setData('text/plain', '')
  }
}

function onDrop(index: number): void {
  if (dragging.value !== null) void move(dragging.value, index)
  dragging.value = null
  over.value = null
}
</script>

<template>
  <div class="field-list">
    <p
      v-if="!readOnly && fields.length > 1"
      id="field-list-help"
      class="help"
    >
      {{ t('cms.fieldList.moveHelp') }}
    </p>
    <ol :aria-label="t('cms.fieldList.label')">
      <li
        v-for="(field, index) in fields"
        :key="field.id"
        :class="{ 'is-selected': field.id === selectedId, 'is-over': over === index, 'is-invalid': invalidIndex === index }"
        :draggable="!readOnly"
        @dragstart="onDragStart($event, index)"
        @dragover.prevent="over = index"
        @dragleave="over = null"
        @drop.prevent="onDrop(index)"
        @dragend="dragging = null; over = null"
      >
        <button
          :id="buttonId(field)"
          type="button"
          :aria-pressed="field.id === selectedId"
          :aria-describedby="!readOnly && fields.length > 1 ? 'field-list-help' : undefined"
          @click="emit('select', field.id)"
          @keydown="onKeydown($event, index)"
        >
          <span class="field-name">{{ t('cms.fieldList.item', { name: field.name, type: t(`cms.types.${field.type}`) }) }}</span>
          <span
            v-if="field.id === titleFieldId"
            class="badge"
          >{{ t('cms.fieldList.namesItems') }}</span>
          <span
            v-else-if="field.required"
            class="badge"
          >{{ t('cms.fieldList.required') }}</span>
        </button>
      </li>
    </ol>
  </div>
</template>

<style scoped>
.field-list ol {
  list-style: none;
  margin: 0;
  padding: 0;
}

.field-list li {
  border: 1px solid transparent;
  border-radius: 6px;
}

.field-list li button {
  display: flex;
  gap: 0.5rem;
  align-items: center;
  justify-content: space-between;
  width: 100%;
  text-align: start;
}

.field-list li.is-selected button {
  font-weight: 600;
}

.field-list li.is-over {
  border-color: currentcolor;
}

.field-list li.is-invalid button {
  outline: 2px solid #b42318;
}
</style>
