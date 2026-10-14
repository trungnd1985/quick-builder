<script setup lang="ts">
// One menu entry: its label, what it points at, and whether it opens in a new tab or is shown
// (024 FR-006 to FR-010). The entry is checked with the menu contract before the dialog closes,
// so the tree only ever holds entries the server would accept (FR-011).
import { computed, nextTick, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import PagePicker, { type PickablePage } from '../../components/PagePicker.vue'
import ItemPicker from '../../components/ItemPicker.vue'
import { ANCHOR_PATTERN, cleanAnchor, entryProblems, LABEL_REQUIRED, NEW_TAB_ALLOWED, type MenuEntry, type MenuTarget, type MenuTargetType } from '../../editor/schema/menuValidator'

const props = defineProps<{
  siteId: string
  /** The entry being edited, or null for a new one. */
  entry: MenuEntry | null
  pages: readonly PickablePage[]
  /** The site's host names, for the hint about addresses on this site. */
  hostNames: readonly string[]
  /** The chosen item's current name, while the item list loads. */
  itemName?: string | null
}>()
const emit = defineEmits<{ save: [fields: Omit<MenuEntry, 'id' | 'children'>]; cancel: [] }>()
const { t } = useI18n()

const dialogId = 'menu-entry-dialog'
const initial = props.entry?.target
const kind = ref<MenuTargetType>(initial?.type ?? 'page')
const label = ref(props.entry?.label ?? '')
const pageId = ref(initial?.type === 'page' ? initial.pageId : '')
const pageAnchor = ref(initial?.type === 'page' ? initial.anchor ?? '' : '')
const item = ref<{ collectionId: string; itemId: string } | null>(initial?.type === 'item' ? { collectionId: initial.collectionId, itemId: initial.itemId } : null)
const href = ref(initial?.type === 'url' ? initial.href : '')
const anchor = ref(initial?.type === 'anchor' ? initial.anchor : '')
const newTab = ref(props.entry?.newTab === true)
const shown = ref(props.entry?.hidden !== true)
const problem = ref<string | null>(null)
const firstField = ref<HTMLInputElement | null>(null)

const KINDS: MenuTargetType[] = ['page', 'item', 'url', 'anchor', 'none']
const labelRequired = computed(() => LABEL_REQUIRED.has(kind.value))
const mayOpenNewTab = computed(() => NEW_TAB_ALLOWED.has(kind.value))

/** FR edge case: an address on one of this site's own host names should be a page target. */
const onOwnHost = computed(() => {
  if (kind.value !== 'url') return false
  try {
    const host = new URL(href.value.trim()).hostname.toLowerCase()
    return props.hostNames.some((h) => h.toLowerCase() === host)
  } catch {
    return false
  }
})

function target(): MenuTarget | null {
  switch (kind.value) {
    case 'page': {
      if (!pageId.value) return null
      const name = cleanAnchor(pageAnchor.value)
      return name ? { type: 'page', pageId: pageId.value, anchor: name } : { type: 'page', pageId: pageId.value }
    }
    case 'item':
      return item.value ? { type: 'item', collectionId: item.value.collectionId, itemId: item.value.itemId } : null
    case 'url':
      return { type: 'url', href: href.value.trim() }
    case 'anchor':
      return { type: 'anchor', anchor: cleanAnchor(anchor.value) }
    default:
      return { type: 'none' }
  }
}

function save(): void {
  problem.value = null
  const chosen = target()
  if (!chosen) {
    problem.value = kind.value === 'item' ? 'item_required' : 'page_required'
    return
  }

  const anchorName = chosen.type === 'anchor' || chosen.type === 'page' ? chosen.anchor : undefined
  if (anchorName !== undefined && !ANCHOR_PATTERN.test(anchorName)) {
    problem.value = 'anchor_invalid'
    return
  }

  const trimmed = label.value.trim()
  const fields: Omit<MenuEntry, 'id' | 'children'> = {
    ...(trimmed ? { label: trimmed } : {}),
    target: chosen,
    ...(mayOpenNewTab.value && newTab.value ? { newTab: true } : {}),
    ...(shown.value ? {} : { hidden: true }),
  }

  const found = entryProblems({ schemaVersion: 1, entries: [{ id: 'check', ...fields, children: [] }] }).get('check')
  if (found) {
    problem.value = found
    return
  }

  emit('save', fields)
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('cancel')
  }
}

onMounted(async () => {
  await nextTick()
  firstField.value?.focus()
})
</script>

