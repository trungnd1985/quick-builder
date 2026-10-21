<script setup lang="ts">
// One form field (018 FR-005 to FR-007; contracts/form-block.md §2): its label, help and
// placeholder, whether it is required, its own error message, and the rules of its type. Empty
// optional settings are left out of the field rather than stored empty, as the schema wants.
// Problems that only publishing refuses (rule 28) are shown in the panel, not here; only what
// the schema itself refuses keeps the dialog open.
import { computed, nextTick, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import PagePicker from '../../../components/PagePicker.vue'
import IconButton from '../../../components/IconButton.vue'
import { useSitePages } from '../../state/useSitePages'
import type { JsonObject } from '../../schema/subsetValidator'

const props = defineProps<{
  siteId: string
  field: JsonObject
  isNew: boolean
  /** The labels of the form's other fields, to warn about a repeat. */
  otherLabels: readonly string[]
}>()
const emit = defineEmits<{ save: [field: JsonObject]; cancel: [] }>()
const { t } = useI18n()

const dialogId = 'form-field-dialog'
const type = String(props.field.type)
const str = (name: string) => (typeof props.field[name] === 'string' ? (props.field[name] as string) : '')
const num = (name: string) => (typeof props.field[name] === 'number' ? String(props.field[name]) : '')

const label = ref(str('label'))
const help = ref(str('help'))
const placeholder = ref(str('placeholder'))
const required = ref(props.field.required === undefined ? type === 'consent' : props.field.required === true)
const message = ref(str('message'))
const minLength = ref(num('minLength'))
const maxLength = ref(num('maxLength'))
const min = ref(num('min'))
const max = ref(num('max'))
const wholeNumber = ref(props.field.wholeNumber === true)
const earliest = ref(str('earliest'))
const latest = ref(str('latest'))
const minSelected = ref(num('minSelected'))
const maxSelected = ref(num('maxSelected'))
const statement = ref(str('statement'))
const linkPageId = ref(str('linkPageId'))
const options = ref<{ value: string; label: string }[]>(
  Array.isArray(props.field.options)
    ? props.field.options.map((o) => (typeof o === 'object' && o !== null && !Array.isArray(o) ? { value: String(o.value ?? ''), label: String(o.label ?? '') } : { value: '', label: '' }))
    : [],
)
const problem = ref<string | null>(null)
const firstField = ref<HTMLInputElement | null>(null)

const pages = useSitePages(props.siteId)
const pickable = computed(() => pages.value.map((p) => ({ id: p.id, title: p.title, path: p.path, templateCollectionId: p.templateCollectionId })))

const TEXT_LIKE = new Set(['text', 'textarea', 'email', 'phone'])
const CHOICE = new Set(['select', 'radio', 'checkboxes'])
const maxTextLength = type === 'textarea' ? 10000 : type === 'email' ? 254 : 500
const takesPlaceholder = TEXT_LIKE.has(type) || type === 'number'
const labelRepeated = computed(() => props.otherLabels.some((l) => l.trim().toLowerCase() === label.value.trim().toLowerCase()))

const integer = (value: string | number): number | undefined => (String(value).trim() === '' ? undefined : Number.parseInt(String(value), 10))
const decimal = (value: string | number): number | undefined => (String(value).trim() === '' || Number.isNaN(Number(value)) ? undefined : Number(value))

function addOption(): void {
  options.value.push({ value: '', label: '' })
}

function removeOption(index: number): void {
  options.value.splice(index, 1)
}

function moveOptionUp(index: number): void {
  if (index === 0) return
  const [moved] = options.value.splice(index, 1)
  options.value.splice(index - 1, 0, moved!)
}

function save(): void {
  if (!label.value.trim()) {
    problem.value = t('editor.form.field.labelRequired')
    return
  }
  if (type === 'consent' && !statement.value.trim()) {
    problem.value = t('editor.form.field.statementRequired')
    return
  }

  const field: JsonObject = { id: props.field.id ?? '', type, label: label.value.trim() }
  const set = (name: string, value: string | number | boolean | undefined) => {
    if (value !== undefined && value !== '') field[name] = value
  }

  set('help', help.value.trim())
  if (takesPlaceholder) set('placeholder', placeholder.value.trim())
  if (required.value !== (type === 'consent')) field.required = required.value
  set('message', message.value.trim())

  if (TEXT_LIKE.has(type)) {
    set('minLength', integer(minLength.value))
    set('maxLength', integer(maxLength.value))
  } else if (type === 'number') {
    set('min', decimal(min.value))
    set('max', decimal(max.value))
    if (wholeNumber.value) field.wholeNumber = true
  } else if (type === 'date') {
    set('earliest', earliest.value)
    set('latest', latest.value)
  } else if (CHOICE.has(type)) {
    const kept = options.value
      .map((o) => ({ label: o.label.trim(), value: (o.value.trim() || o.label.trim()) }))
      .filter((o) => o.label !== '')
    if (kept.length > 0) field.options = kept
    if (type === 'checkboxes') {
      set('minSelected', integer(minSelected.value))
      set('maxSelected', integer(maxSelected.value))
    }
  } else if (type === 'consent') {
    field.statement = statement.value.trim()
    set('linkPageId', linkPageId.value)
  }

  emit('save', field)
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('cancel')
  }
}

