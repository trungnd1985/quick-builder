<script setup lang="ts">
// The page's Search and sharing settings (017 US1, research SE-21).
//
// Every change is a setSeo operation through the editor store, so undo, autosave and the
// recovery copy cover it like any block property. Each field coalesces its keystrokes into one
// undo step. The canonical address and hiding belong to members who may change the page
// structure (SE-03): the server refuses them otherwise, and here they are disabled with the
// reason.
//
// On a template (US4, research SE-04) each field can also read the item shown: a bound text
// field offers the text to use when an item has none instead of its own value, and the
// previews show the sample item's values.
import { computed, nextTick, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { setSeo } from '../ops'
import { useEditorStore } from '../state/editorStore'
import MediaControl from '../panel/controls/MediaControl.vue'
import BindingControl from '../panel/controls/BindingControl.vue'
import { resolveBinding } from '../bindings/resolve'
import { isObject, type Json, type JsonObject } from '../schema/subsetValidator'
import SearchPreview from './SearchPreview.vue'
import SharingPreview from './SharingPreview.vue'
import CanonicalControl from './CanonicalControl.vue'
import {
  addressOf,
  effective,
  lengthOf,
  maxLength,
  RECOMMENDED_LENGTH,
  seoOf,
  SEO_TEXT_FIELDS,
  text,
  type SeoTextField,
  type SitePresentation,
} from './seoFields'

const props = defineProps<{
  siteId: string
  pageTitle: string
  path: string
  site: SitePresentation
  canManageStructure: boolean
}>()
const emit = defineEmits<{ close: [] }>()
const { t } = useI18n()
const store = useEditorStore()

const dialogId = 'seo-panel'
const firstField = ref<HTMLInputElement | null>(null)

/** Why the last change of a field was refused, until it changes again. */
const errors = ref<Partial<Record<string, string>>>({})

const seo = computed(() => (store.root ? seoOf(store.root.props) : {}))
const readOnly = computed(() => store.readOnly !== null)
const isTemplate = computed(() => !!store.bindings.templateCollectionId)

/** Whether a metadata field reads the item, on a template. */
function isBound(field: string): boolean {
  return isTemplate.value && isObject(seo.value.bind) && field in seo.value.bind
}

/** The text a bound field falls back to when an item has none. */
function fallbackOf(field: SeoTextField): string {
  const fallback = seo.value.fallback
  return isObject(fallback) && typeof fallback[field] === 'string' ? fallback[field] : ''
}

/**
 * The metadata as the sample item's page has it: each bound field its resolved value, else its
 * fallback text, else nothing, so the default applies.
 */
const resolved = computed<JsonObject>(() => {
  const own = { ...seo.value }
  if (!isTemplate.value || !store.root) return own
  for (const field of SEO_TEXT_FIELDS) {
    if (!isBound(field)) continue
    const bound = resolveBinding(store.root, `seo.${field}`, store.bindings.data, [])
    const value = bound.state === 'value' && bound.text ? bound.text : fallbackOf(field)
    if (value) own[field] = value
    else delete own[field]
  }
  return own
})

const shownTitle = computed(() => (isTemplate.value ? store.bindings.sampleItem?.name ?? props.pageTitle : props.pageTitle))
const shown = computed(() => effective(resolved.value, shownTitle.value, props.site))
const address = computed(() => addressOf(props.site.baseUrl, props.path))

const LABELS: Record<SeoTextField, string> = {
  title: 'editor.seo.metaTitle',
  description: 'editor.seo.metaDescription',
  ogTitle: 'editor.seo.ogTitle',
  ogDescription: 'editor.seo.ogDescription',
}

/** Shown under a field that is left empty: what will be used instead (FR-005). */
const placeholders = computed<Record<SeoTextField, string>>(() => ({
  title: props.pageTitle + (props.site.titleSuffix ?? ''),
  description: '',
  ogTitle: shown.value.title,
  ogDescription: shown.value.description,
}))

function change(field: string, value: Json | undefined): void {
  const refusal = store.apply(setSeo(store.root!, field, value, store.selection), { coalesce: `seo:${field}` })
  errors.value = { ...errors.value, [field]: refusal ? refusalMessage(field, refusal.keyword) : undefined }
}

function refusalMessage(field: string, keyword: string | undefined): string {
  if (field === 'canonical') return t('editor.seo.canonical.invalid')
  if (keyword === 'maxLength' && (SEO_TEXT_FIELDS as readonly string[]).includes(field)) {
    return t('editor.seo.tooLong', { max: maxLength(field as SeoTextField) })
  }
  if (keyword === 'pattern') return t('editor.seo.oneLine')
  return t('editor.seo.invalid')
}

function onText(field: SeoTextField, event: Event): void {
  const value = (event.target as HTMLInputElement | HTMLTextAreaElement).value
  change(field, value === '' ? undefined : value)
}

function recommendation(field: SeoTextField): string | null {
  const limit = RECOMMENDED_LENGTH[field]
  return limit !== null && lengthOf(text(seo.value, field)) > limit ? t('editor.seo.longerThanShown', { limit }) : null
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('close')
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
    @click.self="emit('close')"
  >
    <div
      class="card dialog dialog--wide seo-panel"
      role="dialog"
      aria-modal="true"
      :aria-labelledby="`${dialogId}-heading`"
      @keydown="onKeydown"
    >
      <h2 :id="`${dialogId}-heading`">
        {{ t('editor.seo.title') }}
      </h2>

      <div class="seo-panel__columns">
        <div class="seo-panel__fields">
          <p
            v-if="isTemplate"
            class="hint"
          >
            {{ t('editor.seo.templateHint') }}
          </p>

          <div
            v-for="field in SEO_TEXT_FIELDS"
            :key="field"
            class="field"
          >
            <label :for="`${dialogId}-${field}`">{{ isBound(field) ? t('editor.seo.whenNone', { field: t(LABELS[field]) }) : t(LABELS[field]) }}</label>
            <input
              v-if="isBound(field)"
              :id="`${dialogId}-${field}`"
              :ref="field === 'title' ? (el: unknown) => (firstField = el as HTMLInputElement | null) : undefined"
              type="text"
              :value="fallbackOf(field)"
              :disabled="readOnly"
              :aria-describedby="errors[`fallback.${field}`] ? `${dialogId}-${field}-error` : undefined"
              @input="change(`fallback.${field}`, ($event.target as HTMLInputElement).value || undefined)"
            >
            <component
              :is="field === 'description' || field === 'ogDescription' ? 'textarea' : 'input'"
              v-else
              :id="`${dialogId}-${field}`"
              :ref="field === 'title' ? (el: unknown) => (firstField = el as HTMLInputElement | null) : undefined"
              :value="text(seo, field)"
              :placeholder="placeholders[field]"
              :disabled="readOnly"
              :rows="field === 'description' || field === 'ogDescription' ? 3 : undefined"
              :aria-describedby="`${dialogId}-${field}-count${errors[field] ? ` ${dialogId}-${field}-error` : ''}`"
              :aria-invalid="errors[field] ? 'true' : undefined"
              @input="onText(field, $event)"
            />
            <p
              v-if="!isBound(field)"
              :id="`${dialogId}-${field}-count`"
              class="hint"
            >
              {{ t('editor.seo.count', { count: lengthOf(text(seo, field)), max: maxLength(field) }) }}
            </p>
            <p
              v-if="recommendation(field)"
              class="warning"
              role="note"
            >
              {{ recommendation(field) }}
            </p>
            <p
              v-if="errors[field] || errors[`fallback.${field}`]"
              :id="`${dialogId}-${field}-error`"
              class="error"
              role="alert"
            >
              {{ errors[field] ?? errors[`fallback.${field}`] }}
            </p>
            <BindingControl
              v-if="isTemplate && store.root && !readOnly"
              :node="store.root"
              :prop="`seo.${field}`"
              :label="t(LABELS[field])"
            />
          </div>

          <MediaControl
            :id="`${dialogId}-ogImage`"
            :site-id="siteId"
            :value="seo.ogImage"
            :label="t('editor.seo.ogImage')"
            prop="ogImage"
            kind="image"
            @change="(value) => change('ogImage', value)"
            @choose="(value) => change('ogImage', value)"
          />
          <BindingControl
            v-if="isTemplate && store.root && !readOnly"
            :node="store.root"
            prop="seo.ogImage"
            :label="t('editor.seo.ogImage')"
          />

          <CanonicalControl
            :id="`${dialogId}-canonical`"
            :site-id="siteId"
            :value="seo.canonical"
            :disabled="readOnly || !canManageStructure"
            :described-by="canManageStructure ? undefined : `${dialogId}-restricted`"
            :error="errors.canonical"
            @change="(value) => change('canonical', value)"
          />

          <fieldset class="seo-panel__switches">
            <legend>{{ t('editor.seo.visibility') }}</legend>
            <label class="checkbox">
              <input
                :id="`${dialogId}-noindex`"
                type="checkbox"
                :checked="seo.noindex === true"
                :disabled="readOnly || !canManageStructure"
                :aria-describedby="canManageStructure ? undefined : `${dialogId}-restricted`"
                @change="change('noindex', ($event.target as HTMLInputElement).checked ? true : undefined)"
              >
              {{ t('editor.seo.noindex') }}
            </label>
            <label class="checkbox">
              <input
                :id="`${dialogId}-noSitemap`"
                type="checkbox"
                :checked="seo.noSitemap === true"
                :disabled="readOnly"
                @change="change('noSitemap', ($event.target as HTMLInputElement).checked ? true : undefined)"
              >
              {{ t('editor.seo.noSitemap') }}
            </label>
            <BindingControl
              v-if="isTemplate && store.root && !readOnly && canManageStructure"
              :node="store.root"
              prop="seo.noindex"
              :label="t('editor.seo.hideItem')"
            />
            <p
              v-if="!canManageStructure"
              :id="`${dialogId}-restricted`"
              class="hint"
            >
              {{ t('editor.seo.restricted') }}
            </p>
          </fieldset>
        </div>

        <div class="seo-panel__previews">
          <SearchPreview
            :id="`${dialogId}-search`"
            :title="shown.title"
            :address="address"
            :description="shown.description"
          />
          <SharingPreview
            :id="`${dialogId}-sharing`"
            :title="shown.ogTitle"
            :description="shown.ogDescription"
            :site-name="site.name"
            :has-image="!!seo.ogImage || isBound('ogImage')"
          />
        </div>
      </div>

      <div class="actions">
        <button
          type="button"
          class="primary"
          @click="emit('close')"
        >
          {{ t('editor.seo.close') }}
        </button>
      </div>
    </div>
  </div>
</template>
