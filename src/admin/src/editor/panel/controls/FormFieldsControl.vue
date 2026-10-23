<script setup lang="ts">
// A form's fields (018 FR-005 to FR-007, research FS-21): an ordered list to add to, edit,
// reorder and remove from. Reordering uses buttons, and Alt+Up / Alt+Down on a field, rather than
// drag and drop: a field list is short, and the buttons are the keyboard equivalent 011 asks for
// anyway. Every move is announced. A field's id is made once and never changed (FR-006).
import { computed, nextTick, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ControlProps } from './types'
import { isObject, type Json, type JsonObject } from '../../schema/subsetValidator'
import { newFieldId } from '../../forms/fieldIds'
import FormFieldDialog from './FormFieldDialog.vue'
import IconButton from '../../../components/IconButton.vue'

const props = defineProps<ControlProps>()
const emit = defineEmits<{ change: [value: Json | undefined] }>()
const { t } = useI18n()

const FIELD_TYPES = ['text', 'textarea', 'email', 'phone', 'number', 'date', 'select', 'radio', 'checkboxes', 'checkbox', 'consent'] as const

const fields = computed<JsonObject[]>(() => (Array.isArray(props.value) ? props.value.filter(isObject) : []))
const newType = ref<string>('text')
const editing = ref<{ index: number | null; field: JsonObject } | null>(null)
const announcement = ref('')
const list = ref<HTMLElement | null>(null)

const nameOf = (field: JsonObject): string =>
  typeof field.label === 'string' && field.label.trim() ? field.label : t('editor.form.fields.newLabel')

const isRequired = (field: JsonObject): boolean => (field.required === undefined ? field.type === 'consent' : field.required === true)

function commit(next: JsonObject[]): void {
  emit('change', next)
}

async function focusRow(index: number, part: string): Promise<void> {
  await nextTick()
  list.value?.querySelector<HTMLElement>(`[data-row="${index}"] [data-part="${part}"]`)?.focus()
}

function move(index: number, by: -1 | 1): void {
  const target = index + by
  if (target < 0 || target >= fields.value.length) return
  const next = fields.value.slice()
  const [moved] = next.splice(index, 1)
  next.splice(target, 0, moved!)
  commit(next)
  announcement.value = t('editor.form.fields.moved', { label: nameOf(moved!), position: target + 1, count: next.length })
  void focusRow(target, by < 0 ? 'up' : 'down')
}

function remove(index: number): void {
  const field = fields.value[index]!
  if (!window.confirm(t('editor.form.fields.confirmRemove', { label: nameOf(field) }))) return
  commit(fields.value.filter((_, i) => i !== index))
  announcement.value = t('editor.form.fields.removed', { label: nameOf(field) })
}

function add(): void {
  editing.value = {
    index: null,
    field: {
      id: newFieldId(fields.value.map((f) => String(f.id))),
      type: newType.value,
      label: '',
      ...(newType.value === 'select' || newType.value === 'radio' || newType.value === 'checkboxes' ? { options: [] } : {}),
      ...(newType.value === 'consent' ? { statement: '' } : {}),
    },
  }
}

function save(field: JsonObject): void {
  const target = editing.value
  if (!target) return
  const next = fields.value.slice()
  if (target.index === null) next.push(field)
  else next[target.index] = field
  commit(next)
  announcement.value = target.index === null ? t('editor.form.fields.added', { label: nameOf(field) }) : ''
  const index = target.index ?? next.length - 1
  editing.value = null
  void focusRow(index, 'edit')
}

function onKeydown(event: KeyboardEvent, index: number): void {
  if (!event.altKey || (event.key !== 'ArrowUp' && event.key !== 'ArrowDown')) return
  event.preventDefault()
  move(index, event.key === 'ArrowUp' ? -1 : 1)
}
</script>

<template>
  <div
    class="editor-field form-fields"
    :data-prop="prop"
  >
    <span
      :id="`${id}-label`"
      class="editor-field__label"
    >{{ label }}</span>
    <p
      v-if="fields.length === 0"
      class="help"
    >
      {{ t('editor.form.fields.none') }}
    </p>
    <ol
      v-else
      ref="list"
      class="form-fields__list"
      :aria-labelledby="`${id}-label`"
      :aria-describedby="`${id}-keys`"
    >
      <li
        v-for="(field, index) in fields"
        :key="String(field.id)"
        class="form-fields__row"
        :data-row="index"
        :data-field-id="String(field.id)"
        @keydown="onKeydown($event, index)"
      >
        <span class="form-fields__name">
          {{ nameOf(field) }}
          <span class="help">{{ t(`editor.form.types.${String(field.type)}`) }}<template v-if="isRequired(field)">, {{ t('editor.form.fields.required') }}</template></span>
        </span>
        <IconButton
          icon="pencil"
          data-part="edit"
          :label="t('editor.form.fields.edit', { label: nameOf(field) })"
          @click="editing = { index, field }"
        />
        <IconButton
          icon="arrow-up"
          data-part="up"
          :disabled="index === 0"
          :label="t('editor.form.fields.moveUp', { label: nameOf(field) })"
          @click="move(index, -1)"
        />
        <IconButton
          icon="arrow-down"
          data-part="down"
          :disabled="index === fields.length - 1"
          :label="t('editor.form.fields.moveDown', { label: nameOf(field) })"
          @click="move(index, 1)"
        />
        <IconButton
          icon="trash"
          data-part="remove"
          danger
          :label="t('editor.form.fields.remove', { label: nameOf(field) })"
          @click="remove(index)"
        />
      </li>
    </ol>
    <p
      :id="`${id}-keys`"
      class="help"
    >
      {{ t('editor.form.fields.keys') }}
    </p>
    <div class="form-fields__add">
      <label
        class="visually-hidden"
        :for="`${id}-type`"
      >{{ t('editor.form.fields.addType') }}</label>
      <select
        :id="`${id}-type`"
        v-model="newType"
      >
        <option
          v-for="type in FIELD_TYPES"
          :key="type"
          :value="type"
        >
          {{ t(`editor.form.types.${type}`) }}
        </option>
      </select>
      <button
        type="button"
        :disabled="fields.length >= 50"
        @click="add"
      >
        {{ t('editor.form.fields.add') }}
      </button>
    </div>
    <p
      v-if="error"
      :id="`${id}-error`"
      class="field-error"
    >
      {{ error }}
    </p>
    <p
      class="visually-hidden"
      aria-live="polite"
    >
      {{ announcement }}
    </p>
    <FormFieldDialog
      v-if="editing"
      :site-id="siteId"
      :field="editing.field"
      :is-new="editing.index === null"
      :other-labels="fields.filter((_, i) => i !== editing!.index).map((f) => String(f.label ?? ''))"
      @save="save"
      @cancel="editing = null"
    />
  </div>
</template>
