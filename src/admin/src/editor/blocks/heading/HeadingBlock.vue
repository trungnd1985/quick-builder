<script setup lang="ts">
// A heading, level 1 to 6, edited in place. Levels map to the site's text sizes by a fixed
// rule (data-model §2), so headings follow the typography tokens.
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ContentNode } from '../../schema/schema'
import { isObject } from '../../schema/subsetValidator'
import { tokenValue } from '../../tokens/tokenCss'
import { useBrokenLinks } from '../brokenLinks'
import { useBlock, useInlineText } from '../useBlock'

const props = defineProps<{ node: ContentNode }>()
const { t } = useI18n()
const { store, prop, token, editable } = useBlock(() => props.node)
const element = ref<HTMLElement | null>(null)
const { onInput, onKeydown, bound } = useInlineText(element, () => props.node, 'text', { singleLine: true })

const SIZE_BY_LEVEL = ['text-3xl', 'text-2xl', 'text-xl', 'text-lg', 'text-md', 'text-md']

const level = computed(() => Math.min(6, Math.max(1, Number(prop('level')) || 2)))
const style = computed(() => ({
  '--qb-heading-color': token('color', 'color'),
  '--qb-heading-size': tokenValue({ token: SIZE_BY_LEVEL[level.value - 1] }, 'text', store.tokens, { token: 'text-md' }),
}))
const empty = computed(() => !props.node.props.text)

// 016 FR-014: a linked heading is underlined, as on the site; on the canvas it never navigates.
// A link bound to an item's page shows its address on hover, a broken one says so.
const isBroken = useBrokenLinks(() => store.siteId)
const bindsLink = computed(() => isObject(props.node.props.bind) && 'link' in props.node.props.bind)
const linked = computed(() => isObject(props.node.props.link) || bindsLink.value)
const title = computed(() => {
  const link = props.node.props.link
  if (isBroken.value(link)) return t('editor.canvas.brokenLink')
  return bindsLink.value && isObject(link) && link.type === 'url' && typeof link.href === 'string'
    ? t('editor.list.itemPageTitle', { address: link.href })
    : undefined
})
</script>

<template>
  <component
    :is="`h${level}`"
    ref="element"
    class="qb-heading"
    :class="[`qb-align--${prop('align')}`, { 'qb-heading--linked': linked }]"
    :title="title"
    :style="style"
    :contenteditable="editable && !bound ? 'plaintext-only' : 'false'"
    :data-placeholder="empty ? t('editor.blocks.placeholder.heading') : undefined"
    spellcheck="true"
    @input="onInput"
    @keydown="onKeydown"
  />
</template>
