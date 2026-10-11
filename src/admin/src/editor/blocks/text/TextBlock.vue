<script setup lang="ts">
// Plain text with line breaks kept, edited in place.
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ContentNode } from '../../schema/schema'
import { useBlock, useInlineText } from '../useBlock'

const props = defineProps<{ node: ContentNode }>()
const { t } = useI18n()
const { prop, token, editable } = useBlock(() => props.node)
const element = ref<HTMLElement | null>(null)
const { onInput, onKeydown, bound } = useInlineText(element, () => props.node, 'text')

const style = computed(() => ({
  '--qb-text-color': token('color', 'color'),
  '--qb-text-size': token('size', 'text'),
}))
const empty = computed(() => !props.node.props.text)
</script>

<template>
  <p
    ref="element"
    class="qb-text"
    :class="`qb-align--${prop('align')}`"
    :style="style"
    :contenteditable="editable && !bound ? 'plaintext-only' : 'false'"
    :data-placeholder="empty ? t('editor.blocks.placeholder.text') : undefined"
    spellcheck="true"
    @input="onInput"
    @keydown="onKeydown"
  />
</template>
