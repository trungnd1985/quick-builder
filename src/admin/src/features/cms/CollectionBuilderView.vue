<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { ApiError, cmsApi, type Collection, type FieldType, type Preview, type SaveRequest, type SchemaDocument, type SchemaField } from '../../api/cms'
import { FIELD_TYPES, fieldIndexOf, newField, TemporaryIds, validateSchema } from './collectionSchema'
import { normalise } from './collectionSchemaRules'
import { cmsErrorMessage, errorCodeOf, ruleMessage } from './cmsErrors'
import { needsPreview } from './schemaChanges'
import DeleteCollectionDialog from './DeleteCollectionDialog.vue'
import FieldEditor from './FieldEditor.vue'
import FieldList from './FieldList.vue'
import SchemaHistoryPanel from './SchemaHistoryPanel.vue'
import SchemaPreviewDialog from './SchemaPreviewDialog.vue'

// 014 US1–US6: one collection's details and fields. Changes stay here until Save. A save whose
// changes are all free goes straight to the server (FR-030); any other shows the preview first,
// and confirming sends its digest (FR-023). Editors see everything read-only. The server
// validates, counts and enforces confirmation again whatever this screen decided.

interface Draft {
  name: string
  slug: string
  description: string
  schema: SchemaDocument
}

const { t, te } = useI18n()
const route = useRoute()
const router = useRouter()

const siteId = computed(() => String(route.params.siteId))
const collectionId = computed(() => String(route.params.collectionId))

const collection = ref<Collection | null>(null)
const draft = ref<Draft | null>(null)
const others = ref<{ id: string; name: string }[]>([])
const loading = ref(true)
const loadError = ref<string | null>(null)
const selectedId = ref<string | null>(null)
const autoKeyIds = ref(new Set<string>())
const fillIns = ref<Record<string, unknown>>({})
const newType = ref<FieldType>('text')
const saving = ref(false)
const saveError = ref<string | null>(null)
const invalidIndex = ref<number | null>(null)
const preview = ref<Preview | null>(null)
const outOfDate = ref(false)
const showHistory = ref(false)
const deleting = ref(false)
const status = ref('')
let ids = new TemporaryIds()

const canManage = computed(() => collection.value?.canManage === true)
const readOnly = computed(() => !canManage.value || outOfDate.value)
const fields = computed(() => draft.value?.schema.fields ?? [])
const selected = computed(() => fields.value.find((f) => f.id === selectedId.value) ?? null)
const storedFields = computed(() => new Map((collection.value?.schema.fields ?? []).map((f) => [f.id, f])))
const proposal = computed(() => (draft.value ? normalise(draft.value.schema) : null))
const titleFieldId = computed(() => proposal.value?.titleFieldId)

const allowedFillIns = computed(() => Object.fromEntries(
  Object.entries(fillIns.value).filter(([id, value]) => value !== undefined && fillInAllowed(fields.value.find((f) => f.id === id))),
))

const dirty = computed(() => {
  if (!collection.value || !draft.value) return false
  const stored = collection.value
  return Object.keys(allowedFillIns.value).length > 0
    || draft.value.name !== stored.name
    || draft.value.slug !== stored.slug
    || (draft.value.description || null) !== stored.description
    || JSON.stringify(draft.value.schema) !== JSON.stringify(stored.schema)
})

onMounted(load)
watch(collectionId, load)

function reset(from: Collection): void {
  collection.value = from
  draft.value = {
    name: from.name,
    slug: from.slug,
    description: from.description ?? '',
    schema: structuredClone(from.schema),
  }
  ids = new TemporaryIds(from.schema)
  autoKeyIds.value = new Set()
  fillIns.value = {}
  invalidIndex.value = null
  outOfDate.value = false
}

async function load(): Promise<void> {
  loading.value = true
  loadError.value = null

  try {
    const [loaded, list] = await Promise.all([cmsApi.get(siteId.value, collectionId.value), cmsApi.list(siteId.value)])
    reset(loaded)
    others.value = list.collections.map((c) => ({ id: c.id, name: c.name }))
    selectedId.value = loaded.schema.fields[0]?.id ?? null
  } catch (caught) {
    loadError.value = cmsErrorMessage(t, te, caught)
  } finally {
    loading.value = false
  }
}