<template>
  <div
    class="dialog-backdrop"
    @click.self="emit('cancel')"
  >
    <div
      class="card dialog"
      role="dialog"
      aria-modal="true"
      :aria-labelledby="`${dialogId}-heading`"
      @keydown="onKeydown"
    >
      <form
        novalidate
        @submit.prevent="save"
      >
        <h2 :id="`${dialogId}-heading`">
          {{ entry ? t('menus.entry.editTitle') : t('menus.entry.newTitle') }}
        </h2>

        <div class="field">
          <label :for="`${dialogId}-label`">{{ t('menus.entry.label') }}</label>
          <input
            :id="`${dialogId}-label`"
            ref="firstField"
            v-model="label"
            type="text"
            maxlength="100"
            :required="labelRequired"
            :aria-describedby="`${dialogId}-label-help`"
          >
          <p
            :id="`${dialogId}-label-help`"
            class="help"
          >
            {{ labelRequired ? t('menus.entry.labelRequiredHelp') : t('menus.entry.labelHelp') }}
          </p>
        </div>

        <fieldset>
          <legend>{{ t('menus.entry.target') }}</legend>
          <label
            v-for="option in KINDS"
            :key="option"
            class="checkbox"
          >
            <input
              v-model="kind"
              type="radio"
              :name="`${dialogId}-kind`"
              :value="option"
            >
            {{ t(`menus.entry.kinds.${option}`) }}
          </label>
        </fieldset>

        <template v-if="kind === 'page'">
          <PagePicker
            :id="`${dialogId}-page`"
            v-model="pageId"
            :label="t('menus.entry.page')"
            :pages="pages"
            placeholder=""
          />
          <div class="field">
            <label :for="`${dialogId}-page-anchor`">{{ t('menus.entry.pageAnchor') }}</label>
            <input
              :id="`${dialogId}-page-anchor`"
              v-model="pageAnchor"
              type="text"
              maxlength="101"
              :aria-describedby="`${dialogId}-anchor-help`"
            >
          </div>
        </template>

        <ItemPicker
          v-else-if="kind === 'item'"
          :id="`${dialogId}-item`"
          v-model="item"
          :site-id="siteId"
          :current-name="itemName"
        />

        <div
          v-else-if="kind === 'url'"
          class="field"
        >
          <label :for="`${dialogId}-href`">{{ t('menus.entry.address') }}</label>
          <input
            :id="`${dialogId}-href`"
            v-model="href"
            type="text"
            inputmode="url"
            maxlength="2048"
            :aria-describedby="`${dialogId}-href-help${onOwnHost ? ` ${dialogId}-own-host` : ''}`"
          >
          <p
            :id="`${dialogId}-href-help`"
            class="help"
          >
            {{ t('menus.entry.addressHelp') }}
          </p>
          <p
            v-if="onOwnHost"
            :id="`${dialogId}-own-host`"
            class="help warning"
          >
            {{ t('menus.entry.ownHostHint') }}
          </p>
        </div>

        <div
          v-else-if="kind === 'anchor'"
          class="field"
        >
          <label :for="`${dialogId}-anchor`">{{ t('menus.entry.anchor') }}</label>
          <input
            :id="`${dialogId}-anchor`"
            v-model="anchor"
            type="text"
            maxlength="101"
            :aria-describedby="`${dialogId}-anchor-help`"
          >
        </div>

        <p
          v-if="kind === 'page' || kind === 'anchor'"
          :id="`${dialogId}-anchor-help`"
          class="help"
        >
          {{ t('menus.entry.anchorHelp') }}
        </p>

        <label
          v-if="mayOpenNewTab"
          class="checkbox"
        >
          <input
            v-model="newTab"
            type="checkbox"
          >
          {{ t('menus.entry.newTab') }}
        </label>

        <label class="checkbox">
          <input
            v-model="shown"
            type="checkbox"
            :aria-describedby="`${dialogId}-shown-help`"
          >
          {{ t('menus.entry.shown') }}
        </label>
        <p
          :id="`${dialogId}-shown-help`"
          class="help"
        >
          {{ t('menus.entry.shownHelp') }}
        </p>

        <p
          v-if="problem"
          class="error"
          role="alert"
        >
          {{ t(`menus.entry.problems.${problem}`) }}
        </p>

        <div class="actions">
          <button
            type="button"
            @click="emit('cancel')"
          >
            {{ t('menus.entry.cancel') }}
          </button>
          <button
            type="submit"
            class="primary"
          >
            {{ t('menus.entry.save') }}
          </button>
        </div>
      </form>
    </div>
  </div>
</template>
