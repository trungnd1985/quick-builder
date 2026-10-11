<script setup lang="ts">
// Whether a Collection list splits its items into pages (016 US5, binding-expression.md §5): off,
// the list shows up to its limit; on, it shows a page size at a time, with Previous and Next
// links that keep the page in the address under a parameter of its own.
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { isObject, type Json } from '../../schema/subsetValidator'
import type { ControlProps } from './types'

const props = defineProps<ControlProps>()
const emit = defineEmits<{ change: [value: Json | undefined] }>()
const { t } = useI18n()

const DEFAULT_PAGE_SIZE = 12
const PARAM = /^[a-z0-9]+(?:-[a-z0-9]+)*$/

const paging = computed(() => (isObject(props.value) ? props.value : null))
const pageSize = computed(() => (typeof paging.value?.pageSize === 'number' ? paging.value.pageSize : DEFAULT_PAGE_SIZE))
const param = computed(() => (typeof paging.value?.param === 'string' ? paging.value.param : ''))

function onToggle(event: Event): void {
  emit('change', (event.target as HTMLInputElement).checked ? { pageSize: DEFAULT_PAGE_SIZE } : undefined)
}

function onPageSize(event: Event): void {
  const size = Number((event.target as HTMLInputElement).value)
  if (Number.isInteger(size) && size >= 1 && size <= 100) emit('change', { ...paging.value, pageSize: size })
}

function onParam(event: Event): void {
  const value = (event.target as HTMLInputElement).value.trim()
  const rest = { ...(paging.value ?? {}) }
  delete rest.param
  if (value === '') emit('change', rest)
  else if (PARAM.test(value) && value.length <= 32) emit('change', { ...rest, param: value })
}
</script>

<template>
  <fieldset
    class="editor-field"
    :data-prop="prop"
  >
    <legend class="editor-field__label">
      {{ label }}
    </legend>
    <label>
      <input
        :id="id"
        type="checkbox"
        :checked="paging !== null"
        @change="onToggle"
      >
      {{ t('editor.list.paging.on') }}
    </label>
    <template v-if="paging">
      <label :for="`${id}-size`">{{ t('editor.list.paging.pageSize') }}</label>
      <input
        :id="`${id}-size`"
        type="number"
        min="1"
        max="100"
        :value="pageSize"
        @change="onPageSize"
      >
      <label :for="`${id}-param`">{{ t('editor.list.paging.param') }}</label>
      <input
        :id="`${id}-param`"
        type="text"
        maxlength="32"
        placeholder="page"
        :value="param"
        :aria-describedby="`${id}-param-help`"
        @change="onParam"
      >
      <p
        :id="`${id}-param-help`"
        class="help"
      >
        {{ t('editor.list.paging.paramHelp') }}
      </p>
    </template>
    <p
      v-if="error"
      class="field-error"
      role="alert"
    >
      {{ error }}
    </p>
  </fieldset>
</template>
