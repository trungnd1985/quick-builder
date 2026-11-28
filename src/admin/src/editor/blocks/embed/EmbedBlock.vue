<script setup lang="ts">
// A video from YouTube or Vimeo. In the editor it is a placeholder naming the provider and
// the address: no frame, no player, no request to a third party (research ED-06, SC-008).
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ContentNode } from '../../schema/schema'
import { useBlock } from '../useBlock'
import { useBorder } from '../useBorder'
import { embedProvider } from './definition'

const props = defineProps<{ node: ContentNode }>()
const { t } = useI18n()
const { prop } = useBlock(() => props.node)

const url = computed(() => (typeof props.node.props.url === 'string' ? props.node.props.url : null))
const provider = computed(() => embedProvider(url.value))
const ratio = computed(() => String(prop('aspectRatio')).replace(':', ' / '))
const { classes: borderClasses, style: borderStyle } = useBorder(() => props.node)

</script>

<template>
  <div
    class="qb-embed qb-editor-placeholder"
    :class="borderClasses"
    :style="{ aspectRatio: ratio, ...borderStyle }"
  >
    <template v-if="url && provider">
      <strong>{{ t(`editor.blocks.embedProvider.${provider}`) }}</strong>
      <span class="qb-embed__url">{{ url }}</span>
    </template>
    <span v-else>{{ t('editor.blocks.placeholder.embed') }}</span>
  </div>
</template>
