<script setup lang="ts">
// Share preview (022 User Stories 2 and 4, FR-005, FR-014, FR-018; contracts/admin-preview.md
// §2): create a time-limited link for a page or item, draft or one version, see its active
// links, and revoke them. The full link is shown once, right after it is created.
import { computed, nextTick, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ApiError } from '../../api/request'
import { MAX_ACTIVE_LINKS, previewLinksApi, type PreviewKind, type PreviewLink } from '../../api/previewLinks'
import ZonedDateTimeInput from '../../components/ZonedDateTimeInput.vue'

const props = defineProps<{
  siteId: string
  kind: PreviewKind
  documentId: string
  /** A version to share instead of the draft. */
  versionNumber?: number | null
  /** For a template page, the item it shows. */
  sampleItemId?: string | null
}>()
const emit = defineEmits<{ close: [] }>()
const { t, d, te } = useI18n()

type Expiry = 'hour' | 'day' | 'week' | 'month' | 'custom'
const HOUR = 60 * 60 * 1000
const spans: Record<Exclude<Expiry, 'custom'>, number> = { hour: HOUR, day: 24 * HOUR, week: 7 * 24 * HOUR, month: 30 * 24 * HOUR }

const label = ref('')
const expiry = ref<Expiry>('week')
const custom = ref<string | null>(null)
const links = ref<PreviewLink[]>([])
const created = ref<{ url: string; expiresAtUtc: string } | null>(null)
const copied = ref(false)
const busy = ref(false)
const error = ref<string | null>(null)
const confirming = ref<string | 'all' | null>(null)
const heading = ref<HTMLHeadingElement | null>(null)

const target = computed(() =>
  props.versionNumber != null
    ? null
    : t(props.kind === 'page' ? 'preview.share.targetPage' : 'preview.share.targetItem'),
)
const atLimit = computed(() => links.value.length >= MAX_ACTIVE_LINKS)

function expiresAt(): string | null {
  if (expiry.value === 'custom') return custom.value
  return new Date(Date.now() + spans[expiry.value]).toISOString()
}

function messageFor(caught: unknown): string {
  const code = caught instanceof ApiError ? caught.errorCode : null
  const key = code ? `preview.share.errors.${code.replace(/^preview\./, '')}` : ''
  return code && te(key) ? t(key) : t('preview.share.errors.generic')
}

async function load(): Promise<void> {
  links.value = await previewLinksApi.list(props.siteId, props.kind, props.documentId)
}

onMounted(async () => {
  await load().catch(() => undefined)
  await nextTick()
  heading.value?.focus()
})

async function create(): Promise<void> {
  const expiresAtUtc = expiresAt()
  if (!expiresAtUtc) {
    error.value = t('preview.share.errors.invalid_expiry')
    return
  }

  busy.value = true
  error.value = null
  copied.value = false
  try {
    const result = await previewLinksApi.create(props.siteId, {
      documentKind: props.kind,
      documentId: props.documentId,
      versionNumber: props.versionNumber ?? null,
      sampleItemId: props.sampleItemId ?? null,
      label: label.value.trim() || null,
      expiresAtUtc,
    })
    created.value = { url: result.url, expiresAtUtc: result.link.expiresAtUtc }
    label.value = ''
    await load()
  } catch (caught) {
    error.value = messageFor(caught)
  } finally {
    busy.value = false
  }
}

async function copy(): Promise<void> {
  if (!created.value) return
  await navigator.clipboard?.writeText(created.value.url)
  copied.value = true
}

async function revoke(id: string | 'all'): Promise<void> {
  busy.value = true
  error.value = null
  try {
    if (id === 'all') {
      await previewLinksApi.revokeAll(props.siteId, { documentKind: props.kind, documentId: props.documentId })
    } else {
      await previewLinksApi.revoke(props.siteId, id)
    }
    confirming.value = null
    await load()
  } catch (caught) {
    error.value = messageFor(caught)
  } finally {
    busy.value = false
  }
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('close')
  }
}

const when = (iso: string | null) => (iso ? d(new Date(iso), 'short') : t('preview.share.never'))
</script>

