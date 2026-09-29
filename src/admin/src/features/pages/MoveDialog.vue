<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { PageNode } from '../../api/pages'

// 010 US2, FR-009, FR-033: move a page, with its subtree, anywhere — without a pointing
// device. Two selects: the new parent, then the place among its children. A page cannot be
// offered as a parent of itself or of anything under it, and the home page stays at the
// top level; the server refuses both regardless.

const props = defineProps<{
  dialogId: string
  page: PageNode
  pages: PageNode[]
  busy: boolean
  error: string | null
  /** 012 FR-011: published pages whose address a move to another parent changes. */
  liveAddressCount?: number
}>()

const emit = defineEmits<{
  submit: [value: { parentId: string | null; afterPageId: string | null }]
  cancel: []
}>()

const { t } = useI18n()

const TOP = ''
const FIRST = ''

/** The page and everything under it: never a valid new parent. */
const excluded = computed(() => {
  const ids = new Set([props.page.id])
  let grew = true
  while (grew) {
    grew = false
    for (const candidate of props.pages) {
      if (candidate.parentId !== null && ids.has(candidate.parentId) && !ids.has(candidate.id)) {
        ids.add(candidate.id)
        grew = true
      }
    }
  }

  return ids
})

const parents = computed(() => (props.page.isHome ? [] : props.pages.filter((p) => !excluded.value.has(p.id))))

const parentId = ref<string>(props.page.parentId ?? TOP)

const siblings = computed(() =>
  props.pages
    .filter((p) => (p.parentId ?? TOP) === parentId.value && p.id !== props.page.id)
    .sort((a, b) => a.position - b.position))

function currentAnchor(): string {
  if ((props.page.parentId ?? TOP) !== parentId.value) {
    return siblings.value.at(-1)?.id ?? FIRST
  }

  const before = props.pages
    .filter((p) => p.parentId === props.page.parentId && p.position < props.page.position)
    .sort((a, b) => b.position - a.position)[0]
  return before?.id ?? FIRST
}

const afterPageId = ref<string>(currentAnchor())
const movesLivePages = computed(() => (props.liveAddressCount ?? 0) > 0 && (props.page.parentId ?? TOP) !== parentId.value)
const parentSelect = ref<HTMLSelectElement | null>(null)

function onParentChange(): void {
  afterPageId.value = currentAnchor()
}

onMounted(async () => {
  await nextTick()
  parentSelect.value?.focus()
})

function indent(page: PageNode): string {
  return '  '.repeat(page.depth - 1)
}

function submit(): void {
  emit('submit', {
    parentId: parentId.value === TOP ? null : parentId.value,
    afterPageId: afterPageId.value === FIRST ? null : afterPageId.value,
  })
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('cancel')
  }
}
</script>

<template>
  <div
    class="dialog-backdrop"
    @click.self="emit('cancel')"
  >
    <form
      class="card dialog"
      role="dialog"
      aria-modal="true"
      :aria-labelledby="`${dialogId}-title`"
      novalidate
      @keydown="onKeydown"
      @submit.prevent="submit"
    >
      <h2 :id="`${dialogId}-title`">
        {{ t('pages.move.title', { title: page.title }) }}
      </h2>

      <div class="field">
        <label :for="`${dialogId}-parent`">{{ t('pages.move.parent') }}</label>
        <select
          :id="`${dialogId}-parent`"
          ref="parentSelect"
          v-model="parentId"
          @change="onParentChange"
        >
          <option :value="TOP">
            {{ t('pages.move.topLevel') }}
          </option>
          <option
            v-for="candidate in parents"
            :key="candidate.id"
            :value="candidate.id"
          >
            {{ indent(candidate) }}{{ candidate.title }} ({{ candidate.path }})
          </option>
        </select>
      </div>

      <div class="field">
        <label :for="`${dialogId}-position`">{{ t('pages.move.position') }}</label>
        <select
          :id="`${dialogId}-position`"
          v-model="afterPageId"
        >
          <option :value="FIRST">
            {{ t('pages.move.first') }}
          </option>
          <option
            v-for="sibling in siblings"
            :key="sibling.id"
            :value="sibling.id"
          >
            {{ t('pages.move.after', { title: sibling.title }) }}
          </option>
        </select>
      </div>

      <p
        v-if="movesLivePages"
        class="warning"
        role="note"
      >
        {{ t('publishing.address.warning', { count: liveAddressCount ?? 0 }) }}
      </p>

      <p
        v-if="error"
        class="error"
        role="alert"
      >
        {{ error }}
      </p>

      <div class="actions">
        <button
          type="button"
          :disabled="busy"
          @click="emit('cancel')"
        >
          {{ t('pages.move.cancel') }}
        </button>
        <button
          type="submit"
          class="primary"
          :disabled="busy"
        >
          {{ busy ? t('pages.move.moving') : t('pages.move.move') }}
        </button>
      </div>
    </form>
  </div>
</template>
