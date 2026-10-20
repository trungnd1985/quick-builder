<script setup lang="ts">
// Which menus point at a page or an item, said before it is deleted or unpublished (024 FR-026).
// Shown inside the confirmation: the menus stay valid, but visitors lose those entries. A failed
// look-up shows nothing, since the action itself is still confirmed and the server decides.
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { menusApi, type MenuReference } from '../../api/menus'

const props = defineProps<{
  siteId: string
  target: { pageId: string } | { itemId: string }
  action: 'delete' | 'unpublish'
}>()
const { t } = useI18n()

const menus = ref<MenuReference[]>([])

onMounted(async () => {
  try {
    const found = await menusApi.references(props.siteId, props.target)
    menus.value = Array.isArray(found?.items) ? found.items : []
  } catch {
    menus.value = []
  }
})

const describe = (menu: MenuReference): string =>
  menu.inLive ? menu.name : t('menus.references.draftOnly', { name: menu.name })
</script>

<template>
  <div
    v-if="menus.length > 0"
    class="card warning"
    role="note"
  >
    <p>{{ t(`menus.references.${action}`, { count: menus.length }, menus.length) }}</p>
    <ul>
      <li
        v-for="menu in menus"
        :key="menu.menuId"
      >
        <RouterLink :to="{ name: 'site-menu', params: { siteId, menuId: menu.menuId } }">
          {{ describe(menu) }}
        </RouterLink>
      </li>
    </ul>
  </div>
</template>
