<script setup lang="ts">
// The page's canonical address (017 US6, FR-012): itself, another page of the site, or an
// absolute http or https address. Restricted like hiding (SE-03): members who may not change
// the page structure see it read-only, and the server refuses it anyway.
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { pagesApi, type PageNode } from '../../api/pages'
import { isObject, type Json } from '../schema/subsetValidator'

const props = defineProps<{
  id: string
  siteId: string
  value: Json | undefined
  disabled: boolean
  /** The restriction note's id, when the member may not change it. */
  describedBy?: string
  error?: string
}>()
const emit = defineEmits<{ change: [value: Json | undefined] }>()
const { t } = useI18n()

type Kind = 'self' | 'page' | 'url'

const current = computed(() => (isObject(props.value) ? props.value : null))
const kind = ref<Kind>(current.value?.type === 'page' ? 'page' : current.value?.type === 'url' ? 'url' : 'self')
const pageId = ref(typeof current.value?.pageId === 'string' ? current.value.pageId : '')
const href = ref(typeof current.value?.href === 'string' ? current.value.href : '')
const pages = ref<PageNode[]>([])
const urlInvalid = ref(false)

onMounted(async () => {
  try {
    pages.value = ((await pagesApi.tree(props.siteId)).pages ?? []).filter((p) => !p.templateCollectionId)
  } catch {
    pages.value = []
  }
})

const URL_PATTERN = /^https?:\/\/[^\s@/]+(\/\S*)?$/u

function emitCurrent(): void {
  urlInvalid.value = false
  if (kind.value === 'self') {
    emit('change', undefined)
  } else if (kind.value === 'page') {
    if (pageId.value) emit('change', { type: 'page', pageId: pageId.value })
  } else if (href.value.trim() === '') {
    emit('change', undefined)
  } else if (URL_PATTERN.test(href.value.trim())) {
    emit('change', { type: 'url', href: href.value.trim() })
  } else {
    urlInvalid.value = true
  }
}

watch(kind, () => {
  if (kind.value === 'page' && !pageId.value) pageId.value = pages.value[0]?.id ?? ''
  emitCurrent()
})
</script>

<template>
  <fieldset
    class="canonical-control"
    :aria-describedby="describedBy"
  >
    <legend>{{ t('editor.seo.canonical.label') }}</legend>
    <label class="checkbox">
      <input
        v-model="kind"
        type="radio"
        :name="`${id}-kind`"
        value="self"
        :disabled="disabled"
      >
      {{ t('editor.seo.canonical.self') }}
    </label>
    <label class="checkbox">
      <input
        v-model="kind"
        type="radio"
        :name="`${id}-kind`"
        value="page"
        :disabled="disabled"
      >
      {{ t('editor.seo.canonical.page') }}
    </label>
    <div
      v-if="kind === 'page'"
      class="field"
    >
      <label :for="`${id}-page`">{{ t('editor.seo.canonical.pageLabel') }}</label>
      <select
        :id="`${id}-page`"
        v-model="pageId"
        :disabled="disabled"
        @change="emitCurrent"
      >
        <option
          v-for="page in pages"
          :key="page.id"
          :value="page.id"
        >
          {{ page.title }} ({{ page.path }})
        </option>
      </select>
    </div>
    <label class="checkbox">
      <input
        v-model="kind"
        type="radio"
        :name="`${id}-kind`"
        value="url"
        :disabled="disabled"
      >
      {{ t('editor.seo.canonical.url') }}
    </label>
    <div
      v-if="kind === 'url'"
      class="field"
    >
      <label :for="`${id}-url`">{{ t('editor.seo.canonical.urlLabel') }}</label>
      <input
        :id="`${id}-url`"
        v-model="href"
        type="url"
        :disabled="disabled"
        :aria-invalid="urlInvalid || !!error ? 'true' : undefined"
        :aria-describedby="urlInvalid || error ? `${id}-error` : undefined"
        @change="emitCurrent"
      >
    </div>
    <p
      v-if="urlInvalid || error"
      :id="`${id}-error`"
      class="error"
      role="alert"
    >
      {{ urlInvalid ? t('editor.seo.canonical.invalid') : error }}
    </p>
  </fieldset>
</template>
