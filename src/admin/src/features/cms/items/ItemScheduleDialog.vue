<script setup lang="ts">
// Scheduling an item's publish or unpublish (015 US6, FR-024 to FR-027). Times are entered in
// the member's time zone, named on screen; the server gets UTC. A scheduled publish captures the
// draft now; moving it can also capture the current draft. Each pending schedule can be cancelled.
import { computed, nextTick, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { itemsApi, type Item, type ScheduleAction } from '../../../api/items'
import ZonedDateTimeInput from '../../../components/ZonedDateTimeInput.vue'
import RedirectOfferCheckbox from '../../../components/RedirectOfferCheckbox.vue'
import RedirectToPagePicker, { type PickablePage } from '../../../components/RedirectToPagePicker.vue'
import { pagesApi } from '../../../api/pages'
import { itemErrorMessage } from './itemErrors'

const props = defineProps<{ siteId: string; collectionId: string; item: Item }>()
const emit = defineEmits<{ changed: [item: Item]; close: [] }>()
const { t, te, d } = useI18n()

const existingOf = (action: ScheduleAction) => props.item.schedules.find((s) => s.action === action) ?? null
const inAnHour = (): string => new Date(Date.now() + 60 * 60 * 1000).toISOString()

const publishAt = ref<string | null>(existingOf('publish')?.dueAtUtc ?? inAnHour())
const unpublishAt = ref<string | null>(existingOf('unpublish')?.dueAtUtc ?? null)
const recapture = ref(false)

// 017 FR-029, FR-032: kept on the schedule and applied when it runs; offered only for an item
// with an address visitors use.
const hasAddress = computed(() => !!props.item.address?.path && !props.item.address.reason)
const slugChanges = computed(() => !!props.item.publishedSlug && props.item.publishedSlug !== props.item.slug)
const redirectOldAddress = ref(true)
const redirectToPageId = ref<string | null>(null)
const sitePages = ref<PickablePage[]>([])
const busy = ref(false)
const error = ref<string | null>(null)
const dialog = ref<HTMLElement | null>(null)
let current: Item = props.item

const canUnpublish = computed(() => props.item.status !== 'draft' || existingOf('publish') !== null)

onMounted(async () => {
  if (hasAddress.value) {
    try {
      sitePages.value = (await pagesApi.tree(props.siteId)).pages ?? []
    } catch {
      sitePages.value = []
    }
  }
  await nextTick()
  dialog.value?.querySelector<HTMLElement>('input')?.focus()
})

async function set(action: ScheduleAction): Promise<void> {
  const due = action === 'publish' ? publishAt.value : unpublishAt.value
  error.value = null
  if (!due || new Date(due).getTime() <= Date.now()) {
    error.value = t('cms.items.schedule.inPast')
    return
  }

  busy.value = true
  try {
    current = await itemsApi.schedule(props.siteId, props.collectionId, current.id, action, {
      dueAtUtc: due,
      revision: current.revision,
      recapture: action === 'publish' && recapture.value,
      redirectOldAddress: action === 'publish' && hasAddress.value && slugChanges.value && redirectOldAddress.value,
      redirectToPageId: action === 'unpublish' && hasAddress.value ? redirectToPageId.value : null,
    })
    emit('changed', current)
  } catch (caught) {
    error.value = itemErrorMessage(t, te, caught)
  } finally {
    busy.value = false
  }
}

async function cancel(action: ScheduleAction): Promise<void> {
  busy.value = true
  error.value = null
  try {
    current = await itemsApi.cancelSchedule(props.siteId, props.collectionId, current.id, action, current.revision)
    emit('changed', current)
  } catch (caught) {
    error.value = itemErrorMessage(t, te, caught)
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
</script>

<template>
  <Teleport to="body">
    <div
      class="dialog-backdrop"
      @click.self="emit('close')"
    >
      <div
        ref="dialog"
        class="card dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby="item-schedule-title"
        @keydown="onKeydown"
      >
        <h2 id="item-schedule-title">
          {{ t('cms.items.schedule.title') }}
        </h2>

        <fieldset>
          <legend>{{ t('cms.items.schedule.publishAt') }}</legend>
          <ZonedDateTimeInput
            id="item-schedule-publish"
            v-model="publishAt"
          />
          <p
            v-if="existingOf('publish')"
            class="help"
          >
            {{ t('cms.items.schedule.captured') }}
          </p>
          <label v-if="existingOf('publish')">
            <input
              v-model="recapture"
              type="checkbox"
            >
            {{ t('cms.items.schedule.recapture', { when: d(new Date(existingOf('publish')!.dueAtUtc), 'short') }) }}
          </label>
          <RedirectOfferCheckbox
            v-if="hasAddress && slugChanges"
            id="item-schedule-publish-redirect"
            v-model="redirectOldAddress"
            :count="1"
          />
          <div class="actions">
            <button
              v-if="existingOf('publish')"
              type="button"
              class="danger"
              :disabled="busy"
              @click="cancel('publish')"
            >
              {{ t('cms.items.schedule.cancel') }}
            </button>
            <button
              type="button"
              class="primary"
              :disabled="busy"
              @click="set('publish')"
            >
              {{ existingOf('publish') ? t('cms.items.schedule.move') : t('cms.items.schedule.set') }}
            </button>
          </div>
        </fieldset>

        <fieldset v-if="canUnpublish">
          <legend>{{ t('cms.items.schedule.unpublishAt') }}</legend>
          <ZonedDateTimeInput
            id="item-schedule-unpublish"
            v-model="unpublishAt"
          />
          <RedirectToPagePicker
            v-if="hasAddress"
            id="item-schedule-unpublish-redirect"
            v-model="redirectToPageId"
            :pages="sitePages"
          />
          <div class="actions">
            <button
              v-if="existingOf('unpublish')"
              type="button"
              class="danger"
              :disabled="busy"
              @click="cancel('unpublish')"
            >
              {{ t('cms.items.schedule.cancel') }}
            </button>
            <button
              type="button"
              class="primary"
              :disabled="busy"
              @click="set('unpublish')"
            >
              {{ existingOf('unpublish') ? t('cms.items.schedule.move') : t('cms.items.schedule.set') }}
            </button>
          </div>
        </fieldset>

        <p
          v-if="error"
          class="field-error"
          role="alert"
        >
          {{ error }}
        </p>

        <div class="actions">
          <button
            type="button"
            @click="emit('close')"
          >
            {{ t('cms.items.schedule.close') }}
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>
