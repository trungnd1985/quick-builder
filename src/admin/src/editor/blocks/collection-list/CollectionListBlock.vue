<script setup lang="ts">
// A Collection list on the canvas (016 research TB-07, TB-14): its card once per item the site
// would list, each showing that item's published values, then its empty state with a caption.
// The first card is the one being edited; the rest are drawn from it. Without a source, or
// while nothing is listed, the card shows once, unbound, so it can still be designed.
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import CanvasNode from '../../canvas/CanvasNode.vue'
import { useListPreview } from '../../bindings/listPreview'
import type { ContentNode } from '../../schema/schema'
import { isObject } from '../../schema/subsetValidator'
import { useBlock } from '../useBlock'
import ListItemScope from './ListItemScope.vue'

const props = defineProps<{ node: ContentNode }>()
const { t } = useI18n()
const { prop, token } = useBlock(() => props.node)

const columns = computed(() => Number(prop('columns')) || 3)
const style = computed(() => ({
  '--qb-cols': String(columns.value),
  '--qb-cols-tablet': String(Math.min(columns.value, 2)),
  '--qb-list-gap': token('gap', 'space'),
}))

const card = computed(() => props.node.children.find((child) => child.type === 'list-card') ?? null)
const empty = computed(() => props.node.children.find((child) => child.type === 'list-empty') ?? null)

const preview = useListPreview(() => props.node)
const shown = computed(() => (preview.value.items.length > 0 ? preview.value.items : [null]))

/** US5: a paginated list shows its first page on the canvas, and a pager that does not navigate. */
const pages = computed(() => {
  const paging = props.node.props.paging
  if (!isObject(paging) || preview.value.total === 0) return 0
  const size = typeof paging.pageSize === 'number' && paging.pageSize > 0 ? paging.pageSize : 12
  return Math.ceil(preview.value.total / size)
})
const pagerNumbers = computed(() => {
  const last = pages.value
  return last <= 4 ? Array.from({ length: last }, (_, i) => i + 1) : [1, 2, 3, 0, last]
})

const status = computed(() => {
  const state = preview.value
  if (state.idle) return t('editor.list.chooseSource')
  if (state.loading) return t('editor.list.loading')
  if (state.failed) return t('editor.list.previewFailed')
  if (state.items.length === 0) return t('editor.list.noItems')
  return t('editor.list.showing', { shown: state.items.length, total: state.total })
})
</script>

<template>
  <div class="qb-list-block">
    <p
      class="qb-editor-list-status"
      role="status"
    >
      {{ status }}
    </p>
    <div
      v-if="card"
      class="qb-list"
      :style="style"
    >
      <ListItemScope
        v-for="(item, index) in shown"
        :key="item?.id ?? 'pattern'"
        :card="card"
        :list-id="node.id"
        :item="item"
        :copy="index > 0"
      />
    </div>
    <div
      v-if="pages > 1"
      class="qb-pager qb-pager--canvas"
      aria-hidden="true"
    >
      <span
        v-for="(number, index) in pagerNumbers"
        :key="index"
        :class="number === 0 ? 'qb-pager__gap' : 'qb-pager__page'"
        :aria-current="number === 1 ? 'page' : undefined"
      >{{ number === 0 ? '…' : number }}</span>
      <span class="qb-pager__step">{{ t('editor.list.paging.next') }}</span>
    </div>
    <p
      v-if="pages > 1"
      class="qb-editor-list-status"
    >
      {{ t('editor.list.paging.canvasHint', { pages }) }}
    </p>
    <template v-if="empty">
      <p class="qb-editor-list-status">
        {{ t('editor.list.emptyCaption') }}
      </p>
      <CanvasNode :node="empty" />
    </template>
  </div>
</template>