onMounted(async () => {
  await nextTick()
  firstField.value?.focus()
})
</script>

<template>
  <div
    class="dialog-backdrop"
    @click.self="emit('cancel')"
  >
    <div
      class="card dialog"
      role="dialog"
      aria-modal="true"
      :aria-labelledby="`${dialogId}-heading`"
      @keydown="onKeydown"
    >
      <form
        novalidate
        @submit.prevent="save"
      >
        <h2 :id="`${dialogId}-heading`">
          {{ isNew ? t('editor.form.field.newTitle') : t('editor.form.field.editTitle') }}
        </h2>
        <p class="help">
          {{ t('editor.form.field.type') }}: {{ t(`editor.form.types.${type}`) }}
        </p>

        <div class="field">
          <label :for="`${dialogId}-label`">{{ t('editor.form.field.label') }}</label>
          <input
            :id="`${dialogId}-label`"
            ref="firstField"
            v-model="label"
            type="text"
            maxlength="200"
            required
            :aria-describedby="type === 'consent' ? `${dialogId}-label-help` : labelRepeated ? `${dialogId}-label-taken` : undefined"
          >
          <p
            v-if="type === 'consent'"
            :id="`${dialogId}-label-help`"
            class="help"
          >
            {{ t('editor.form.field.consentLabelHelp') }}
          </p>
          <p
            v-if="labelRepeated"
            :id="`${dialogId}-label-taken`"
            class="help warning"
          >
            {{ t('editor.form.field.labelTaken') }}
          </p>
        </div>

        <div
          v-if="type === 'consent'"
          class="field"
        >
          <label :for="`${dialogId}-statement`">{{ t('editor.form.field.statement') }}</label>
          <textarea
            :id="`${dialogId}-statement`"
            v-model="statement"
            rows="3"
            maxlength="1000"
            required
          />
          <PagePicker
            :id="`${dialogId}-link`"
            v-model="linkPageId"
            :label="t('editor.form.field.linkPage')"
            :pages="pickable"
            :placeholder="t('editor.form.field.noLinkPage')"
          />
        </div>

        <div class="field">
          <label :for="`${dialogId}-help`">{{ t('editor.form.field.help') }}</label>
          <input
            :id="`${dialogId}-help`"
            v-model="help"
            type="text"
            maxlength="500"
          >
        </div>

        <div
          v-if="takesPlaceholder"
          class="field"
        >
          <label :for="`${dialogId}-placeholder`">{{ t('editor.form.field.placeholder') }}</label>
          <input
            :id="`${dialogId}-placeholder`"
            v-model="placeholder"
            type="text"
            maxlength="200"
          >
        </div>

        <label class="checkbox">
          <input
            v-model="required"
            type="checkbox"
          >
          {{ t('editor.form.field.required') }}
        </label>

        <div
          v-if="TEXT_LIKE.has(type)"
          class="field-row"
        >
          <div class="field">
            <label :for="`${dialogId}-min-length`">{{ t('editor.form.field.minLength') }}</label>
            <input
              :id="`${dialogId}-min-length`"
              v-model="minLength"
              type="number"
              min="0"
              :max="maxTextLength"
              step="1"
            >
          </div>
          <div class="field">
            <label :for="`${dialogId}-max-length`">{{ t('editor.form.field.maxLength') }}</label>
            <input
              :id="`${dialogId}-max-length`"
              v-model="maxLength"
              type="number"
              min="0"
              :max="maxTextLength"
              step="1"
              :placeholder="String(maxTextLength)"
            >
          </div>
        </div>

        <template v-else-if="type === 'number'">
          <div class="field-row">
            <div class="field">
              <label :for="`${dialogId}-min`">{{ t('editor.form.field.min') }}</label>
              <input
                :id="`${dialogId}-min`"
                v-model="min"
                type="number"
                step="any"
              >
            </div>
            <div class="field">
              <label :for="`${dialogId}-max`">{{ t('editor.form.field.max') }}</label>
              <input
                :id="`${dialogId}-max`"
                v-model="max"
                type="number"
                step="any"
              >
            </div>
          </div>
          <label class="checkbox">
            <input
              v-model="wholeNumber"
              type="checkbox"
            >
            {{ t('editor.form.field.wholeNumber') }}
          </label>
        </template>

        <div
          v-else-if="type === 'date'"
          class="field-row"
        >
          <div class="field">
            <label :for="`${dialogId}-earliest`">{{ t('editor.form.field.earliest') }}</label>
            <input
              :id="`${dialogId}-earliest`"
              v-model="earliest"
              type="date"
            >
          </div>
          <div class="field">
            <label :for="`${dialogId}-latest`">{{ t('editor.form.field.latest') }}</label>
            <input
              :id="`${dialogId}-latest`"
              v-model="latest"
              type="date"
            >
          </div>
        </div>

        <fieldset v-else-if="CHOICE.has(type)">
          <legend>{{ t('editor.form.field.options') }}</legend>
          <div
            v-for="(option, index) in options"
            :key="index"
            class="field-row"
          >
            <div class="field">
              <label :for="`${dialogId}-option-${index}`">{{ t('editor.form.field.optionLabel', { number: index + 1 }) }}</label>
              <input
                :id="`${dialogId}-option-${index}`"
                v-model="option.label"
                type="text"
                maxlength="200"
              >
            </div>
            <div class="field">
              <label :for="`${dialogId}-option-${index}-value`">{{ t('editor.form.field.optionValue') }}</label>
              <input
                :id="`${dialogId}-option-${index}-value`"
                v-model="option.value"
                type="text"
                maxlength="200"
                :placeholder="option.label"
              >
            </div>
            <IconButton
              icon="arrow-up"
              :disabled="index === 0"
              :label="t('editor.form.field.moveOptionUp', { number: index + 1 })"
              @click="moveOptionUp(index)"
            />
            <IconButton
              icon="trash"
              danger
              :label="t('editor.form.field.removeOption', { number: index + 1 })"
              @click="removeOption(index)"
            />
          </div>
          <button
            type="button"
            :disabled="options.length >= 100"
            @click="addOption"
          >
            {{ t('editor.form.field.addOption') }}
          </button>
          <div
            v-if="type === 'checkboxes'"
            class="field-row"
          >
            <div class="field">
              <label :for="`${dialogId}-min-selected`">{{ t('editor.form.field.minSelected') }}</label>
              <input
                :id="`${dialogId}-min-selected`"
                v-model="minSelected"
                type="number"
                min="0"
                max="100"
                step="1"
              >
            </div>
            <div class="field">
              <label :for="`${dialogId}-max-selected`">{{ t('editor.form.field.maxSelected') }}</label>
              <input
                :id="`${dialogId}-max-selected`"
                v-model="maxSelected"
                type="number"
                min="0"
                max="100"
                step="1"
              >
            </div>
          </div>
        </fieldset>

        <div class="field">
          <label :for="`${dialogId}-message`">{{ t('editor.form.field.message') }}</label>
          <input
            :id="`${dialogId}-message`"
            v-model="message"
            type="text"
            maxlength="200"
            :aria-describedby="`${dialogId}-message-help`"
          >
          <p
            :id="`${dialogId}-message-help`"
            class="help"
          >
            {{ t('editor.form.field.messageHelp') }}
          </p>
        </div>

        <p
          v-if="problem"
          class="error"
          role="alert"
        >
          {{ problem }}
        </p>

        <div class="actions">
          <button
            type="submit"
            class="primary"
          >
            {{ t('editor.form.field.save') }}
          </button>
          <button
            type="button"
            @click="emit('cancel')"
          >
            {{ t('editor.form.field.cancel') }}
          </button>
        </div>
      </form>
    </div>
  </div>
</template>