function fillInAllowed(field: SchemaField | undefined): boolean {
  if (!field || !['text', 'number', 'boolean', 'date', 'option'].includes(field.type)) return false
  const stored = storedFields.value.get(field.id)
  return !stored || (!stored.required && field.required)
}

async function addField(): Promise<void> {
  if (!draft.value) return

  const id = ids.take()
  const field = newField(newType.value, id, t('cms.add.newName', { type: t(`cms.types.${newType.value}`) }))
  draft.value.schema.fields.push(field)
  autoKeyIds.value.add(id)
  selectedId.value = id
  status.value = t('cms.status.added', { name: field.name })
  await nextTick()
  document.getElementById(`field-${id.replace('~', 't')}-settings-name`)?.focus()
}

function updateField(field: SchemaField): void {
  if (!draft.value) return
  draft.value.schema.fields = draft.value.schema.fields.map((f) => (f.id === field.id ? field : f))
}

function removeField(id: string): void {
  if (!draft.value) return
  const field = fields.value.find((f) => f.id === id)
  draft.value.schema.fields = draft.value.schema.fields.filter((f) => f.id !== id)
  delete fillIns.value[id]
  selectedId.value = draft.value.schema.fields[0]?.id ?? null
  if (field) status.value = t('cms.status.removed', { name: field.name })
}

function moveField(from: number, to: number): void {
  if (!draft.value) return
  const next = [...draft.value.schema.fields]
  const [field] = next.splice(from, 1)
  if (!field) return
  next.splice(to, 0, field)
  draft.value.schema.fields = next
  status.value = t('cms.status.moved', { name: field.name, position: to + 1, count: next.length })
}

function makeTitle(id: string): void {
  if (!draft.value) return
  draft.value.schema.titleFieldId = id
  draft.value.schema.fields = draft.value.schema.fields.map((f) => (f.id === id ? { ...f, required: true } : f))
}

function addOption(fieldId: string): void {
  const field = fields.value.find((f) => f.id === fieldId)
  if (field) updateField({ ...field, options: [...(field.options ?? []), { id: ids.take(), label: '' }] })
}

function request(digest?: string): SaveRequest | null {
  if (!collection.value || !draft.value || !proposal.value) return null
  return {
    revision: collection.value.revision,
    name: draft.value.name.trim(),
    slug: draft.value.slug.trim(),
    description: draft.value.description.trim() || null,
    schema: proposal.value,
    fillIns: allowedFillIns.value,
    ...(digest ? { confirmation: { digest } } : {}),
  }
}

function showInvalid(pointer: string | undefined, reason: string): void {
  const index = pointer ? fieldIndexOf(pointer) : null
  invalidIndex.value = index
  if (index !== null) selectedId.value = fields.value[index]?.id ?? selectedId.value
  saveError.value = t('cms.builder.invalid', { reason })
}

async function save(): Promise<void> {
  const body = request()
  if (!body || !collection.value) return

  saveError.value = null
  invalidIndex.value = null

  const checked = validateSchema(body.schema, 'proposal')
  if (!checked.valid) {
    showInvalid(checked.pointer, ruleMessage(t, te, checked.rule))
    return
  }

  if (!needsPreview(collection.value.schema, body.schema, new Set(Object.keys(body.fillIns ?? {})))) {
    await submit()
    return
  }

  saving.value = true
  try {
    preview.value = await cmsApi.preview(siteId.value, collectionId.value, body)
  } catch (caught) {
    failed(caught)
  } finally {
    saving.value = false
  }
}

async function submit(digest?: string): Promise<void> {
  const body = request(digest)
  if (!body) return

  saving.value = true
  saveError.value = null

  try {
    const saved = await cmsApi.save(siteId.value, collectionId.value, body)
    const selectedNow = selectedId.value
    reset(saved)
    selectedId.value = selectedNow ? saved.idMap[selectedNow] ?? selectedNow : null
    preview.value = null
    status.value = t('cms.status.saved', { revision: saved.revision })
  } catch (caught) {
    failed(caught)
  } finally {
    saving.value = false
  }
}

