<script setup lang="ts">
// Publishing the site's design (012 FR-010, clarification Q1, research PR-17). Saving tokens
// changes nothing visitors see; this panel publishes the saved set, lists the retained
// versions and rolls back to one after confirmation. The server checks every action;
// `canPublish` only decides what is offered.
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { publishingApi, type DesignPublication, type DesignVersionSummary } from '../../api/publishing'
import ConfirmDialog from '../sites/ConfirmDialog.vue'
import { isOutOfDate, publishingErrorMessage } from './errorMessages'

const props = defineProps<{
  siteId: string
  publication: DesignPublication
  canPublish: boolean
  /** Saves pending edits first and returns the token set version to publish, or null. */
  beforePublish: () => Promise<number | null>
}>()

const emit = defineEmits<{ changed: [publication: DesignPublication]; outOfDate: [] }>()

const { t, te, d } = useI18n()

const current = ref<DesignPublication>(props.publication)
const versions = ref<DesignVersionSummary[]>([])
const versionsFailed = ref(false)
const busy = ref(false)
const status = ref('')
const error = ref<string | null>(null)
const pendingRollback = ref<DesignVersionSummary | null>(null)

watch(() => props.publication, (value) => { current.value = value })

const liveNumber = computed(() => current.value.liveNumber)

async function loadVersions(): Promise<void> {
  versionsFailed.value = false
  try {
    const result = await publishingApi.designVersions(props.siteId)
    versions.value = result.versions ?? []
    current.value = result.publication
  } catch {
    versionsFailed.value = true
  }
}

onMounted(loadVersions)

async function run(action: () => Promise<DesignPublication | null>, message: (p: DesignPublication) => string): Promise<void> {
  busy.value = true
  error.value = null
  status.value = ''
  try {
    const publication = await action()
    if (!publication) return
    current.value = publication
    status.value = message(publication)
    emit('changed', publication)
    await loadVersions()
  } catch (caught) {
    if (isOutOfDate(caught)) emit('outOfDate')
    error.value = publishingErrorMessage(t, te, caught)
  } finally {
    busy.value = false
  }
}

async function publish(): Promise<void> {
  await run(async () => {
    const version = await props.beforePublish()
    return version === null ? null : publishingApi.publishDesign(props.siteId, version)
  }, (p) => t('publishing.design.published', { number: p.liveNumber }))
}

async function confirmRollback(): Promise<void> {
  const target = pendingRollback.value
  if (!target) return
  await run(
    () => publishingApi.rollbackDesign(props.siteId, target.number),
    () => t('publishing.design.rolledBack', { number: target.number }),
  )
  pendingRollback.value = null
}
</script>

<template>
  <section
    class="design-publishing design-group"
    aria-labelledby="design-publishing-heading"
  >
    <h2 id="design-publishing-heading">
      {{ t('publishing.menu.label') }}
    </h2>

    <p v-if="liveNumber === 0">
      {{ t('publishing.design.defaults') }}
    </p>
    <p v-else>
      {{ t('publishing.design.live', { number: liveNumber }) }}
    </p>
    <p
      v-if="current.hasUnpublishedChanges"
      class="help"
    >
      {{ t('publishing.design.changes') }}
    </p>

    <div>
      <button
        v-if="canPublish"
        type="button"
        class="primary"
        :disabled="busy"
        @click="publish"
      >
        {{ busy ? t('publishing.menu.publishing') : t('publishing.design.publish') }}
      </button>
    </div>

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

    <template v-if="versions.length > 0">
      <h3>{{ t('publishing.design.versions') }}</h3>
      <table class="sites-table">
        <caption class="visually-hidden">
          {{ t('publishing.design.versions') }}
        </caption>
        <thead>
          <tr>
            <th scope="col">
              {{ t('publishing.history.columns.version') }}
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
            v-for="version in versions"
            :key="version.number"
          >
            <th scope="row">
              {{ t('publishing.history.version', { number: version.number }) }}
              <span
                v-if="version.number === liveNumber"
                class="badge"
              >{{ t('publishing.history.live') }}</span>
              <span v-if="version.restoredFromNumber !== null">
                — {{ t('publishing.actions.rollback', { number: version.restoredFromNumber }) }}
              </span>
            </th>
            <td>{{ version.publishedBy.displayName ?? '' }}</td>
            <td>{{ d(new Date(version.publishedAtUtc), 'short') }}</td>
            <td>
              <button
                v-if="canPublish && version.number !== liveNumber"
                type="button"
                :disabled="busy"
                :aria-label="`${t('publishing.design.rollback')}: ${t('publishing.history.version', { number: version.number })}`"
                @click="pendingRollback = version"
              >
                {{ t('publishing.design.rollback') }}
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </template>

    <ConfirmDialog
      v-if="pendingRollback"
      dialog-id="design-rollback"
      :title="t('publishing.design.rollback')"
      :body="t('publishing.confirm.rollback', { number: pendingRollback.number })"
      :confirm-label="t('publishing.confirm.confirm')"
      :cancel-label="t('publishing.confirm.cancel')"
      :busy-label="t('publishing.menu.publishing')"
      :busy="busy"
      @confirm="confirmRollback"
      @cancel="pendingRollback = null"
    />
  </section>
</template>
