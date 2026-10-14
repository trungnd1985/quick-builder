<script setup lang="ts">
// Choosing a collection item (024 FR-010): first its collection, then the item, found by name
// or slug through the item list (015). Returns the item with its collection, as a menu entry
// stores it (menu.md §3).
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { cmsApi, type CollectionSummary } from '../api/cms'
import { itemsApi, type ItemSummary } from '../api/items'

const props = defineProps<{
  id: string
  siteId: string
  /** The item's name when it is already chosen, shown until the list loads. */
  currentName?: string | null
}>()

/** The chosen item and its collection, or null. */
const chosen = defineModel<{ collectionId: string; itemId: string } | null>({ required: true })
const { t } = useI18n()

const collections = ref<CollectionSummary[]>([])
const collectionId = ref(chosen.value?.collectionId ?? '')
const query = ref('')
const items = ref<ItemSummary[]>([])
const loading = ref(false)
const failed = ref(false)

onMounted(async () => {
  try {
    collections.value = (await cmsApi.list(props.siteId)).collections
    if (!collectionId.value && collections.value.length === 1) collectionId.value = collections.value[0].id
  } catch {
    failed.value = true
  }
})

let searchSeq = 0
watch([collectionId, query], async ([collection, text]) => {
  if (!collection) {
    items.value = []
    return
  }
  const mine = ++searchSeq
  loading.value = true
  try {
    const page = await itemsApi.list(props.siteId, collection, { q: text.trim() || undefined, pageSize: 25 })
    if (mine === searchSeq) items.value = page.items
  } catch {
    if (mine === searchSeq) failed.value = true
  } finally {
    if (mine === searchSeq) loading.value = false
  }
}, { immediate: true })

const itemId = computed({
  get: () => (chosen.value && chosen.value.collectionId === collectionId.value ? chosen.value.itemId : ''),
  set: (value: string) => {
    chosen.value = value ? { collectionId: collectionId.value, itemId: value } : null
  },
})

const showsCurrent = computed(() => itemId.value !== '' && !items.value.some((i) => i.id === itemId.value))
</script>

<template>
  <fieldset class="item-picker">
    <legend>{{ t('menus.entry.item') }}</legend>
    <div class="field">
      <label :for="`${id}-collection`">{{ t('menus.entry.collection') }}</label>
      <select
        :id="`${id}-collection`"
        v-model="collectionId"
      >
        <option value="">
          {{ t('menus.entry.chooseCollection') }}
        </option>
        <option
          v-for="collection in collections"
          :key="collection.id"
          :value="collection.id"
        >
          {{ collection.name }}
        </option>
      </select>
    </div>
    <template v-if="collectionId">
      <div class="field">
        <label :for="`${id}-search`">{{ t('menus.entry.searchItems') }}</label>
        <input
          :id="`${id}-search`"
          v-model="query"
          type="search"
          maxlength="200"
        >
      </div>
      <div class="field">
        <label :for="`${id}-item`">{{ t('menus.entry.itemField') }}</label>
        <select
          :id="`${id}-item`"
          v-model="itemId"
          :aria-busy="loading ? 'true' : undefined"
        >
          <option value="">
            {{ t('menus.entry.chooseItem') }}
          </option>
          <option
            v-if="showsCurrent"
            :value="itemId"
          >
            {{ currentName || t('menus.entry.untitledItem') }}
          </option>
          <option
            v-for="item in items"
            :key="item.id"
            :value="item.id"
          >
            {{ item.name || t('menus.entry.untitledItem') }} ({{ item.slug }})
          </option>
        </select>
      </div>
    </template>
    <p
      v-if="failed"
      class="error"
      role="alert"
    >
      {{ t('menus.entry.itemsFailed') }}
    </p>
  </fieldset>
</template>
