<script setup lang="ts">
// The item list's pages (015 FR-028): previous and next, where the member is, the total, and
// how many items a page shows.
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { PAGE_SIZES, type PageSize } from './listQuery'

const props = defineProps<{ page: number; pageSize: number; total: number }>()
const emit = defineEmits<{ page: [page: number]; pageSize: [size: PageSize] }>()
const { t } = useI18n()

const pages = computed(() => Math.max(1, Math.ceil(props.total / props.pageSize)))
</script>

<template>
  <nav
    class="pager"
    :aria-label="t('cms.items.pager.label')"
  >
    <p
      class="help"
      aria-live="polite"
    >
      {{ t('cms.items.list.total', { count: total }, total) }}
    </p>
    <button
      type="button"
      :disabled="page <= 1"
      @click="emit('page', page - 1)"
    >
      {{ t('cms.items.pager.previous') }}
    </button>
    <span>{{ t('cms.items.pager.page', { page, pages }) }}</span>
    <button
      type="button"
      :disabled="page >= pages"
      @click="emit('page', page + 1)"
    >
      {{ t('cms.items.pager.next') }}
    </button>
    <label for="item-page-size">{{ t('cms.items.pager.pageSize') }}</label>
    <select
      id="item-page-size"
      :value="pageSize"
      @change="emit('pageSize', Number(($event.target as HTMLSelectElement).value) as PageSize)"
    >
      <option
        v-for="size in PAGE_SIZES"
        :key="size"
        :value="size"
      >
        {{ size }}
      </option>
    </select>
  </nav>
</template>