function failed(caught: unknown): void {
  const code = errorCodeOf(caught)
  if (code === 'cms.revision_conflict') {
    preview.value = null
    outOfDate.value = true
    return
  }

  if ((code === 'cms.preview_outdated' || code === 'cms.confirmation_required') && caught instanceof ApiError) {
    preview.value = (caught.problem as unknown as { preview?: Preview })?.preview ?? null
    return
  }

  preview.value = null
  if (code === 'cms.invalid_schema' && caught instanceof ApiError) {
    showInvalid(caught.problem?.pointer, ruleMessage(t, te, caught.problem?.rule))
    return
  }

  saveError.value = cmsErrorMessage(t, te, caught)
}

async function deleted(): Promise<void> {
  deleting.value = false
  await router.push({ name: 'site-collections', params: { siteId: siteId.value } })
}

const savingLabel = computed(() => {
  const rewrites = preview.value?.changes.some((c) => (c.counts.converted ?? 0) + (c.counts.cleared ?? 0) + (c.counts.partlyLost ?? 0) + (c.counts.filled ?? 0) > 0)
  return rewrites && preview.value ? t('cms.builder.updating', { count: preview.value.itemCount }, preview.value.itemCount) : t('cms.builder.saving')
})
</script>

<template>
  <section aria-labelledby="builder-heading">
    <p>
      <RouterLink :to="{ name: 'site-collections', params: { siteId } }">
        {{ t('cms.builder.back') }}
      </RouterLink>
    </p>

    <p
      class="visually-hidden"
      role="status"
      aria-live="polite"
    >
      {{ status }}
    </p>

    <p
      v-if="loading && !collection"
      class="status"
      role="status"
    >
      {{ t('cms.builder.loading') }}
    </p>

    <div
      v-else-if="loadError && !collection"
      class="card error-card"
      role="alert"
    >
      <h1 id="builder-heading">
        {{ t('cms.builder.errorTitle') }}
      </h1>
      <p>{{ loadError }}</p>
      <button
        type="button"
        @click="load()"
      >
        {{ t('cms.builder.retry') }}
      </button>
    </div>

    <template v-else-if="collection && draft">
      <header class="page-header">
        <div>
          <h1 id="builder-heading">
            {{ collection.name }}
          </h1>
          <p class="subtitle">
            {{ t('cms.builder.meta', { revision: collection.revision, who: collection.updatedBy.displayName }) }}
            · {{ t('cms.builder.items', { count: collection.itemCount }, collection.itemCount) }}
          </p>
        </div>
        <div class="page-toolbar">
          <button
            type="button"
            :aria-expanded="showHistory"
            aria-controls="schema-history"
            @click="showHistory = !showHistory"
          >
            {{ showHistory ? t('cms.builder.hideHistory') : t('cms.builder.history') }}
          </button>
          <button
            v-if="canManage"
            type="button"
            class="danger"
            @click="deleting = true"
          >
            {{ t('cms.builder.delete') }}
          </button>
        </div>
      </header>

      <p
        v-if="!canManage"
        class="help"
      >
        {{ t('cms.builder.readOnly') }}
      </p>

      <div
        v-if="outOfDate"
        class="card error"
        role="alert"
      >
        <p>{{ t('cms.builder.outOfDate') }}</p>
        <button
          type="button"
          @click="load()"
        >
          {{ t('cms.builder.reload') }}
        </button>
      </div>

      <form
        class="card"
        aria-labelledby="builder-details-heading"
        novalidate
        @submit.prevent="save"
      >
        <h2 id="builder-details-heading">
          {{ t('cms.builder.details') }}
        </h2>
        <fieldset :disabled="readOnly">
          <div class="field">
            <label for="builder-name">{{ t('cms.create.name') }}</label>
            <input
              id="builder-name"
              v-model="draft.name"
              type="text"
              maxlength="100"
            >
          </div>
          <div class="field">
            <label for="builder-slug">{{ t('cms.create.slug') }}</label>
            <input
              id="builder-slug"
              v-model="draft.slug"
              type="text"
              maxlength="64"
            >
          </div>
          <div class="field">
            <label for="builder-description">{{ t('cms.create.description') }}</label>
            <textarea
              id="builder-description"
              v-model="draft.description"
              maxlength="500"
              rows="2"
            />
          </div>
        </fieldset>
      </form>

      <div class="builder-columns">
        <section
          class="card"
          aria-labelledby="builder-fields-heading"
        >
          <h2 id="builder-fields-heading">
            {{ t('cms.builder.fields') }}
          </h2>
          <p
            v-if="fields.length === 0"
            class="help"
          >
            {{ t('cms.builder.empty') }}
          </p>
          <FieldList
            v-else
            :fields="fields"
            :selected-id="selectedId"
            :title-field-id="titleFieldId"
            :invalid-index="invalidIndex"
            :read-only="readOnly"
            @select="selectedId = $event"
            @move="moveField"
          />
          <div
            v-if="!readOnly"
            class="add-field"
          >
            <label for="builder-new-type">{{ t('cms.add.type') }}</label>
            <select
              id="builder-new-type"
              v-model="newType"
            >
              <option
                v-for="type in FIELD_TYPES"
                :key="type"
                :value="type"
              >
                {{ t(`cms.types.${type}`) }}
              </option>
            </select>
            <button
              type="button"
              @click="addField"
            >
              {{ t('cms.add.add') }}
            </button>
          </div>
        </section>

        <FieldEditor
          v-if="selected"
          :key="selected.id"
          :field="selected"
          :stored="storedFields.get(selected.id)"
          :is-title="selected.id === titleFieldId"
          :auto-key="autoKeyIds.has(selected.id)"
          :collections="others"
          :collection-id="collection.id"
          :collection-name="draft.name"
          :read-only="readOnly"
          :fill-in-allowed="fillInAllowed(selected)"
          :fill-in="fillIns[selected.id]"
          @update="updateField"
          @key-edited="autoKeyIds.delete(selected.id)"
          @remove="removeField(selected.id)"
          @make-title="makeTitle(selected.id)"
          @add-option="addOption(selected.id)"
          @fill-in="fillIns[selected.id] = $event"
        />
        <p
          v-else
          class="card help"
        >
          {{ t('cms.builder.noFieldSelected') }}
        </p>
      </div>

      <div
        v-if="!readOnly"
        class="card save-bar"
      >
        <p
          v-if="saveError"
          class="error"
          role="alert"
        >
          {{ saveError }}
        </p>
        <p
          v-else-if="dirty"
          class="help"
        >
          {{ t('cms.builder.unsaved') }}
        </p>
        <div class="actions">
          <button
            type="button"
            :disabled="!dirty || saving"
            @click="reset(collection)"
          >
            {{ t('cms.builder.discard') }}
          </button>
          <button
            type="button"
            class="primary"
            :disabled="!dirty || saving"
            @click="save"
          >
            {{ saving ? savingLabel : t('cms.builder.save') }}
          </button>
        </div>
      </div>

      <SchemaHistoryPanel
        v-if="showHistory"
        id="schema-history"
        :site-id="siteId"
        :collection-id="collection.id"
        :revision="collection.revision"
      />

      <SchemaPreviewDialog
        v-if="preview"
        :preview="preview"
        :saving="saving"
        @confirm="submit(preview.digest)"
        @cancel="preview = null"
      />

      <DeleteCollectionDialog
        v-if="deleting"
        :site-id="siteId"
        :collection-id="collection.id"
        :name="collection.name"
        :revision="collection.revision"
        :item-count="collection.itemCount"
        @deleted="deleted"
        @cancel="deleting = false"
      />
    </template>
  </section>
</template>

<style scoped>
.builder-columns {
  display: grid;
  grid-template-columns: minmax(16rem, 1fr) 2fr;
  gap: 1rem;
  align-items: start;
}

.add-field {
  display: flex;
  gap: 0.5rem;
  align-items: center;
  margin-top: 1rem;
}

@media (width < 48rem) {
  .builder-columns {
    grid-template-columns: 1fr;
  }
}
</style>
