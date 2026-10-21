<script setup lang="ts">
// Where a visitor goes after a successful submission (018 FR-012, FR-039): nowhere (the success
// message), a page of this site — kept by id, so it follows the page when it moves — or an
// address beginning with https:// or http://. A page that is not published is marked, because
// publishing waits until it is; a page that no longer exists is marked as broken.
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ControlProps } from './types'
import PagePicker from '../../../components/PagePicker.vue'
import { isObject, type Json } from '../../schema/subsetValidator'
import { useSitePages } from '../../state/useSitePages'

const props = defineProps<ControlProps>()
const emit = defineEmits<{ change: [value: Json | undefined] }>()
const { t } = useI18n()

type Kind = 'none' | 'page' | 'url'
const kindOf = (value: Json | undefined): Kind => (isObject(value) && (value.type === 'page' || value.type === 'url') ? value.type : 'none')

const pages = useSitePages(props.siteId)
const pickable = computed(() => pages.value.map((p) => ({ id: p.id, title: p.title, path: p.path, templateCollectionId: p.templateCollectionId })))

const kind = ref<Kind>(kindOf(props.value))
const pageId = ref(isObject(props.value) && typeof props.value.pageId === 'string' ? props.value.pageId : '')
const href = ref(isObject(props.value) && typeof props.value.href === 'string' ? props.value.href : '')
const addressProblem = ref(false)

watch(() => props.value, (value) => {
  if (props.error && kind.value === 'url') return
  kind.value = kindOf(value)
  pageId.value = isObject(value) && typeof value.pageId === 'string' ? value.pageId : ''
  href.value = isObject(value) && typeof value.href === 'string' ? value.href : ''
})

const chosenPage = computed(() => pages.value.find((p) => p.id === pageId.value) ?? null)
const gone = computed(() => kind.value === 'page' && pageId.value !== '' && pages.value.length > 0 && !chosenPage.value)
const notLive = computed(() => kind.value === 'page' && chosenPage.value !== null && chosenPage.value.publication.state === 'draft')

const ADDRESS = /^https?:\/\/[^\s]+$/

function setKind(next: Kind): void {
  kind.value = next
  addressProblem.value = false
  if (next === 'none') emit('change', undefined)
  else if (next === 'page' && pageId.value) emit('change', { type: 'page', pageId: pageId.value })
  else if (next === 'url' && ADDRESS.test(href.value.trim())) emit('change', { type: 'url', href: href.value.trim() })
}

function choosePage(id: string): void {
  pageId.value = id
  emit('change', id ? { type: 'page', pageId: id } : undefined)
}

function commitAddress(): void {
  const address = href.value.trim()
  addressProblem.value = address !== '' && !ADDRESS.test(address)
  if (!addressProblem.value) emit('change', address ? { type: 'url', href: address } : undefined)
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
    <label
      v-for="option in (['none', 'page', 'url'] as const)"
      :key="option"
      class="checkbox"
    >
      <input
        type="radio"
        :name="`${id}-kind`"
        :value="option"
        :checked="kind === option"
        @change="setKind(option)"
      >
      {{ t(`editor.form.redirect.${option}`) }}
    </label>
    <template v-if="kind === 'page'">
      <PagePicker
        :id="`${id}-page`"
        :model-value="pageId"
        :label="t('editor.form.redirect.choosePage')"
        :pages="pickable"
        placeholder=""
        :described-by="gone || notLive ? `${id}-page-state` : undefined"
        @update:model-value="choosePage"
      />
      <p
        v-if="gone || notLive"
        :id="`${id}-page-state`"
        class="help warning"
      >
        {{ gone ? t('editor.form.redirect.gone') : t('editor.form.redirect.notLive') }}
      </p>
    </template>
    <div
      v-else-if="kind === 'url'"
      class="field"
    >
      <label :for="`${id}-href`">{{ t('editor.form.redirect.address') }}</label>
      <input
        :id="`${id}-href`"
        v-model="href"
        type="text"
        inputmode="url"
        maxlength="2048"
        :aria-invalid="addressProblem ? 'true' : undefined"
        :aria-describedby="`${id}-href-help`"
        @change="commitAddress"
      >
      <p
        :id="`${id}-href-help`"
        :class="addressProblem ? 'field-error' : 'help'"
      >
        {{ addressProblem ? t('editor.form.redirect.addressInvalid') : t('editor.form.redirect.addressHelp') }}
      </p>
    </div>
    <p
      v-if="error"
      :id="`${id}-error`"
      class="field-error"
    >
      {{ error }}
    </p>
  </fieldset>
</template>
