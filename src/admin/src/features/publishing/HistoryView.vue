<script setup lang="ts">
// A page's or component's publishing history (012 FR-013 to FR-016, research PR-15): what
// happened, newest first, and for each retained version View, Roll back and Copy to draft.
// Roll back publishes the version again at once; Copy to draft only replaces the draft. Both
// ask first. The server checks every action; the permissions only decide what is offered.
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { menusApi } from '../../api/menus'
import { pagesApi } from '../../api/pages'
import { publishingApi, type LogEntry, type SubjectHistory, type SubjectKind } from '../../api/publishing'
import ConfirmDialog from '../sites/ConfirmDialog.vue'
import PublicationBadge from './PublicationBadge.vue'
import { publishingErrorMessage } from './errorMessages'
import { currentDraftVersion, listRoute, versionRoute as routeToVersion } from './subjects'
import SharePreviewDialog from '../preview/SharePreviewDialog.vue'
import { pagePreviewUrl } from '../preview/previewUrls'

const props = defineProps<{ siteId: string; kind: SubjectKind; subjectId: string }>()

const { t, te, d } = useI18n()

type Pending = { action: 'rollback' | 'copy'; number: number }

const history = ref<SubjectHistory | null>(null)
const title = ref('')
const canPublish = ref(false)
const canEditDraft = ref(false)
const loading = ref(true)
const loadError = ref<string | null>(null)
const busy = ref(false)
const error = ref<string | null>(null)
const status = ref('')
const pending = ref<Pending | null>(null)

const liveNumber = computed(() => history.value?.publication.liveNumber ?? null)
const retained = computed(() => new Set(history.value?.retainedVersions ?? []))
// 022 FR-028: a kept version of a page can be previewed and shared.
const sharingVersion = ref<number | null>(null)
const backTo = computed(() => listRoute(props.kind, props.siteId))

/** Entries that made a version live; those versions can be viewed and restored while kept. */
const MAKES_VERSION = new Set(['publish', 'scheduled_publish', 'rollback'])

function versionOf(entry: LogEntry): number | null {
  return MAKES_VERSION.has(entry.action) && entry.versionNumber !== null && retained.value.has(entry.versionNumber)
    ? entry.versionNumber
    : null
}

function actionLabel(entry: LogEntry): string {
  return t(`publishing.actions.${entry.action}`, {
    number: entry.restoredFromNumber ?? entry.versionNumber ?? 0,
    // 022 FR-016: when a preview link created here stops working.
    expiry: entry.expiresAtUtc ? d(new Date(entry.expiresAtUtc), 'short') : '',
  })
}

function who(entry: LogEntry): string {
  const person = entry.actor ?? entry.scheduleOwner
  return person?.displayName ?? ''
}

function versionRoute(number: number) {
  return routeToVersion(props.kind, props.siteId, props.subjectId, number)
}

async function load(): Promise<void> {
  loading.value = true
  loadError.value = null
  try {
    // Together, so the list never shows before what may be done with it is known.
    const [loaded] = await Promise.all([publishingApi.history(props.siteId, props.kind, props.subjectId), details()])
    history.value = loaded
  } catch (caught) {
    loadError.value = publishingErrorMessage(t, te, caught)
  } finally {
    loading.value = false
  }
}

/** The subject's name and what this member may do with its versions. */
async function details(): Promise<void> {
  if (props.kind === 'page') {
    const page = await pagesApi.get(props.siteId, props.subjectId)
    title.value = page.title
    canPublish.value = page.permissions?.canPublish === true
    canEditDraft.value = page.permissions?.canEditPageDrafts === true
  } else if (props.kind === 'menu') {
    const menu = await menusApi.get(props.siteId, props.subjectId)
    title.value = menu.name
    canPublish.value = menu.permissions.canPublish
    canEditDraft.value = menu.permissions.canManage
  } else {
    const [component, list] = await Promise.all([
      pagesApi.getComponent(props.siteId, props.subjectId),
      pagesApi.components(props.siteId).catch(() => null),
    ])
    title.value = component.name
    canPublish.value = list?.permissions.canPublish === true
    canEditDraft.value = list?.permissions.canManageComponents === true
  }
}

onMounted(load)
watch(() => [props.siteId, props.kind, props.subjectId], load)

