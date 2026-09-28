<script setup lang="ts">
// Formatted text: paragraphs, bold, italic, links and lists (FR-013). Drawn from the stored
// structure; in-place editing with TipTap mounts on demand (US2).
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import RichTextView from '../../richtext/RichTextView'
import type { ContentNode } from '../../schema/schema'
import { isObject } from '../../schema/subsetValidator'
import { useBlock } from '../useBlock'

const props = defineProps<{ node: ContentNode }>()
const { t } = useI18n()
const { prop, token } = useBlock(() => props.node)

const content = computed(() => props.node.props.content ?? null)
const empty = computed(() => {
  const doc = content.value
  return !isObject(doc) || !Array.isArray(doc.content) || doc.content.length === 0
})
const style = computed(() => ({ '--qb-text-color': token('color', 'color') }))
</script>

<template>
  <div
    class="qb-rich-text"
    :class="`qb-align--${prop('align')}`"
    :style="style"
    :data-placeholder="empty ? t('editor.blocks.placeholder.richText') : undefined"
  >
    <RichTextView :doc="content" />
  </div>
</template>
