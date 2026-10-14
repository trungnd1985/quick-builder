<script setup lang="ts">
// An item's history (015 US7, FR-022, FR-031): its kept versions, newest first, with the live
// one marked, and what happened to it. A version can be published again (rolled back), which is
// refused with the reasons when it no longer meets today's rules, or copied into the draft.
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { itemsApi, refusalOf, type HistoryEvent, type Item, type ItemHistory, type Version } from '../../../api/items'
import ConfirmDialog from '../../sites/ConfirmDialog.vue'
import RedirectOfferCheckbox from '../../../components/RedirectOfferCheckbox.vue'
import { errorCodeOf, itemErrorMessage, problemMessage } from './itemErrors'

const props = defineProps<{ siteId: string; collectionId: string; item: Item }>()
const emit = defineEmits<{ changed: [item: Item]; close: [] }>()
const { t, te, d } = useI18n()

const history = ref<ItemHistory | null>(null)
const error = ref<string | null>(null)
const notice = ref<string | null>(null)
const problems = ref<string[]>([])
const confirming = ref<{ action: 'rollback' | 'copy'; version: Version } | null>(null)
const busy = ref(false)

// 017 FR-029: rolling back to a version with another slug moves a live item's address.
const redirectOldAddress = ref(true)
const hasAddress = computed(() => !!props.item.address?.path && !props.item.address.reason)
const movesAddress = computed(() =>
  confirming.value?.action === 'rollback' && hasAddress.value
  && !!props.item.publishedSlug && confirming.value.version.slug !== props.item.publishedSlug)

async function load(): Promise<void> {
  error.value = null
  try {
    history.value = await itemsApi.history(props.siteId, props.collectionId, props.item.id)
  } catch (caught) {
    error.value = itemErrorMessage(t, te, caught)
  }
}

onMounted(load)
watch(() => props.item.revision, load)

const kindLabel = (version: Version): string =>
  version.kind === 'rollback' ? t('cms.items.history.kinds.rollback', { from: version.restoredFrom }) : t(`cms.items.history.kinds.${version.kind}`)

function eventLabel(event: HistoryEvent): string {
  const action = event.scheduleAction ? t(`cms.items.history.actions.${event.scheduleAction}`) : ''
  const reason = event.failureCode && te(`cms.items.errors.${event.failureCode.replace('cms.', '')}`)
    ? t(`cms.items.errors.${event.failureCode.replace('cms.', '')}`)
    : (event.failureCode ?? '')
  return t(`cms.items.history.happened.${event.kind}`, {
    number: event.versionNumber,
    action,
    when: event.dueAtUtc ? d(new Date(event.dueAtUtc), 'short') : '',
    reason,
  })
}

async function confirm(): Promise<void> {
  const pending = confirming.value
  if (!pending) return
  busy.value = true
  error.value = null
  notice.value = null
  problems.value = []
  try {
    const hasSchedule = props.item.schedules.some((s) => s.action === 'publish')
    const next = pending.action === 'rollback'
      ? await itemsApi.rollback(
        props.siteId, props.collectionId, props.item.id, pending.version.number, props.item.revision, hasSchedule, movesAddress.value && redirectOldAddress.value)
      : await itemsApi.copyToDraft(props.siteId, props.collectionId, props.item.id, pending.version.number, props.item.revision)
    notice.value = pending.action === 'rollback'
      ? t('cms.items.history.rolledBack', { number: pending.version.number })
      : t('cms.items.history.copied', { number: pending.version.number })
    emit('changed', next)
  } catch (caught) {
    error.value = itemErrorMessage(t, te, caught)
    if (errorCodeOf(caught) === 'cms.version_not_publishable') {
      problems.value = (refusalOf(caught).report?.problems ?? []).map((p) => problemMessage(t, te, p))
    }
  } finally {
    busy.value = false
    confirming.value = null
  }
}
</script>

<template>
  <aside
    class="card item-history"
    aria-labelledby="item-history-title"
  >
    <header class="page-header">
      <h2 id="item-history-title">
        {{ t('cms.items.history.title') }}
      </h2>
      <button
        type="button"
        @click="emit('close')"
      >
        {{ t('cms.items.history.close') }}
      </button>
    </header>

    <p
      v-if="notice"
      class="notice"
      role="status"
    >
      {{ notice }}
    </p>
    <div
      v-if="error"
      class="field-error"
      role="alert"
    >
      <p>{{ error }}</p>
      <ul v-if="problems.length > 0">
        <li
          v-for="problem in problems"
          :key="problem"
        >
          {{ problem }}
        </li>
      </ul>
    </div>

    <template v-if="history">
      <h3>{{ t('cms.items.history.versions') }}</h3>
      <p
        v-if="history.versions.length === 0"
        class="help"
      >
        {{ t('cms.items.history.empty') }}
      </p>
      <ol
        v-else
        class="item-history__versions"
      >
        <li
          v-for="version in history.versions"
          :key="version.number"
        >
          <strong>{{ t('cms.items.history.version', { number: version.number }) }}</strong>
          <span
            v-if="version.live"
            class="tag tag--published"
          >{{ t('cms.items.history.live') }}</span>
          · {{ kindLabel(version) }}
          · {{ t('cms.items.history.byWhen', { name: version.publishedBy.name, when: d(new Date(version.publishedAtUtc), 'short') }) }}
          <span class="item-history__actions">
            <button
              v-if="!version.live"
              type="button"
              :disabled="busy"
              :aria-label="`${t('cms.items.history.rollback')}: ${t('cms.items.history.version', { number: version.number })}`"
              @click="confirming = { action: 'rollback', version }"
            >
              {{ t('cms.items.history.rollback') }}
            </button>
            <button
              type="button"
              :disabled="busy"
              :aria-label="`${t('cms.items.history.copyToDraft')}: ${t('cms.items.history.version', { number: version.number })}`"
              @click="confirming = { action: 'copy', version }"
            >
              {{ t('cms.items.history.copyToDraft') }}
            </button>
          </span>
        </li>
      </ol>

      <h3>{{ t('cms.items.history.events') }}</h3>
      <ol class="item-history__events">
        <li
          v-for="(event, index) in history.events"
          :key="index"
        >
          {{ eventLabel(event) }} ·
          {{ t('cms.items.history.byWhen', { name: event.by.name, when: d(new Date(event.atUtc), 'short') }) }}
        </li>
      </ol>
    </template>

    <ConfirmDialog
      v-if="confirming"
      dialog-id="item-history-confirm"
      :title="confirming.action === 'rollback' ? t('cms.items.history.rollback') : t('cms.items.history.copyToDraft')"
      :body="confirming.action === 'rollback'
        ? t('cms.items.history.confirmRollback', { number: confirming.version.number })
        : t('cms.items.history.confirmCopy', { number: confirming.version.number })"
      :confirm-label="t('cms.items.publish.confirm')"
      :cancel-label="t('cms.items.publish.cancel')"
      :busy-label="t('cms.items.publish.publishing')"
      :busy="busy"
      @confirm="confirm"
      @cancel="confirming = null"
    >
      <RedirectOfferCheckbox
        v-if="movesAddress"
        id="item-history-redirect"
        v-model="redirectOldAddress"
        :count="1"
      />
    </ConfirmDialog>
  </aside>
</template>
