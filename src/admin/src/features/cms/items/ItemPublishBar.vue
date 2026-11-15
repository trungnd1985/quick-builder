<script setup lang="ts">
// Publishing an item (015 US2, US5, US6; FR-019 to FR-021, FR-027): publish, unpublish,
// schedule, duplicate and delete. Each action that changes what visitors see is confirmed first,
// saying what it does: the address that changes, the schedule it cancels, the published items
// that refer to it. A publish refused because someone changed the item just before is retried
// once with the new revision, unless their change altered the content (analysis I3).
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { itemsApi, refusalOf, type Item } from '../../../api/items'
import ConfirmDialog from '../../sites/ConfirmDialog.vue'
import MenuReferencesNote from '../../menus/MenuReferencesNote.vue'
import RedirectOfferCheckbox from '../../../components/RedirectOfferCheckbox.vue'
import RedirectToPagePicker, { type PickablePage } from '../../../components/RedirectToPagePicker.vue'
import { pagesApi } from '../../../api/pages'
import DeleteItemDialog from './DeleteItemDialog.vue'
import type { TrashReceipt } from '../../../api/trash'
import ItemScheduleDialog from './ItemScheduleDialog.vue'
import HolderWarning from '../../../editing/HolderWarning.vue'
import { errorCodeOf, itemErrorMessage } from './itemErrors'

/** `editingBy`: someone else holds the editing lock (023 research CE-17). */
const props = defineProps<{ siteId: string; collectionId: string; item: Item; dirty: boolean; editingBy?: string | null }>()
const emit = defineEmits<{ changed: [item: Item]; deleted: [receipt?: TrashReceipt]; duplicated: [copy: Item] }>()
const { t, te, d } = useI18n()

type Pending = 'publish' | 'unpublish' | null

const confirming = ref<Pending>(null)
const confirmBody = ref('')
const busy = ref(false)
const scheduling = ref(false)
const deleting = ref(false)
const notice = ref<{ kind: 'info' | 'error'; text: string } | null>(null)

// 017 FR-029 to FR-032: the item's live address may follow a new slug, or go to a page when it
// stops being live. Offered only when the item has an address visitors use.
const hasAddress = computed(() => !!props.item.address?.path && !props.item.address.reason)
const slugChanges = computed(() => !!props.item.publishedSlug && props.item.publishedSlug !== props.item.slug)
const redirectOldAddress = ref(true)
const redirectToPageId = ref<string | null>(null)
const sitePages = ref<PickablePage[]>([])

const pendingPublish = computed(() => props.item.schedules.find((s) => s.action === 'publish') ?? null)
const sameContent = (a: Item, b: Item): boolean =>
  a.slug === b.slug && JSON.stringify(a.draft.values) === JSON.stringify(b.draft.values)

function askPublish(): void {
  const lines = [t('cms.items.publish.confirmPublish')]
  if (props.item.publishedSlug && props.item.publishedSlug !== props.item.slug) {
    lines.push(t('cms.items.publish.slugChange', { from: props.item.publishedSlug, to: props.item.slug }))
  }
  if (pendingPublish.value) {
    lines.push(t('cms.items.publish.cancelsSchedule', { when: d(new Date(pendingPublish.value.dueAtUtc), 'short') }))
  }
  confirmBody.value = lines.join(' ')
  redirectOldAddress.value = true
  confirming.value = 'publish'
}

async function askUnpublish(): Promise<void> {
  const lines = [t('cms.items.publish.confirmUnpublish')]
  try {
    const referrers = await itemsApi.referrers(props.siteId, props.collectionId, props.item.id, true)
    if (referrers.total > 0) lines.push(t('cms.items.publish.referrersPublished', { count: referrers.total }, referrers.total))
  } catch {
    // The count is a courtesy; the unpublish itself is still confirmed.
  }
  if (pendingPublish.value) {
    lines.push(t('cms.items.publish.cancelsSchedule', { when: d(new Date(pendingPublish.value.dueAtUtc), 'short') }))
  }
  confirmBody.value = lines.join(' ')
  redirectToPageId.value = null
  if (hasAddress.value && sitePages.value.length === 0) {
    try {
      sitePages.value = (await pagesApi.tree(props.siteId)).pages ?? []
    } catch {
      sitePages.value = []
    }
  }
  confirming.value = 'unpublish'
}

async function confirm(): Promise<void> {
  const action = confirming.value
  if (!action) return
  busy.value = true
  notice.value = null
  try {
    const next = action === 'publish' ? await publish(props.item) : await unpublish(props.item)
    if (next) emit('changed', next)
  } finally {
    busy.value = false
    confirming.value = null
  }
}

