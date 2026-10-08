<script setup lang="ts">
// Writing an item (015 US1, FR-009 to FR-014): a form generated from the collection's schema,
// one input per field in schema order. The slug follows the name until the member edits it.
// Values are checked as the member types with the same rules as the server; the server's report
// decides, and its problems that only it can see (deleted media, missing items) are shown until
// the field changes. A draft saves however incomplete; only malformed values are refused.
import { computed, onBeforeUnmount, onMounted, provide, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { onBeforeRouteLeave, useRoute, useRouter } from 'vue-router'
import { cmsApi, type Collection, type SchemaDocument } from '../../../api/cms'
import { itemsApi, refusalOf, type Item, type Problem } from '../../../api/items'
import { slugify } from '../../pages/slugify'
import ItemField from './ItemField.vue'
import ItemHistoryPanel from './ItemHistoryPanel.vue'
import ItemPublishBar from './ItemPublishBar.vue'
import ItemStatusBadge from './ItemStatusBadge.vue'
import { errorCodeOf, itemErrorMessage, problemMessage } from './itemErrors'
import { useItemRefs } from './itemRefs'
import { ITEM_REFS } from './itemRefsKey'
import { checkItem, isMalformed, SLUG_FIELD } from './itemValueRules'

const { t, te } = useI18n()
const route = useRoute()
const router = useRouter()

const siteId = computed(() => String(route.params.siteId))
const collectionId = computed(() => String(route.params.collectionId))
const itemId = computed(() => (route.params.itemId ? String(route.params.itemId) : null))

provide(ITEM_REFS, useItemRefs(() => siteId.value))

const SLUG_CHECK_DELAY_MS = 300

const collection = ref<Collection | null>(null)
const item = ref<Item | null>(null)
const slug = ref('')
const slugEdited = ref(false)
const values = ref<Record<string, unknown>>({})
const saved = ref('')
const serverProblems = ref<Problem[]>([])
const slugSuggestion = ref<string | null>(null)
const changedFields = ref<Set<string>>(new Set())
const loading = ref(true)
const saving = ref(false)
const loadError = ref<string | null>(null)
const message = ref<{ kind: 'error' | 'info' | 'conflict' | 'outdated'; text: string } | null>(null)
const showHistory = ref(false)
let slugTimer: ReturnType<typeof setTimeout> | undefined

const schema = computed<SchemaDocument | null>(() => collection.value?.schema ?? null)
const titleField = computed(() => schema.value?.fields.find((f) => f.id === schema.value?.titleFieldId) ?? null)
const snapshot = (): string => JSON.stringify({ slug: slug.value, values: values.value })
const dirty = computed(() => saved.value !== '' && snapshot() !== saved.value)

/** The problems shown: the local checks, plus the server's that only it can see, until their field changes. */
const problems = computed<Problem[]>(() => {
  if (!schema.value) return []
  const local = checkItem(schema.value, slug.value, values.value, itemId.value)
  const seen = new Set(local.map((p) => p.fieldId))
  return [...local, ...serverProblems.value.filter((p) => !seen.has(p.fieldId))]
})
const problemOf = (fieldId: string): Problem | null => problems.value.find((p) => p.fieldId === fieldId) ?? null
const malformed = computed(() => problems.value.filter(isMalformed))
const fieldName = (fieldId: string): string =>
  fieldId === SLUG_FIELD ? t('cms.items.form.slug')
    : fieldId === 'schedule' ? t('cms.items.form.schedule')
      : (schema.value?.fields.find((f) => f.id === fieldId)?.name ?? fieldId)
const anchorOf = (fieldId: string): string =>
  fieldId === SLUG_FIELD ? '#item-slug' : fieldId === 'schedule' ? '#item-history' : `#item-field-${fieldId}`

function startFrom(next: Item | null, schemaDocument: SchemaDocument): void {
  item.value = next
  slug.value = next?.slug ?? ''
  slugEdited.value = next !== null
  values.value = next ? { ...next.draft.values } : {}
  serverProblems.value = next?.report.problems ?? []
  saved.value = snapshot()
  changedFields.value = new Set()
  if (!next && titleField.value === null) slug.value = ''
  void schemaDocument
}

async function load(): Promise<void> {
  loading.value = true
  loadError.value = null
  try {
    const [loadedCollection, loadedItem] = await Promise.all([
      cmsApi.get(siteId.value, collectionId.value),
      itemId.value ? itemsApi.get(siteId.value, collectionId.value, itemId.value) : Promise.resolve(null),
    ])
    collection.value = loadedCollection
    startFrom(loadedItem, loadedCollection.schema)
  } catch (caught) {
    loadError.value = itemErrorMessage(t, te, caught)
  } finally {
    loading.value = false
  }
}

onMounted(load)
watch(itemId, (next, previous) => {
  if (next !== previous && !(previous === null && next === item.value?.id)) void load()
})

function setValue(fieldId: string, value: unknown): void {
  const next = { ...values.value }
  if (value === undefined) delete next[fieldId]
  else next[fieldId] = value
  values.value = next
  serverProblems.value = serverProblems.value.filter((p) => p.fieldId !== fieldId)

  // The slug follows the name until the member edits it (US1 scenario 2).
  if (!slugEdited.value && fieldId === titleField.value?.id) {
    slug.value = slugify(typeof value === 'string' ? value : '')
  }
}

function editSlug(next: string): void {
  slugEdited.value = true
  slug.value = next
}

// The slug is checked as it changes; only the save decides.
watch(slug, (next) => {
  clearTimeout(slugTimer)
  slugSuggestion.value = null
  serverProblems.value = serverProblems.value.filter((p) => p.fieldId !== SLUG_FIELD)
  if (!next || !collection.value) return
  slugTimer = setTimeout(async () => {
    try {
      const answer = await itemsApi.slugAvailability(siteId.value, collectionId.value, next, itemId.value ?? undefined)
      if (slug.value === next && !answer.available && answer.suggestion) slugSuggestion.value = answer.suggestion
    } catch {
      // Advisory only: the save gives the answer.
    }
  }, SLUG_CHECK_DELAY_MS)
})

function useSuggestion(): void {
  if (slugSuggestion.value) editSlug(slugSuggestion.value)
}

async function save(): Promise<void> {
  if (!collection.value || saving.value) return
  message.value = null
  if (malformed.value.length > 0) {
    focusSummary()
    return
  }

  saving.value = true
  const body = {
    slug: slug.value,
    values: { schemaVersion: 1, values: values.value },
    schemaRevision: collection.value.schemaRevision,
  }

  try {
    const result = item.value
      ? await itemsApi.save(siteId.value, collectionId.value, item.value.id, { ...body, revision: item.value.revision })
      : await itemsApi.create(siteId.value, collectionId.value, body)
    const created = item.value === null
    startFrom(result, collection.value.schema)
    message.value = { kind: 'info', text: t(created ? 'cms.items.form.created' : 'cms.items.form.saved') }
    if (created) await router.replace({ name: 'site-collection-item', params: { siteId: siteId.value, collectionId: collectionId.value, itemId: result.id } })
  } catch (caught) {
    await refused(caught)
  } finally {
    saving.value = false
  }
}

/** 035 FR-020: an out-of-date admin needs the page itself reloaded, to get the current admin. */
function reloadPage(): void {
  window.location.reload()
}

async function refused(caught: unknown): Promise<void> {
  const code = errorCodeOf(caught)
  const refusal = refusalOf(caught)

  if (code === 'cms.invalid_item' && refusal.report) {
    serverProblems.value = refusal.report.problems
    message.value = { kind: 'error', text: itemErrorMessage(t, te, caught) }
    focusSummary()
  } else if (code === 'cms.item_slug_taken') {
    slugSuggestion.value = refusal.suggestion ?? null
    serverProblems.value = [...serverProblems.value.filter((p) => p.fieldId !== SLUG_FIELD), { fieldId: SLUG_FIELD, problem: 'invalid_slug' }]
    message.value = { kind: 'error', text: itemErrorMessage(t, te, caught) }
  } else if (code === 'cms.admin_outdated') {
    message.value = { kind: 'outdated', text: t('cms.items.form.outdated') }
  } else if (code === 'cms.item_conflict') {
    message.value = { kind: 'conflict', text: t('cms.items.form.conflict') }
  } else if (code === 'cms.schema_changed') {
    await reloadSchema()
  } else if (code === 'cms.collection_not_found') {
    message.value = { kind: 'error', text: t('cms.items.errors.item_not_found') }
  } else {
    message.value = { kind: 'error', text: itemErrorMessage(t, te, caught) }
  }
}

/**
 * The schema changed under the form (spec Edge Cases): load the current schema, keep the
 * entered values of fields that still exist, and point at the fields that changed.
 */
async function reloadSchema(): Promise<void> {
  if (!collection.value) return
  const before = collection.value.schema
  const current = await cmsApi.get(siteId.value, collectionId.value)
  const kept: Record<string, unknown> = {}
  for (const field of current.schema.fields) {
    if (field.id in values.value) kept[field.id] = values.value[field.id]
  }

  const changed = new Set(current.schema.fields
    .filter((field) => JSON.stringify(field) !== JSON.stringify(before.fields.find((f) => f.id === field.id)))
    .map((f) => f.id))

  collection.value = current
  values.value = kept
  serverProblems.value = []
  changedFields.value = changed
  message.value = { kind: 'info', text: t('cms.items.form.schemaChanged') }
}

function focusSummary(): void {
  requestAnimationFrame(() => document.getElementById('item-problems')?.focus())
}

/** The publish bar changed the item (published, unpublished, scheduled, copied a version). */
function onItemChanged(next: Item): void {
  if (!collection.value) return
  const keepEdits = dirty.value
  const edits = { slug: slug.value, values: values.value }
  startFrom(next, collection.value.schema)
  if (keepEdits) {
    slug.value = edits.slug
    values.value = edits.values
  }
}

async function onDeleted(): Promise<void> {
  saved.value = snapshot()
  await router.push({ name: 'site-collection-items', params: { siteId: siteId.value, collectionId: collectionId.value } })
}

async function onDuplicated(copy: Item): Promise<void> {
  await router.push({ name: 'site-collection-item', params: { siteId: siteId.value, collectionId: collectionId.value, itemId: copy.id } })
}

function backToList(): void {
  void router.push({ name: 'site-collection-items', params: { siteId: siteId.value, collectionId: collectionId.value }, query: route.query })
}

// Leaving with unsaved changes asks first (FR-011).
onBeforeRouteLeave(() => (dirty.value ? window.confirm(t('cms.items.form.leaveConfirm')) : true))
function onBeforeUnload(event: BeforeUnloadEvent): void {
  if (!dirty.value) return
  event.preventDefault()
  event.returnValue = ''
}
onMounted(() => window.addEventListener('beforeunload', onBeforeUnload))
onBeforeUnmount(() => {
  window.removeEventListener('beforeunload', onBeforeUnload)
  clearTimeout(slugTimer)
})
</script>

<template>
  <section
    class="item-form"
    aria-labelledby="item-form-heading"
  >
    <p>
      <a
        href="#"
        @click.prevent="backToList"
      >{{ t('cms.items.form.back') }}</a>
    </p>

    <p
      v-if="loading"
      role="status"
    >
      {{ t('cms.items.form.loading') }}
    </p>
    <p
      v-else-if="loadError"
      class="field-error"
      role="alert"
    >
      {{ loadError }}
    </p>

    <form
      v-else-if="collection && schema"
      novalidate
      @submit.prevent="save"
    >
      <header class="page-header">
        <div>
          <h1 id="item-form-heading">
            {{ item ? (item.name ?? item.slug) : t('cms.items.form.newTitle') }}
          </h1>
          <p class="subtitle">
            {{ collection.name }}
            <ItemStatusBadge
              v-if="item"
              :item="item"
            />
          </p>
        </div>
        <div class="page-toolbar">
          <span
            v-if="dirty"
            class="help"
          >{{ t('cms.items.form.unsaved') }}</span>
          <button
            type="submit"
            class="primary"
            :disabled="saving"
          >
            {{ saving ? t('cms.items.form.saving') : t('cms.items.form.saveDraft') }}
          </button>
          <button
            v-if="item"
            type="button"
            :aria-expanded="showHistory"
            aria-controls="item-history"
            @click="showHistory = !showHistory"
          >
            {{ t('cms.items.publish.history') }}
          </button>
        </div>
      </header>

      <ItemPublishBar
        v-if="item"
        :site-id="siteId"
        :collection-id="collectionId"
        :item="item"
        :dirty="dirty"
        @changed="onItemChanged"
        @deleted="onDeleted"
        @duplicated="onDuplicated"
      />

      <div
        v-if="message"
        :class="message.kind === 'info' ? 'notice' : 'field-error'"
        :role="message.kind === 'info' ? 'status' : 'alert'"
      >
        {{ message.text }}
        <button
          v-if="message.kind === 'conflict'"
          type="button"
          @click="load"
        >
          {{ t('cms.items.form.reload') }}
        </button>
        <button
          v-if="message.kind === 'outdated'"
          type="button"
          @click="reloadPage"
        >
          {{ t('cms.items.form.reloadPage') }}
        </button>
      </div>

      <div
        v-if="problems.length > 0"
        id="item-problems"
        class="item-problems"
        tabindex="-1"
        :role="malformed.length > 0 ? 'alert' : undefined"
        aria-labelledby="item-problems-title"
      >
        <h2 id="item-problems-title">
          {{ t('cms.items.form.problemsTitle') }}
        </h2>
        <p>{{ malformed.length > 0 ? t('cms.items.form.malformedIntro') : t('cms.items.form.problemsIntro') }}</p>
        <ul>
          <li
            v-for="problem in problems"
            :key="`${problem.fieldId}-${problem.problem}`"
          >
            <a :href="anchorOf(problem.fieldId)">{{ fieldName(problem.fieldId) }}</a>:
            {{ problemMessage(t, te, problem) }}
          </li>
        </ul>
      </div>

      <div class="item-field">
        <label
          for="item-slug"
          class="item-field__label"
        >{{ t('cms.items.form.slug') }}</label>
        <p
          id="item-slug-help"
          class="help"
        >
          {{ t('cms.items.form.slugHelp') }}
        </p>
        <input
          id="item-slug"
          type="text"
          :value="slug"
          aria-describedby="item-slug-help item-slug-error"
          :aria-invalid="!!problemOf('slug') || !!slugSuggestion"
          @input="editSlug(($event.target as HTMLInputElement).value)"
        >
        <p
          id="item-slug-error"
          class="field-error"
        >
          <template v-if="slugSuggestion">
            {{ t('cms.items.form.slugTaken', { suggestion: slugSuggestion }) }}
            <button
              type="button"
              class="link"
              @click="useSuggestion"
            >
              {{ t('cms.items.form.useSuggestion', { suggestion: slugSuggestion }) }}
            </button>
          </template>
          <template v-else-if="problemOf('slug')">
            {{ problemMessage(t, te, problemOf('slug')!) }}
          </template>
        </p>
      </div>

      <ItemField
        v-for="field in schema.fields"
        :key="field.id"
        :field="field"
        :value="values[field.id]"
        :site-id="siteId"
        :item-id="item?.id ?? null"
        :problem="problemOf(field.id)"
        :changed="changedFields.has(field.id)"
        @update:value="setValue(field.id, $event)"
      />
    </form>

    <ItemHistoryPanel
      v-if="showHistory && item"
      id="item-history"
      :site-id="siteId"
      :collection-id="collectionId"
      :item="item"
      @changed="onItemChanged"
      @close="showHistory = false"
    />
  </section>
</template>
