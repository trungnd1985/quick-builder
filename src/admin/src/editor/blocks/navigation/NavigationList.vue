<script setup lang="ts">
// One level of a menu on the canvas, with the published page's classes (024 public-navigation.md
// §4). Labels are text, not links: nothing on the canvas navigates. Hidden entries are shown
// faded, and entries whose target is not live are marked, so the designer sees what visitors
// will not.
import { useI18n } from 'vue-i18n'
import type { MenuPreviewEntry } from '../../../api/menus'

defineProps<{ entries: MenuPreviewEntry[]; level: number }>()
const { t } = useI18n()
</script>

<template>
  <ul
    v-if="entries.length > 0"
    class="qb-nav__list"
    :class="`qb-nav__list--l${level}`"
  >
    <li
      v-for="entry in entries"
      :key="entry.id"
      class="qb-nav__item"
      :class="{ 'qb-editor-nav-hidden': entry.hidden, 'qb-editor-nav-not-live': entry.state !== 'live' }"
    >
      <span
        v-if="entry.kind !== 'none' || entry.children.length === 0"
        class="qb-nav__link"
        :title="entry.state !== 'live' ? t(`menus.tree.states.${entry.state}`) : undefined"
      >{{ entry.label || t('menus.tree.untitled') }}</span>
      <details
        v-if="entry.children.length > 0"
        class="qb-nav__sub"
      >
        <summary
          class="qb-nav__expander"
          :class="{ 'qb-nav__expander--label': entry.kind === 'none' }"
        >
          <template v-if="entry.kind === 'none'">
            {{ entry.label }}
          </template>
          <span
            v-else
            class="qb-nav__hidden"
          >{{ t('editor.navigation.showSubmenu', { label: entry.label }) }}</span>
        </summary>
        <NavigationList
          :entries="entry.children"
          :level="level + 1"
        />
      </details>
    </li>
  </ul>
</template>