async function confirm(): Promise<void> {
  const action = pending.value
  if (!action) return
  busy.value = true
  error.value = null
  status.value = ''
  try {
    if (action.action === 'rollback') {
      const publication = await publishingApi.rollback(props.siteId, props.kind, props.subjectId, action.number)
      status.value = t('publishing.history.rolledBack', { number: action.number, newNumber: publication.liveNumber ?? 0 })
    } else {
      const draftVersion = await currentDraftVersion(props.kind, props.siteId, props.subjectId)
      await publishingApi.copyToDraft(props.siteId, props.kind, props.subjectId, action.number, draftVersion)
      status.value = t('publishing.history.copied', { number: action.number })
    }
    pending.value = null
    await load()
  } catch (caught) {
    error.value = publishingErrorMessage(t, te, caught)
    pending.value = null
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section aria-labelledby="history-heading">
    <p>
      <RouterLink :to="backTo">
        {{ t('publishing.history.back') }}
      </RouterLink>
    </p>

    <header class="page-header">
      <div>
        <h1 id="history-heading">
          {{ t('publishing.history.title', { title }) }}
        </h1>
        <PublicationBadge
          v-if="history"
          :publication="history.publication"
        />
      </div>
    </header>

    <p
      class="visually-hidden"
      role="status"
      aria-live="polite"
    >
      {{ status }}
    </p>
    <p
      v-if="error"
      class="error"
      role="alert"
    >
      {{ error }}
    </p>

    <p
      v-if="loading && !history"
      role="status"
    >
      …
    </p>
    <p
      v-else-if="loadError"
      class="error"
      role="alert"
    >
      {{ loadError }}
    </p>

    <template v-else-if="history">
      <p
        v-if="history.entries.length === 0"
        class="card"
      >
        {{ t('publishing.history.empty') }}
      </p>

      <table
        v-else
        class="sites-table"
      >
        <caption class="visually-hidden">
          {{ t('publishing.history.title', { title }) }}
        </caption>
        <thead>
          <tr>
            <th scope="col">
              {{ t('publishing.history.columns.version') }}
            </th>
            <th scope="col">
              {{ t('publishing.history.columns.action') }}
            </th>
            <th scope="col">
              {{ t('publishing.history.columns.who') }}
            </th>
            <th scope="col">
              {{ t('publishing.history.columns.when') }}
            </th>
            <th scope="col">
              {{ t('publishing.history.columns.actions') }}
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="entry in history.entries"
            :key="entry.id"
          >
            <th scope="row">
              <template v-if="entry.versionNumber !== null">
                {{ t('publishing.history.version', { number: entry.versionNumber }) }}
                <span
                  v-if="versionOf(entry) !== null && entry.versionNumber === liveNumber"
                  class="badge"
                >{{ t('publishing.history.live') }}</span>
              </template>
            </th>
            <td>{{ actionLabel(entry) }}</td>
            <td>{{ who(entry) }}</td>
            <td>{{ d(new Date(entry.occurredAtUtc), 'short') }}</td>
            <td>
              <div
                v-if="versionOf(entry) !== null"
                class="page-toolbar"
              >
                <RouterLink
                  v-if="versionRoute(entry.versionNumber!)"
                  class="button-link"
                  :to="versionRoute(entry.versionNumber!)!"
                  :aria-label="`${t('publishing.history.view')}: ${t('publishing.history.version', { number: entry.versionNumber })}`"
                >
                  {{ t('publishing.history.view') }}
                </RouterLink>
                <a
                  v-if="kind === 'page'"
                  class="button-link"
                  :href="pagePreviewUrl(siteId, subjectId, { version: entry.versionNumber! })"
                  target="_blank"
                  rel="noopener"
                  :aria-label="`${t('preview.open')}: ${t('publishing.history.version', { number: entry.versionNumber })}`"
                >{{ t('preview.open') }}</a>
                <button
                  v-if="kind === 'page' && canEditDraft"
                  type="button"
                  :aria-label="`${t('preview.share.shareVersion')}: ${t('publishing.history.version', { number: entry.versionNumber })}`"
                  @click="sharingVersion = entry.versionNumber"
                >
                  {{ t('preview.share.shareVersion') }}
                </button>
                <button
                  v-if="canPublish && entry.versionNumber !== liveNumber"
                  type="button"
                  :disabled="busy"
                  :aria-label="`${t('publishing.history.rollback')}: ${t('publishing.history.version', { number: entry.versionNumber })}`"
                  @click="pending = { action: 'rollback', number: entry.versionNumber! }"
                >
                  {{ t('publishing.history.rollback') }}
                </button>
                <button
                  v-if="canEditDraft"
                  type="button"
                  :disabled="busy"
                  :aria-label="`${t('publishing.history.copyToDraft')}: ${t('publishing.history.version', { number: entry.versionNumber })}`"
                  @click="pending = { action: 'copy', number: entry.versionNumber! }"
                >
                  {{ t('publishing.history.copyToDraft') }}
                </button>
              </div>
              <span
                v-else-if="entry.versionNumber !== null && MAKES_VERSION.has(entry.action)"
                class="help"
              >{{ t('publishing.history.notKept') }}</span>
            </td>
          </tr>
        </tbody>
      </table>
    </template>

    <SharePreviewDialog
      v-if="sharingVersion !== null && kind === 'page'"
      :site-id="siteId"
      kind="page"
      :document-id="subjectId"
      :version-number="sharingVersion"
      @close="sharingVersion = null"
    />

    <ConfirmDialog
      v-if="pending"
      dialog-id="history-confirm"
      :title="pending.action === 'rollback' ? t('publishing.history.rollback') : t('publishing.history.copyToDraft')"
      :body="pending.action === 'rollback'
        ? t('publishing.confirm.rollback', { number: pending.number })
        : t('publishing.confirm.copyToDraft', { number: pending.number })"
      :confirm-label="t('publishing.confirm.confirm')"
      :cancel-label="t('publishing.confirm.cancel')"
      :busy-label="t('publishing.menu.publishing')"
      :busy="busy"
      @confirm="confirm"
      @cancel="pending = null"
    />
  </section>
</template>
