<script setup lang="ts">
// Choosing items of a collection for a reference field (015 US4, FR-016; research IT-21): a
// search over the target collection's list, one page at a time, as a listbox. Arrow keys move,
// Enter chooses, Escape closes. Items already chosen, and the item itself, cannot be chosen.
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { itemsApi, type ItemRef, type ItemSummary } from '../../../api/items'
import { itemErrorMessage } from './itemErrors'

const props = defineProps<{
  siteId: string
  collectionId: string
  title: string
  /** Ids that cannot be chosen: already chosen, or the item itself. */
  disabled: ReadonlySet<string>
  selfId: string | null
}>()
const emit = defineEmits<{ choose: [item: ItemRef]; close: [] }>()
const { t, te } = useI18n()

const SEARCH_DELAY_MS = 300
const PAGE_SIZE = 25

const dialog = ref<HTMLElement | null>(null)
const searchBox = ref<HTMLInputElement | null>(null)
const search = ref('')
const page = ref(1)
const items = ref<ItemSummary[]>([])
const total = ref(0)
const active = ref(0)
const error = ref<string | null>(null)
let timer: ReturnType<typeof setTimeout> | undefined

const pages = computed(() => Math.max(1, Math.ceil(total.value / PAGE_SIZE)))
const listId = 'reference-picker-list'
const optionId = (index: number): string => `reference-picker-option-${index}`
const isDisabled = (item: ItemSummary): boolean =>
  props.disabled.has(item.id.toLowerCase()) || (props.selfId !== null && item.id.toLowerCase() === props.selfId.toLowerCase())

async function load(): Promise<void> {
  error.value = null
  try {
    const result = await itemsApi.list(props.siteId, props.collectionId, { q: search.value.trim() || undefined, page: page.value, pageSize: PAGE_SIZE })
    items.value = result.items
    total.value = result.total
    active.value = Math.max(0, items.value.findIndex((i) => !isDisabled(i)))
  } catch (caught) {
    error.value = itemErrorMessage(t, te, caught)
  }
}

watch(search, () => {
  clearTimeout(timer)
  timer = setTimeout(() => {
    page.value = 1
    void load()
  }, SEARCH_DELAY_MS)
})
watch(page, () => void load())

onMounted(async () => {
  await load()
  await nextTick()
  searchBox.value?.focus()
})
onBeforeUnmount(() => clearTimeout(timer))

function choose(item: ItemSummary | undefined): void {
  if (!item || isDisabled(item)) return
  emit('choose', { id: item.id, collectionId: props.collectionId, name: item.name, slug: item.slug, status: item.status })
}

function moveActive(step: number): void {
  if (items.value.length === 0) return
  active.value = (active.value + step + items.value.length) % items.value.length
  void nextTick(() => document.getElementById(optionId(active.value))?.scrollIntoView?.({ block: 'nearest' }))
}

function onListKeydown(event: KeyboardEvent): void {
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault()
    moveActive(event.key === 'ArrowDown' ? 1 : -1)
  } else if (event.key === 'Home' || event.key === 'End') {
    event.preventDefault()
    active.value = event.key === 'Home' ? 0 : items.value.length - 1
  } else if (event.key === 'Enter') {
    event.preventDefault()
    choose(items.value[active.value])
  }
}

/** Keeps keyboard focus inside the dialog, and Escape closes it. */
function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('close')
    return
  }

  if (event.key !== 'Tab' || !dialog.value) return
  const focusable = Array.from(dialog.value.querySelectorAll<HTMLElement>('button:not([disabled]), input, [tabindex="0"]'))
  const first = focusable[0]
  const last = focusable.at(-1)
  if (!first || !last) return
  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault()
    last.focus()
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault()
    first.focus()
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
        class="card dialog reference-picker"
        role="dialog"
        aria-modal="true"
        aria-labelledby="reference-picker-title"
        @keydown="onKeydown"
      >
        <header class="page-header">
          <h2 id="reference-picker-title">
            {{ t('cms.items.picker.title', { collection: title }) }}
          </h2>
          <button
            type="button"
            @click="emit('close')"
          >
            {{ t('cms.items.picker.close') }}
          </button>
        </header>

        <label
          for="reference-picker-search"
          class="visually-hidden"
        >{{ t('cms.items.picker.search') }}</label>
        <input
          id="reference-picker-search"
          ref="searchBox"
          v-model="search"
          type="search"
          :placeholder="t('cms.items.picker.search')"
          :aria-controls="listId"
        >

        <p
          v-if="error"
          class="field-error"
          role="alert"
        >
          {{ error }}
        </p>
        <p
          v-else-if="items.length === 0"
          class="help"
        >
          {{ t('cms.items.picker.empty') }}
        </p>
        <ul
          v-else
          :id="listId"
          role="listbox"
          tabindex="0"
          :aria-label="t('cms.items.picker.title', { collection: title })"
          :aria-activedescendant="optionId(active)"
          class="reference-picker__list"
          @keydown="onListKeydown"
        >
          <li
            v-for="(item, index) in items"
            :id="optionId(index)"
            :key="item.id"
            role="option"
            :aria-selected="index === active"
            :aria-disabled="isDisabled(item)"
            :class="{ 'is-active': index === active, 'is-disabled': isDisabled(item) }"
            @click="choose(item)"
          >
            <span class="reference-picker__name">{{ item.name ?? item.slug }}</span>
            <code>{{ item.slug }}</code>
            <span
              v-if="item.status === 'draft'"
              class="tag"
            >{{ t('cms.items.status.notPublished') }}</span>
            <span
              v-if="selfId !== null && item.id.toLowerCase() === selfId.toLowerCase()"
              class="tag"
            >{{ t('cms.items.picker.self') }}</span>
            <span
              v-else-if="disabled.has(item.id.toLowerCase())"
              class="tag"
            >{{ t('cms.items.picker.chosen') }}</span>
          </li>
        </ul>

        <nav
          v-if="pages > 1"
          class="pager"
          :aria-label="t('cms.items.pager.label')"
        >
          <button
            type="button"
            :disabled="page <= 1"
            @click="page--"
          >
            {{ t('cms.items.pager.previous') }}
          </button>
          <span>{{ t('cms.items.pager.page', { page, pages }) }}</span>
          <button
            type="button"
            :disabled="page >= pages"
            @click="page++"
          >
            {{ t('cms.items.pager.next') }}
          </button>
        </nav>
      </div>
    </div>
  </Teleport>
</template>