async function publish(item: Item, retry = true): Promise<Item | null> {
  try {
    const published = await itemsApi.publish(
      props.siteId, props.collectionId, item.id, item.revision, pendingPublish.value !== null, hasAddress.value && slugChanges.value && redirectOldAddress.value)
    const missing = published.unpublishedReferences.map((r) => r.name ?? r.slug)
    notice.value = {
      kind: 'info',
      text: [
        t('cms.items.publish.published', { number: published.liveVersion }),
        missing.length > 0 ? t('cms.items.publish.unpublishedReferences', { names: missing.join(', ') }) : '',
        published.redirectRequested ? t('redirects.offer.itemRequested') : '',
        retry ? '' : t('cms.items.publish.retried'),
      ].filter(Boolean).join(' '),
    }
    return published
  } catch (caught) {
    if (retry && errorCodeOf(caught) === 'cms.item_conflict') {
      const current = await itemsApi.get(props.siteId, props.collectionId, item.id)
      if (sameContent(current, item)) return publish(current, false)
      notice.value = { kind: 'error', text: t('cms.items.publish.retriedChanged') }
      return current
    }

    notice.value = { kind: 'error', text: itemErrorMessage(t, te, caught) }
    if (errorCodeOf(caught) === 'cms.item_needs_attention' && refusalOf(caught).report) {
      requestAnimationFrame(() => document.getElementById('item-problems')?.focus())
    }
    return null
  }
}

async function unpublish(item: Item): Promise<Item | null> {
  try {
    const result = await itemsApi.unpublish(
      props.siteId, props.collectionId, item.id, item.revision, pendingPublish.value !== null, hasAddress.value ? redirectToPageId.value : null)
    notice.value = {
      kind: 'info',
      text: [t('cms.items.publish.unpublished'), result.redirectRequested ? t('redirects.offer.itemRequested') : ''].filter(Boolean).join(' '),
    }
    return result
  } catch (caught) {
    notice.value = { kind: 'error', text: itemErrorMessage(t, te, caught) }
    return null
  }
}

async function duplicate(): Promise<void> {
  busy.value = true
  notice.value = null
  try {
    emit('duplicated', await itemsApi.duplicate(props.siteId, props.collectionId, props.item.id))
  } catch (caught) {
    notice.value = { kind: 'error', text: itemErrorMessage(t, te, caught) }
  } finally {
    busy.value = false
  }
}

function scheduled(next: Item): void {
  scheduling.value = false
  emit('changed', next)
}
</script>

<template>
  <div
    class="item-publish-bar"
    role="group"
    :aria-label="t('cms.items.publish.label')"
  >
    <HolderWarning
      v-if="editingBy"
      :name="editingBy"
      action="publish"
    />
    <button
      type="button"
      class="primary"
      :disabled="busy || dirty"
      :aria-describedby="dirty ? 'item-publish-dirty' : undefined"
      @click="askPublish"
    >
      {{ busy && confirming === 'publish' ? t('cms.items.publish.publishing') : t('cms.items.publish.publish') }}
    </button>
    <button
      v-if="item.status !== 'draft'"
      type="button"
      :disabled="busy"
      @click="askUnpublish"
    >
      {{ t('cms.items.publish.unpublish') }}
    </button>
    <button
      type="button"
      :disabled="busy || dirty"
      @click="scheduling = true"
    >
      {{ t('cms.items.publish.schedule') }}
    </button>
    <button
      type="button"
      :disabled="busy || dirty"
      @click="duplicate"
    >
      {{ t('cms.items.publish.duplicate') }}
    </button>
    <button
      type="button"
      class="danger"
      :disabled="busy"
      @click="deleting = true"
    >
      {{ t('cms.items.publish.delete') }}
    </button>
    <span
      v-if="dirty"
      id="item-publish-dirty"
      class="help"
    >{{ t('cms.items.form.unsaved') }}</span>

    <p
      v-if="notice"
      :class="notice.kind === 'info' ? 'notice' : 'field-error'"
      :role="notice.kind === 'info' ? 'status' : 'alert'"
    >
      {{ notice.text }}
    </p>

    <ConfirmDialog
      v-if="confirming"
      dialog-id="item-publish-confirm"
      :title="confirming === 'publish' ? t('cms.items.publish.publish') : t('cms.items.publish.unpublish')"
      :body="confirmBody"
      :confirm-label="t('cms.items.publish.confirm')"
      :cancel-label="t('cms.items.publish.cancel')"
      :busy-label="t('cms.items.publish.publishing')"
      :busy="busy"
      :danger="confirming === 'unpublish'"
      @confirm="confirm"
      @cancel="confirming = null"
    >
      <RedirectOfferCheckbox
        v-if="confirming === 'publish' && hasAddress && slugChanges"
        id="item-publish-redirect"
        v-model="redirectOldAddress"
        :count="1"
      />
      <!-- 024 FR-026: the menus that link here lose that entry while it is unpublished. -->
      <MenuReferencesNote
        v-if="confirming === 'unpublish'"
        :site-id="siteId"
        :target="{ itemId: item.id }"
        action="unpublish"
      />
      <RedirectToPagePicker
        v-if="confirming === 'unpublish' && hasAddress"
        id="item-unpublish-redirect"
        v-model="redirectToPageId"
        :pages="sitePages"
      />
    </ConfirmDialog>

    <ItemScheduleDialog
      v-if="scheduling"
      :site-id="siteId"
      :collection-id="collectionId"
      :item="item"
      @changed="scheduled"
      @close="scheduling = false"
    />

    <DeleteItemDialog
      v-if="deleting"
      :site-id="siteId"
      :collection-id="collectionId"
      :item="item"
      :editing-by="editingBy"
      @deleted="(receipt) => emit('deleted', receipt)"
      @cancel="deleting = false"
    />
  </div>
</template>
