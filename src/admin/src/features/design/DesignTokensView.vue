<script setup lang="ts">
// A site's design tokens: its colours, fonts, text sizes and spacing (011 US6, FR-031 to
// FR-035). Blocks refer to these by id, so a change here changes every block that uses the
// token, on every page, the next time the editor draws it.
//
// The screen edits a copy and saves the whole set against the version it read. Deleting a
// token first asks the server how many blocks use it and says so (clarification Q4); those
// blocks then show their default. A member without the design capability sees it read-only.
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import { ApiError } from '../../api/request'
import { editorApi, type DesignTokens } from '../../api/editor'
import { pagesApi } from '../../api/pages'
import { serverKey } from '../../editor/editorErrors'
import type { TokenSet } from '../../editor/tokens/defaultTokens'
import { validateTokenSet } from '../../editor/tokens/tokenValidator'
import ColorList from './ColorList.vue'
import FontSelect from './FontSelect.vue'
import ScaleList from './ScaleList.vue'

const { t } = useI18n()
const route = useRoute()
const siteId = computed(() => String(route.params.siteId))

const loaded = ref<DesignTokens | null>(null)
const draft = ref<TokenSet | null>(null)
const loading = ref(true)
const loadFailed = ref(false)
const saving = ref(false)
const message = ref('')
const error = ref('')
const outOfDate = ref(false)

const readOnly = computed(() => !loaded.value?.permissions.canManage)

async function load(): Promise<void> {
  loading.value = true
  loadFailed.value = false
  outOfDate.value = false
  error.value = ''
  try {
    loaded.value = await editorApi.designTokens(siteId.value)
    draft.value = JSON.parse(JSON.stringify(loaded.value.document)) as TokenSet
  } catch {
    loadFailed.value = true
  } finally {
    loading.value = false
  }
}

onMounted(load)

/** A new, never-reused id for a token of one kind (research ED-14). */
function newId(prefix: 'color' | 'text' | 'space'): string {
  const alphabet = 'abcdefghijklmnopqrstuvwxyz0123456789'
  const bytes = crypto.getRandomValues(new Uint8Array(8))
  return `${prefix}-${Array.from(bytes, (b) => alphabet[b % alphabet.length]).join('')}`
}

function addColor(): void {
  draft.value!.colors.push({ id: newId('color'), name: t('design.colors.newName', { n: draft.value!.colors.length + 1 }), value: '#000000' })
}

function addStep(kind: 'sizes' | 'spacing'): void {
  const list = kind === 'sizes' ? draft.value!.typography.sizes : draft.value!.spacing
  list.push({ id: newId(kind === 'sizes' ? 'text' : 'space'), name: t(`design.${kind}.newName`, { n: list.length + 1 }), rem: 1 })
}

/** Deleting a token says first how many blocks use it; they fall back to their default. */
async function remove(kind: 'colors' | 'sizes' | 'spacing', id: string): Promise<void> {
  const list = kind === 'colors' ? draft.value!.colors : kind === 'sizes' ? draft.value!.typography.sizes : draft.value!.spacing
  if (list.length <= 1) return
  const token = list.find((entry) => entry.id === id)
  if (!token) return

  let blocks: number
  try {
    blocks = (await pagesApi.tokenUsage(siteId.value, id)).blockCount
  } catch {
    blocks = -1
  }

  const question = blocks < 0
    ? t('design.confirmRemoveUnknown', { name: token.name })
    : t(`design.confirmRemove.${kind}`, { name: token.name, count: blocks }, blocks)
  if (!window.confirm(question)) return

  const index = list.findIndex((entry) => entry.id === id)
  if (index >= 0) list.splice(index, 1)
}

