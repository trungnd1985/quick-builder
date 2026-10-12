<script setup lang="ts">
// Where a Button, Image or rich-text link goes (FR-015): nowhere, a page of this site — kept
// by id, so it survives the page being moved or renamed — or an address beginning with
// https://, http://, mailto: or tel:. A page that no longer exists is marked as a broken link.
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { isObject, type Json } from '../../schema/subsetValidator'
import { useSitePages } from '../../state/useSitePages'

const props = defineProps<{ siteId: string; value: Json | undefined; label: string; prop?: string; id?: string; error?: string }>()
const emit = defineEmits<{ change: [value: Json | undefined] }>()
const { t } = useI18n()

const pages = useSitePages(props.siteId)
// 016 TB-02: a template page has no address of its own, so it is not a link target.
const linkable = computed(() => pages.value.filter((p) => !p.templateCollectionId))
const base = computed(() => props.id ?? `link-${Math.random().toString(36).slice(2, 8)}`)

type Kind = 'none' | 'page' | 'url'
const kindOf = (value: Json | undefined): Kind => (isObject(value) && (value.type === 'page' || value.type === 'url') ? value.type : 'none')

const kind = ref<Kind>(kindOf(props.value))
const pageId = ref(isObject(props.value) && typeof props.value.pageId === 'string' ? props.value.pageId : '')
const href = ref(isObject(props.value) && typeof props.value.href === 'string' ? props.value.href : '')

watch(() => props.value, (value) => {
  // A refused address stays as typed; anything else follows the stored value.
  if (props.error && kind.value === 'url') return
  kind.value = kindOf(value)
  pageId.value = isObject(value) && typeof value.pageId === 'string' ? value.pageId : ''
  href.value = isObject(value) && typeof value.href === 'string' ? value.href : ''
})

const broken = computed(() => kind.value === 'page' && pageId.value !== '' && pages.value.length > 0 && !pages.value.some((p) => p.id === pageId.value))

function setKind(next: Kind): void {
  kind.value = next
  if (next === 'none') emit('change', undefined)
  else if (next === 'page' && pageId.value) emit('change', { type: 'page', pageId: pageId.value })
  else if (next === 'url' && href.value) emit('change', { type: 'url', href: href.value })
}

function onPage(event: Event): void {
  pageId.value = (event.target as HTMLSelectElement).value
  emit('change', pageId.value ? { type: 'page', pageId: pageId.value } : undefined)
}

function onHref(event: Event): void {
  href.value = (event.target as HTMLInputElement).value.trim()
  emit('change', href.value ? { type: 'url', href: href.value } : undefined)
}
</script>

<template>
  <fieldset
    class="editor-field editor-link"
    :data-prop="prop"
  >
    <legend class="editor-field__label">
      {{ label }}
    </legend>
    <div class="editor-link__kinds">
      <label class="checkbox">
        <input
          type="radio"
          :name="`${base}-kind`"
          :checked="kind === 'none'"
          @change="setKind('none')"
        >
        {{ t('editor.panel.link.none') }}
      </label>
      <label class="checkbox">
        <input
          type="radio"
          :name="`${base}-kind`"
          :checked="kind === 'page'"
          @change="setKind('page')"
        >
        {{ t('editor.panel.link.page') }}
      </label>
      <label class="checkbox">
        <input
          type="radio"
          :name="`${base}-kind`"
          :checked="kind === 'url'"
          @change="setKind('url')"
        >
        {{ t('editor.panel.link.address') }}
      </label>
    </div>

    <template v-if="kind === 'page'">
      <label :for="`${base}-page`">{{ t('editor.panel.link.pageField') }}</label>
      <select
        :id="`${base}-page`"
        :value="pageId"
        :aria-describedby="broken ? `${base}-broken` : undefined"
        @change="onPage"
      >
        <option value="">
          {{ t('editor.panel.link.choosePage') }}
        </option>
        <option
          v-for="page in linkable"
          :key="page.id"
          :value="page.id"
        >
          {{ page.title }} ({{ page.path }})
        </option>
      </select>
      <p
        v-if="broken"
        :id="`${base}-broken`"
        class="field-error"
      >
        {{ t('editor.panel.link.broken') }}
      </p>
    </template>

    <template v-if="kind === 'url'">
      <label :for="`${base}-href`">{{ t('editor.panel.link.addressField') }}</label>
      <input
        :id="`${base}-href`"
        type="text"
        inputmode="url"
        :value="href"
        :aria-invalid="error ? 'true' : undefined"
        :aria-describedby="`${base}-href-help${error ? ` ${base}-error` : ''}`"
        @input="onHref"
      >
      <p
        :id="`${base}-href-help`"
        class="help"
      >
        {{ t('editor.panel.link.addressHelp') }}
      </p>
    </template>

    <p
      v-if="error"
      :id="`${base}-error`"
      class="field-error"
      role="alert"
    >
      {{ error }}
    </p>
  </fieldset>
</template>
