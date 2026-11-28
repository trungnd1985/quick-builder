<script setup lang="ts">
// A call to action. On the canvas it is a styled label edited in place; it never navigates.
// Its colours come from the brand tokens.
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ContentNode } from '../../schema/schema'
import { isObject } from '../../schema/subsetValidator'
import { tokenValue } from '../../tokens/tokenCss'
import { useBrokenLinks } from '../brokenLinks'
import { useBlock, useInlineText } from '../useBlock'
import { useBorder } from '../useBorder'

const props = defineProps<{ node: ContentNode }>()
const { t } = useI18n()
const { store, prop, editable } = useBlock(() => props.node)
const isBroken = useBrokenLinks(() => store.siteId)
const broken = computed(() => isBroken.value(props.node.props.link))

// 016 T070: a link bound to an item's page goes nowhere on the canvas; its address shows on hover.
const boundAddress = computed(() => {
  const link = props.node.props.link
  const bind = props.node.props.bind
  return isObject(bind) && 'link' in bind && isObject(link) && link.type === 'url' && typeof link.href === 'string' ? link.href : null
})
const element = ref<HTMLElement | null>(null)
const { onInput, onKeydown, bound } = useInlineText(element, () => props.node, 'label', { singleLine: true, minLength: 1 })

// 066 clarification Q2: a border frames the button itself and replaces its own frame.
const { classes: borderClasses, style: borderStyle } = useBorder(() => props.node)
const style = computed(() => ({
  '--qb-button-brand': tokenValue({ token: 'color-brand' }, 'color', store.tokens, { token: 'color-brand' }),
  '--qb-button-on-brand': tokenValue({ token: 'color-background' }, 'color', store.tokens, { token: 'color-background' }),
  ...borderStyle.value,
}))
</script>

<template>
  <div
    class="qb-button-row"
    :class="`qb-align--${prop('align')}`"
  >
    <span
      ref="element"
      class="qb-button"
      :class="[`qb-button--${prop('style')}`, borderClasses]"
      :title="boundAddress ? t('editor.list.itemPageTitle', { address: boundAddress }) : undefined"
      :style="style"
      :contenteditable="editable && !bound ? 'plaintext-only' : 'false'"
      spellcheck="true"
      @input="onInput"
      @keydown="onKeydown"
    />
    <span
      v-if="broken"
      class="qb-editor-broken-link"
    >{{ t('editor.canvas.brokenLink') }}</span>
  </div>
</template>
