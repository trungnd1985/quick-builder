<script setup lang="ts">
// A call to action. On the canvas it is a styled label edited in place; it never navigates.
// Its colours come from the brand tokens.
import { computed, ref } from 'vue'
import type { ContentNode } from '../../schema/schema'
import { tokenValue } from '../../tokens/tokenCss'
import { useBlock, useInlineText } from '../useBlock'

const props = defineProps<{ node: ContentNode }>()
const { store, prop, editable } = useBlock(() => props.node)
const element = ref<HTMLElement | null>(null)
const { onInput, onKeydown } = useInlineText(element, () => props.node, 'label', { singleLine: true, minLength: 1 })

const style = computed(() => ({
  '--qb-button-brand': tokenValue({ token: 'color-brand' }, 'color', store.tokens, { token: 'color-brand' }),
  '--qb-button-on-brand': tokenValue({ token: 'color-background' }, 'color', store.tokens, { token: 'color-background' }),
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
      :class="`qb-button--${prop('style')}`"
      :style="style"
      :contenteditable="editable ? 'plaintext-only' : 'false'"
      spellcheck="true"
      @input="onInput"
      @keydown="onKeydown"
    />
  </div>
</template>