async function save(): Promise<void> {
  if (!draft.value || !loaded.value) return
  message.value = ''
  error.value = ''

  const check = validateTokenSet(draft.value)
  if (!check.valid) {
    error.value = t(serverKey(check.code))
    return
  }

  saving.value = true
  try {
    loaded.value = await editorApi.saveDesignTokens(siteId.value, draft.value, loaded.value.version)
    draft.value = JSON.parse(JSON.stringify(loaded.value.document)) as TokenSet
    message.value = t('design.saved')
  } catch (failure) {
    const code = failure instanceof ApiError ? failure.errorCode : undefined
    outOfDate.value = code === 'editor.out_of_date'
    error.value = t(serverKey(code))
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="design">
    <div class="page-header">
      <div>
        <p class="breadcrumb">
          <RouterLink :to="{ name: 'site', params: { siteId } }">
            {{ t('design.backToSite') }}
          </RouterLink>
        </p>
        <h1>{{ t('design.title') }}</h1>
        <p class="subtitle">
          {{ t('design.subtitle') }}
        </p>
      </div>
      <button
        v-if="draft && !readOnly"
        type="button"
        class="primary"
        :disabled="saving"
        @click="save"
      >
        {{ saving ? t('design.saving') : t('design.save') }}
      </button>
    </div>

    <p
      v-if="loading"
      role="status"
    >
      {{ t('design.loading') }}
    </p>
    <div
      v-else-if="loadFailed"
      role="alert"
    >
      <p>{{ t('design.loadFailed') }}</p>
      <button
        type="button"
        @click="load"
      >
        {{ t('design.reload') }}
      </button>
    </div>

    <template v-else-if="draft">
      <p
        v-if="readOnly"
        class="help"
      >
        {{ t('design.readOnly') }}
      </p>
      <p
        v-if="message"
        role="status"
        class="design-message"
      >
        {{ message }}
      </p>
      <div
        v-if="error"
        role="alert"
        class="error"
      >
        <p>{{ error }}</p>
        <button
          v-if="outOfDate"
          type="button"
          @click="load"
        >
          {{ t('design.reload') }}
        </button>
      </div>

      <ColorList
        :colors="draft.colors"
        :read-only="readOnly"
        :max="24"
        @add="addColor"
        @remove="(id) => remove('colors', id)"
      />

      <fieldset class="design-group">
        <legend>{{ t('design.typography.title') }}</legend>
        <FontSelect
          id="design-heading-font"
          v-model="draft.typography.headingFont"
          :label="t('design.typography.headingFont')"
          :disabled="readOnly"
        />
        <FontSelect
          id="design-body-font"
          v-model="draft.typography.bodyFont"
          :label="t('design.typography.bodyFont')"
          :disabled="readOnly"
        />
        <label for="design-base-size">{{ t('design.typography.baseSize') }}</label>
        <input
          id="design-base-size"
          v-model.number="draft.typography.baseSizePx"
          type="number"
          min="12"
          max="24"
          :disabled="readOnly"
        >
      </fieldset>

      <ScaleList
        :steps="draft.typography.sizes"
        kind="sizes"
        :read-only="readOnly"
        :min="0.5"
        :max="6"
        :max-steps="12"
        @add="addStep('sizes')"
        @remove="(id) => remove('sizes', id)"
      />
      <ScaleList
        :steps="draft.spacing"
        kind="spacing"
        :read-only="readOnly"
        :min="0"
        :max="16"
        :max-steps="12"
        @add="addStep('spacing')"
        @remove="(id) => remove('spacing', id)"
      />
    </template>
  </div>
</template>

<style scoped>
.design {
  display: grid;
  gap: 1rem;
  max-width: 48rem;
}

.design :deep(.design-group) {
  display: grid;
  gap: 0.5rem;
  border: 1px solid var(--border);
  border-radius: var(--radius);
  padding: 1rem;
}

.design :deep(.design-group > legend) {
  font-weight: 650;
  padding: 0 0.25rem;
}

.design :deep(.design-list) {
  list-style: none;
  margin: 0;
  padding: 0;
  display: grid;
  gap: 0.5rem;
}

.design :deep(.design-row) {
  display: grid;
  grid-template-columns: auto minmax(8rem, 1fr) minmax(6rem, 10rem) auto;
  align-items: center;
  gap: 0.5rem;
}

.design :deep(.design-swatch) {
  width: 1.75rem;
  height: 1.75rem;
  border-radius: 4px;
  border: 1px solid var(--border);
}

.design :deep(.design-preview--spacing) {
  display: inline-block;
  height: 0.75rem;
  background: var(--accent);
}

.design :deep(.design-font__sample) {
  margin: 0 0 0.75rem;
  font-size: 1.125rem;
}

.design-message {
  color: #146c2e;
}
</style>
