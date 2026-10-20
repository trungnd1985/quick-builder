<script setup lang="ts">
// A Navigation block on the canvas (024 FR-018, research NV-16): the chosen menu's draft, drawn
// with the published page's classes so the same stylesheet lays it out, and so the responsive
// preview collapses it at the same widths. Links never navigate here. A menu that no longer
// exists, or was never published, is marked: publishing would be refused (FR-003a).
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { menusApi, type MenuPreviewEntry } from '../../../api/menus'
import type { ContentNode } from '../../schema/schema'
import { isObject } from '../../schema/subsetValidator'
import { useSiteMenus } from '../../state/useSiteMenus'
import { useBlock } from '../useBlock'
import NavigationList from './NavigationList.vue'

const props = defineProps<{ node: ContentNode }>()
const { t } = useI18n()
const { store, prop, tokenIfSet } = useBlock(() => props.node)

const menus = useSiteMenus(store.siteId)
const menuId = computed(() => String(props.node.props.menuId ?? ''))
const entries = ref<MenuPreviewEntry[]>([])
const loaded = ref(false)

const summary = computed(() => menus.value?.items.find((m) => m.id === menuId.value) ?? null)
const missing = computed(() => menus.value !== null && summary.value === null)
const unpublished = computed(() => summary.value?.publication.state === 'draft')

const collapse = computed(() => {
  const value = prop('collapse')
  return isObject(value) && value.enabled === true
    ? { below: value.below === 'tablet' ? 'tablet' : 'mobile', label: typeof value.buttonLabel === 'string' && value.buttonLabel ? value.buttonLabel : t('editor.navigation.menuButton') }
    : null
})

const LOOKS = ['link', 'underline', 'pill', 'button']

// 024 research NV-28: the block's look, else the site's (draft tokens), else plain links.
const look = computed(() => {
  const own = props.node.props.look
  if (typeof own === 'string' && LOOKS.includes(own)) return own
  const site = store.tokens.menuStyle?.look
  return site && LOOKS.includes(site) ? site : 'link'
})

const classes = computed(() => [
  `qb-nav--${prop('layout')}`,
  `qb-nav--align-${prop('align')}`,
  collapse.value ? `qb-nav--collapse-${collapse.value.below}` : '',
  `qb-nav--look-${look.value}`,
])

// contracts/menu-style.md §3: only the colours the block overrides; the rest come from the
// site's menu style in the token CSS, as on the published page.
const OVERRIDES: [string, string][] = [
  ['color', '--qb-nav-text'],
  ['activeColor', '--qb-nav-current-text'],
  ['background', '--qb-nav-background'],
  ['hoverColor', '--qb-nav-hover-text'],
  ['hoverBackground', '--qb-nav-hover-background'],
  ['activeBackground', '--qb-nav-current-background'],
  ['focusColor', '--qb-nav-focus'],
  ['panelBackground', '--qb-nav-panel-background'],
]

const style = computed(() => Object.fromEntries(
  OVERRIDES.flatMap(([name, property]) => {
    const value = tokenIfSet(name, 'color')
    return value === null ? [] : [[property, value]]
  }),
))

const label = computed(() => {
  const own = prop<string | undefined>('ariaLabel')
  return own || summary.value?.name || undefined
})

async function load(): Promise<void> {
  if (!menuId.value) return
  try {
    const preview = await menusApi.preview(store.siteId, menuId.value)
    entries.value = Array.isArray(preview.entries) ? preview.entries : []
  } catch {
    entries.value = []
  } finally {
    loaded.value = true
  }
}

// NV-16: menu drafts change rarely while a page is edited, so the preview is fetched when the
// block appears, when its menu changes, and when the editor window gets the focus again.
onMounted(() => {
  void load()
  window.addEventListener('focus', load)
})
onBeforeUnmount(() => window.removeEventListener('focus', load))
watch(menuId, load)
</script>

<template>
  <nav
    class="qb-nav"
    :class="classes"
    :aria-label="label"
    :style="style"
  >
    <details
      v-if="collapse"
      class="qb-nav__panel"
      open
    >
      <summary class="qb-nav__toggle">
        {{ collapse.label }}
      </summary>
      <NavigationList
        :entries="entries"
        :level="1"
      />
    </details>
    <NavigationList
      v-else
      :entries="entries"
      :level="1"
    />
    <p
      v-if="missing"
      class="qb-editor-nav-note qb-editor-nav-note--error"
    >
      {{ t('editor.navigation.missing') }}
    </p>
    <p
      v-else-if="unpublished"
      class="qb-editor-nav-note"
    >
      {{ t('editor.navigation.unpublished') }}
    </p>
    <p
      v-else-if="loaded && entries.length === 0"
      class="qb-editor-nav-note"
    >
      {{ t('editor.navigation.empty') }}
    </p>
  </nav>
</template>
