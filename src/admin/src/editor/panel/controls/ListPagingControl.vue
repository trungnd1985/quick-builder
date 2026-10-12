<script setup lang="ts">
// Whether a Collection list splits its items into pages (016 US5, binding-expression.md §5): off,
// the list shows up to its limit; on, it shows a page size at a time, with Previous and Next
// links that keep the page in the address under a parameter of its own.
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ContentNode } from '../../schema/schema'
import { isObject, type Json } from '../../schema/subsetValidator'
import { useEditorStore } from '../../state/editorStore'
import { useSharedComponent } from '../../state/componentCache'
import type { ControlProps } from './types'

const props = defineProps<ControlProps>()
const emit = defineEmits<{ change: [value: Json | undefined] }>()
const { t } = useI18n()

const DEFAULT_PAGE_SIZE = 12
const PARAM = /^[a-z0-9]+(?:-[a-z0-9]+)*$/

const store = useEditorStore()

const paging = computed(() => (isObject(props.value) ? props.value : null))

const paramOf = (value: Json | undefined): string =>
  isObject(value) && typeof value.param === 'string' && value.param !== '' ? value.param : 'page'

/**
 * The parameters the document's other paginated lists read, and on a page those of the shared
 * components it shows. Rules 19 and 21 give each list its own, and a document that broke them
 * could not be saved, so a clashing name is never set. A component cannot see the pages that
 * show it; the server refuses a clash there (rule 21).
 */
const taken = computed(() => {
  const names = new Set<string>()
  const visit = (node: ContentNode, own: boolean): void => {
    if (node.type === 'collection-list' && !(own && node.id === store.selection) && isObject(node.props.paging)) names.add(paramOf(node.props.paging))
    if (own && node.type === 'component-ref' && typeof node.props.componentId === 'string') {
      const shared = useSharedComponent(store.siteId, node.props.componentId)
      if (shared.root) visit(shared.root, false)
    }
    node.children.forEach((child) => visit(child, own))
  }
  if (store.root) visit(store.root, true)
  return names
})

/** The name last refused for clashing, until a name is accepted. */
const refusedName = ref<string | null>(null)
const clash = computed(() => refusedName.value !== null || (!!paging.value && taken.value.has(paramOf(paging.value))))

const pageSize = computed(() => (typeof paging.value?.pageSize === 'number' ? paging.value.pageSize : DEFAULT_PAGE_SIZE))
const param = computed(() => (typeof paging.value?.param === 'string' ? paging.value.param : ''))
const layout = computed(() => (paging.value?.layout === 'steps' ? 'steps' : 'numbers'))

/** FR-027: the pager's look; "numbers" is the default and is not stored. */
function onLayout(value: 'numbers' | 'steps'): void {
  const rest = { ...(paging.value ?? {}) }
  delete rest.layout
  emit('change', value === 'steps' ? { ...rest, layout: value } : rest)
}

function onToggle(event: Event): void {
  if (!(event.target as HTMLInputElement).checked) {
    refusedName.value = null
    emit('change', undefined)
    return
  }

  // The first name no other list reads: page, then page-2, page-3…
  let name = 'page'
  for (let n = 2; taken.value.has(name); n++) name = `page-${n}`
  emit('change', name === 'page' ? { pageSize: DEFAULT_PAGE_SIZE } : { pageSize: DEFAULT_PAGE_SIZE, param: name })
}

function onPageSize(event: Event): void {
  const size = Number((event.target as HTMLInputElement).value)
  if (Number.isInteger(size) && size >= 1 && size <= 100) emit('change', { ...paging.value, pageSize: size })
}

function onParam(event: Event): void {
  const value = (event.target as HTMLInputElement).value.trim()
  const rest = { ...(paging.value ?? {}) }
  delete rest.param
  if (taken.value.has(value === '' ? 'page' : value)) {
    refusedName.value = value === '' ? 'page' : value
    return
  }

  refusedName.value = null
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
      <fieldset>
        <legend>{{ t('editor.list.paging.layout') }}</legend>
        <label
          v-for="option in (['numbers', 'steps'] as const)"
          :key="option"
        >
          <input
            type="radio"
            :name="`${id}-layout`"
            :value="option"
            :checked="layout === option"
            @change="onLayout(option)"
          >
          {{ t(option === 'numbers' ? 'editor.list.paging.layoutNumbers' : 'editor.list.paging.layoutSteps') }}
        </label>
      </fieldset>
    </template>
    <p
      v-if="clash"
      class="field-error"
      role="alert"
    >
      {{ t('editor.list.paging.clash') }}
    </p>
    <p
      v-if="error"
      class="field-error"
      role="alert"
    >
      {{ error }}
    </p>
  </fieldset>
</template>