<template>
  <Teleport to="body">
    <div
      class="dialog-backdrop"
      @click.self="emit('close')"
    >
      <div
        class="card dialog share-preview"
        role="dialog"
        aria-modal="true"
        aria-labelledby="share-preview-title"
        @keydown="onKeydown"
      >
        <h2
          id="share-preview-title"
          ref="heading"
          tabindex="-1"
        >
          {{ t('preview.share.title') }}
        </h2>
        <p>
          {{ versionNumber != null ? t('preview.share.introVersion', { number: versionNumber }) : t('preview.share.intro', { target }) }}
        </p>

        <form
          class="share-preview__create"
          @submit.prevent="create"
        >
          <div class="field">
            <label for="share-preview-label">{{ t('preview.share.label') }}</label>
            <input
              id="share-preview-label"
              v-model="label"
              type="text"
              maxlength="80"
              autocomplete="off"
              aria-describedby="share-preview-label-help"
            >
            <p
              id="share-preview-label-help"
              class="help"
            >
              {{ t('preview.share.labelHelp') }}
            </p>
          </div>

          <fieldset class="field">
            <legend>{{ t('preview.share.expires') }}</legend>
            <label
              v-for="option in (['hour', 'day', 'week', 'month', 'custom'] as const)"
              :key="option"
              class="choice"
            >
              <input
                v-model="expiry"
                type="radio"
                name="share-preview-expiry"
                :value="option"
              >
              {{ t(`preview.share.expiry.${option}`) }}
            </label>
            <ZonedDateTimeInput
              v-if="expiry === 'custom'"
              id="share-preview-custom"
              v-model="custom"
            />
          </fieldset>

          <p
            v-if="atLimit"
            class="help"
          >
            {{ t('preview.share.limit', { kind: t(kind === 'page' ? 'preview.links.kindPage' : 'preview.links.kindItem').toLowerCase() }) }}
          </p>
          <button
            type="submit"
            class="primary"
            :disabled="busy || atLimit"
          >
            {{ t('preview.share.create') }}
          </button>
        </form>

        <div
          v-if="created"
          class="share-preview__created"
        >
          <div class="field">
            <label for="share-preview-url">{{ t('preview.share.linkLabel') }}</label>
            <input
              id="share-preview-url"
              :value="created.url"
              type="text"
              readonly
              aria-describedby="share-preview-once"
              @focus="($event.target as HTMLInputElement).select()"
            >
          </div>
          <p
            id="share-preview-once"
            class="help"
          >
            {{ t('preview.share.created', { target: target ?? t('preview.share.version', { number: versionNumber }), expiry: when(created.expiresAtUtc) }) }}
          </p>
          <button
            type="button"
            @click="copy"
          >
            {{ t('preview.share.copy') }}
          </button>
        </div>
        <p
          class="visually-hidden"
          role="status"
          aria-live="polite"
        >
          <span v-if="copied">{{ t('preview.share.copied') }}</span>
        </p>

        <p
          v-if="error"
          class="field-error"
          role="alert"
        >
          {{ error }}
        </p>

        <h3>{{ t('preview.share.active') }}</h3>
        <p v-if="links.length === 0">
          {{ t('preview.share.none') }}
        </p>
        <table
          v-else
          class="share-preview__links"
        >
          <thead>
            <tr>
              <th scope="col">
                {{ t('preview.share.columns.label') }}
              </th>
              <th scope="col">
                {{ t('preview.share.columns.target') }}
              </th>
              <th scope="col">
                {{ t('preview.share.columns.createdBy') }}
              </th>
              <th scope="col">
                {{ t('preview.share.columns.expires') }}
              </th>
              <th scope="col">
                {{ t('preview.share.columns.lastOpened') }}
              </th>
              <th scope="col">
                <span class="visually-hidden">{{ t('preview.share.revoke') }}</span>
              </th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="link in links"
              :key="link.id"
            >
              <td>{{ link.label ?? '—' }}</td>
              <td>{{ link.versionNumber != null ? t('preview.share.version', { number: link.versionNumber }) : t('preview.share.draft') }}</td>
              <td>{{ link.createdBy.displayName }}</td>
              <td>{{ when(link.expiresAtUtc) }}</td>
              <td>{{ when(link.lastOpenedAtUtc) }}</td>
              <td>
                <template v-if="confirming === link.id">
                  <span>{{ t('preview.share.revokeConfirm') }}</span>
                  <button
                    type="button"
                    class="danger"
                    :disabled="busy"
                    @click="revoke(link.id)"
                  >
                    {{ t('preview.share.revoke') }}
                  </button>
                  <button
                    type="button"
                    @click="confirming = null"
                  >
                    {{ t('trash.restore.cancel') }}
                  </button>
                </template>
                <button
                  v-else
                  type="button"
                  @click="confirming = link.id"
                >
                  {{ t('preview.share.revoke') }}
                </button>
              </td>
            </tr>
          </tbody>
        </table>

        <div class="actions">
          <template v-if="links.length > 1">
            <template v-if="confirming === 'all'">
              <span>{{ t('preview.share.revokeAllConfirm', { count: links.length }) }}</span>
              <button
                type="button"
                class="danger"
                :disabled="busy"
                @click="revoke('all')"
              >
                {{ t('preview.share.revokeAll') }}
              </button>
              <button
                type="button"
                @click="confirming = null"
              >
                {{ t('trash.restore.cancel') }}
              </button>
            </template>
            <button
              v-else
              type="button"
              @click="confirming = 'all'"
            >
              {{ t('preview.share.revokeAll') }}
            </button>
          </template>
          <button
            type="button"
            @click="emit('close')"
          >
            {{ t('preview.share.close') }}
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>
