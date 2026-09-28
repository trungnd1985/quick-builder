<script setup lang="ts">
// A shared component on a page (011 US7, FR-038): the component's current content, drawn in
// place and marked as shared. It is one block on this page — selected, moved and removed as a
// unit — and its content is edited in the component itself, never here.
//
// The content renders in the canvas's static mode: no node ids, no selection, no editing. A
// component's ids are unique only within the component, so they must not mix with the page's.
import { computed, provide } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ContentNode } from '../schema/schema'
import { useEditorStore } from '../state/editorStore'
import { useSharedComponent } from '../state/componentCache'
import CanvasChildren from './CanvasChildren.vue'
import { STATIC_CANVAS } from './staticCanvas'

const props = defineProps<{ node: ContentNode }>()
const { t } = useI18n()
const store = useEditorStore()

provide(STATIC_CANVAS, true)

const componentId = computed(() => (typeof props.node.props.componentId === 'string' ? props.node.props.componentId : ''))
const shared = computed(() => useSharedComponent(store.siteId, componentId.value))
</script>

<template>
  <div class="qb-component-ref">
    <span class="qb-component-ref__badge">{{ t('editor.components.shared') }}<template v-if="shared.name">: {{ shared.name }}</template></span>
    <div
      v-if="shared.state === 'ready' && shared.root"
      class="qb-component-ref__content"
      inert
    >
      <CanvasChildren :node="shared.root" />
    </div>
    <div
      v-else
      class="qb-editor-placeholder"
    >
      {{ shared.state === 'loading' ? t('editor.components.loading') : t('editor.components.missing') }}
    </div>
  </div>
</template>
