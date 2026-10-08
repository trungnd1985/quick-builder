<script setup lang="ts">
// Deleting an item (015 US5, FR-008): confirmed first, saying when it is live. Refused while
// other items refer to it, listing them with links, so the member can change their references.
import { nextTick, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { itemsApi, refusalOf, type Item, type Referrer } from '../../../api/items'
import { errorCodeOf, itemErrorMessage } from './itemErrors'

const props = defineProps<{ siteId: string; collectionId: string; item: Item }>()
const emit = defineEmits<{ deleted: []; cancel: [] }>()
const { t, te } = useI18n()

const busy = ref(false)
const error = ref<string | null>(null)
const referrers = ref<Referrer[]>([])
const total = ref(0)
const cancelButton = ref<HTMLButtonElement | null>(null)

onMounted(async () => {
  await nextTick()
  cancelButton.value?.focus()
})

async function remove(): Promise<void> {
  busy.value = true
  error.value = null
  try {
    await itemsApi.remove(props.siteId, props.collectionId, props.item.id, props.item.revision)
    emit('deleted')
  } catch (caught) {
    if (errorCodeOf(caught) === 'cms.item_referenced') {
      const refusal = refusalOf(caught)
      referrers.value = refusal.referrers ?? []
      total.value = refusal.total ?? referrers.value.length
    } else {
      error.value = itemErrorMessage(t, te, caught)
    }
  } finally {
    busy.value = false
  }
}

const where = (referrer: Referrer): string =>
  [referrer.inDraft ? 'draft' : '', referrer.inPublished ? 'published' : '', referrer.inScheduled ? 'scheduled' : '']
    .filter(Boolean)
    .map((w) => t(`cms.items.delete.where.${w}`))
    .join(', ')

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('cancel')
  }
}
</script>

<template>
  <Teleport to="body">
    <div
      class="dialog-backdrop"
      @click.self="emit('cancel')"
    >
      <div
        class="card dialog"
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="delete-item-title"
        aria-describedby="delete-item-body"
        @keydown="onKeydown"
      >
        <h2 id="delete-item-title">
          {{ t('cms.items.delete.title') }}
        </h2>

        <div id="delete-item-body">
          <template v-if="total === 0">
            <p>{{ t('cms.items.delete.confirm') }}</p>
            <p v-if="item.status !== 'draft'">
              <strong>{{ t('cms.items.delete.live') }}</strong>
            </p>
          </template>
          <template v-else>
            <p role="alert">
              {{ t('cms.items.delete.refused') }}
            </p>
            <ul>
              <li
                v-for="referrer in referrers"
                :key="`${referrer.itemId}-${referrer.fieldId}`"
              >
                <RouterLink :to="{ name: 'site-collection-item', params: { siteId, collectionId: referrer.collectionId, itemId: referrer.itemId } }">
                  {{ referrer.itemName ?? referrer.itemSlug }}
                </RouterLink>
                ({{ referrer.collectionName }}, {{ referrer.fieldName }}: {{ where(referrer) }})
              </li>
            </ul>
            <p v-if="total > referrers.length">
              {{ t('cms.items.delete.more', { count: total - referrers.length }) }}
            </p>
          </template>
        </div>

        <p
          v-if="error"
          class="field-error"
          role="alert"
        >
          {{ error }}
        </p>

        <div class="actions">
          <button
            ref="cancelButton"
            type="button"
            :disabled="busy"
            @click="emit('cancel')"
          >
            {{ total === 0 ? t('cms.items.delete.cancel') : t('cms.items.delete.close') }}
          </button>
          <button
            v-if="total === 0"
            type="button"
            class="danger"
            :disabled="busy"
            @click="remove"
          >
            {{ t('cms.items.delete.delete') }}
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>
