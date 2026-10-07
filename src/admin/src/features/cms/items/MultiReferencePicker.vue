<script setup lang="ts">
// A multi-reference field: an ordered list of up to 100 distinct items of the target collection
// (015 US4, FR-016). Items are added in the picker, removed, and reordered by dragging or, for
// every drag, by keyboard: the Move up and Move down buttons, or Alt with the up or down arrow
// on a row, as in 014's field list. Each move is announced.
import { computed, inject, nextTick, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { SchemaField } from '../../../api/cms'
import type { ItemRef } from '../../../api/items'
import ReferenceChip from './ReferenceChip.vue'
import ReferencePickerDialog from './ReferencePickerDialog.vue'
import { useItemRefs } from './itemRefs'
import { ITEM_REFS } from './itemRefsKey'
import { MAX_REFERENCES } from './itemValueRules'

const props = defineProps<{
  field: SchemaField
  value: unknown
  id: string
  describedBy: string
  invalid: boolean
  siteId: string
  itemId: string | null
  label: string
}>()
const emit = defineEmits<{ 'update:value': [value: unknown] }>()
const { t } = useI18n()
const refs = inject(ITEM_REFS, () => useItemRefs(() => props.siteId), true)

const open = ref(false)
const announcement = ref('')
const dragging = ref<number | null>(null)
const over = ref<number | null>(null)
const addButton = ref<HTMLButtonElement | null>(null)

const ids = computed<string[]>(() => (Array.isArray(props.value) ? props.value.filter((v): v is string => typeof v === 'string') : []))
const chosen = computed(() => new Set(ids.value.map((id) => id.toLowerCase())))
const full = computed(() => ids.value.length >= MAX_REFERENCES)

watch(ids, (next) => void refs.load(next), { immediate: true })

const nameOf = (id: string): string => {
  const state = refs.stateOf(id)
  return state.kind === 'found' ? (state.item.name ?? state.item.slug) : id
}
const rowButton = (index: number, action: string): string => `${props.id}-${action}-${index}`

function set(next: string[]): void {
  emit('update:value', next.length === 0 ? undefined : next)
}

function add(item: ItemRef): void {
  if (full.value || chosen.value.has(item.id.toLowerCase())) return
  refs.remember(item)
  set([...ids.value, item.id])
  if (ids.value.length + 1 >= MAX_REFERENCES) close()
}

async function remove(index: number): Promise<void> {
  const next = ids.value.filter((_, i) => i !== index)
  set(next)
  await nextTick()
  const focus = next.length === 0 ? addButton.value : document.getElementById(rowButton(Math.min(index, next.length - 1), 'remove'))
  focus?.focus()
}

async function move(from: number, to: number, focusAction = 'row'): Promise<void> {
  if (to < 0 || to >= ids.value.length || from === to) return
  const next = [...ids.value]
  const [moved] = next.splice(from, 1)
  next.splice(to, 0, moved!)
  set(next)
  announcement.value = t('cms.items.inputs.moved', { name: nameOf(moved!), position: to + 1 })
  await nextTick()
  document.getElementById(rowButton(to, focusAction))?.focus()
}

function onRowKeydown(event: KeyboardEvent, index: number): void {
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

function close(): void {
  open.value = false
  addButton.value?.focus()
}
</script>

<template>
  <div
    class="item-input multi-reference"
    role="group"
    :aria-labelledby="`${id}-label`"
    :aria-describedby="describedBy"
  >
    <ol
      v-if="ids.length > 0"
      class="multi-reference__list"
    >
      <li
        v-for="(refId, index) in ids"
        :key="refId"
        draggable="true"
        :class="{ 'is-over': over === index }"
        @dragstart="onDragStart($event, index)"
        @dragover.prevent="over = index"
        @dragleave="over = null"
        @drop.prevent="onDrop(index)"
        @dragend="dragging = null; over = null"
      >
        <span
          :id="rowButton(index, 'row')"
          tabindex="0"
          class="multi-reference__row"
          @keydown="onRowKeydown($event, index)"
        >
          <ReferenceChip
            :id="refId"
            :state="refs.stateOf(refId)"
          />
        </span>
        <button
          :id="rowButton(index, 'up')"
          type="button"
          class="icon-button"
          :disabled="index === 0"
          :aria-label="t('cms.items.inputs.moveUp', { name: nameOf(refId) })"
          @click="move(index, index - 1, 'up')"
        >
          {{ t('cms.items.inputs.upSymbol') }}
        </button>
        <button
          :id="rowButton(index, 'down')"
          type="button"
          class="icon-button"
          :disabled="index === ids.length - 1"
          :aria-label="t('cms.items.inputs.moveDown', { name: nameOf(refId) })"
          @click="move(index, index + 1, 'down')"
        >
          {{ t('cms.items.inputs.downSymbol') }}
        </button>
        <button
          :id="rowButton(index, 'remove')"
          type="button"
          class="link"
          :aria-label="t('cms.items.inputs.remove', { name: nameOf(refId) })"
          @click="remove(index)"
        >
          {{ t('cms.items.form.clear') }}
        </button>
      </li>
    </ol>
    <p class="help">
      {{ t('cms.items.inputs.referenceCount', { count: ids.length, limit: MAX_REFERENCES }) }}
    </p>
    <button
      :id="id"
      ref="addButton"
      type="button"
      aria-haspopup="dialog"
      :disabled="full"
      :aria-invalid="invalid"
      @click="open = true"
    >
      {{ t('cms.items.inputs.pickItems') }}
    </button>
    <p
      v-if="full"
      class="help"
    >
      {{ t('cms.items.inputs.limitReached', { limit: MAX_REFERENCES }) }}
    </p>
    <p
      class="visually-hidden"
      aria-live="polite"
    >
      {{ announcement }}
    </p>

    <ReferencePickerDialog
      v-if="open && field.target"
      :site-id="siteId"
      :collection-id="field.target"
      :title="label"
      :disabled="chosen"
      :self-id="itemId"
      @choose="add"
      @close="close"
    />
  </div>
</template>
