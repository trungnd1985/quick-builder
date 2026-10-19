<script setup lang="ts">
// The page as visitors will see it (FR-002): the site's tokens as custom properties on a
// frame that is the `page` container for the block styles' container queries (research
// ED-09). Drops from the palette and moves of blocks land here.
import { menuIdsOf } from '../state/useSiteMenus'
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { dragState, dropResult, endDrag, resolveTarget, setTarget, sourceOf, type DropPosition } from '../dnd/useDragDrop'
import { useEditorStore } from '../state/editorStore'
import { tokenCss } from '../tokens/tokenCss'
import { takesChildren } from '../schema/schema'
import { locate } from '../ops/tree'
import CanvasChildren from './CanvasChildren.vue'
import DropIndicator from './DropIndicator.vue'
import '../blocks/blocks.css'
import './canvas.css'

const props = withDefaults(defineProps<{ width?: number }>(), { width: 1280 })
const emit = defineEmits<{ refused: [code: string] }>()

const { t } = useI18n()
const store = useEditorStore()
const frame = ref<HTMLElement | null>(null)

// When the editor area is narrower than the chosen width, the frame is scaled to fit. Only its
// look is scaled: its layout width, which the block styles' container queries read, stays the
// chosen width (research ED-09). Never below half size — smaller is unreadable; past that the
// area scrolls sideways instead.
const stage = ref<HTMLElement | null>(null)
const scale = ref(1)
const frameHeight = ref(0)
let observer: ResizeObserver | null = null

const MIN_SCALE = 0.5

function measure(): void {
  const area = stage.value?.parentElement
  let available = 0
  if (area) {
    const padding = getComputedStyle(area)
    available = area.clientWidth - parseFloat(padding.paddingLeft || '0') - parseFloat(padding.paddingRight || '0')
  }
  scale.value = available > 0 ? Math.max(MIN_SCALE, Math.min(1, available / props.width)) : 1
  frameHeight.value = frame.value?.offsetHeight ?? 0
}

onMounted(() => {
  if (typeof ResizeObserver === 'undefined') return
  observer = new ResizeObserver(measure)
  if (stage.value?.parentElement) observer.observe(stage.value.parentElement)
  if (frame.value) observer.observe(frame.value)
})
onBeforeUnmount(() => observer?.disconnect())
watch(() => props.width, () => nextTick(measure))

const style = computed(() => ({
  ...tokenCss(store.tokens),
  width: `${props.width}px`,
  ...(scale.value < 1 ? { transform: `scale(${scale.value})`, transformOrigin: 'top left' } : {}),
}))
// The stage takes the frame's scaled size, so the scroll area scrolls exactly as far as needed.
const stageStyle = computed(() =>
  scale.value < 1
    ? { width: `${props.width * scale.value}px`, ...(frameHeight.value > 0 ? { height: `${frameHeight.value * scale.value}px` } : {}) }
    : { width: `${props.width}px` },
)
const empty = computed(() => (store.root?.children.length ?? 0) === 0)

/** Where over which node the pointer is, from the geometry of the node under it. */
function positionOver(event: DragEvent): { id: string; position: DropPosition } | null {
  const element = (event.target as HTMLElement | null)?.closest<HTMLElement>('[data-node-id], [data-empty-parent]')
  if (!element) return store.root ? { id: store.root.id, position: 'inside' } : null

  if (element.dataset.emptyParent) return { id: element.dataset.emptyParent, position: 'inside' }

  const id = element.dataset.nodeId!
  const node = store.root ? locate(store.root, id)?.node : undefined
  const rect = element.getBoundingClientRect()
  // Beside a block in a row means left or right of it, not above or below.
  const horizontal = element.parentElement?.classList.contains('qb-container--horizontal') === true
  const offset = horizontal ? (event.clientX - rect.left) / Math.max(rect.width, 1) : (event.clientY - rect.top) / Math.max(rect.height, 1)

  // A block that holds others takes drops in its middle; its edges are for beside it.
  if (node && takesChildren(node.type) && offset > 0.25 && offset < 0.75) return { id, position: 'inside' }
  return { id, position: offset < 0.5 ? 'before' : 'after' }
}

function onDragOver(event: DragEvent): void {
  const source = sourceOf(event)
  const over = positionOver(event)
  if (!source || !over || !store.root || store.readOnly) return

  const target = setTarget(resolveTarget(store.root, source, over.id, over.position, store.kind))
  if (target?.allowed) {
    event.preventDefault()
    if (event.dataTransfer) event.dataTransfer.dropEffect = source.source === 'palette' ? 'copy' : 'move'
  } else if (event.dataTransfer) {
    event.dataTransfer.dropEffect = 'none'
  }
}

function onDrop(event: DragEvent): void {
  event.preventDefault()
  const source = sourceOf(event)
  const target = dragState.target
  endDrag()
  if (!source || !target || !store.root) return
  if (!target.allowed) {
    emit('refused', 'nesting_not_allowed')
    return
  }

  const refused = store.apply(dropResult(store.root, source, target, store.kind, t, { collectionIds: [...(store.bindings.collectionIds ?? [])], nameFieldOf: store.bindings.nameFieldOf, menuIds: menuIdsOf(store.siteId) }))
  if (refused) emit('refused', refused.code)
}

// Whatever selects a block — the layer view, the keyboard — brings it into view (FR-022).
watch(() => store.selection, async (id) => {
  if (!id) return
  await nextTick()
  frame.value?.querySelector<HTMLElement>(`[data-node-id="${id}"]`)?.scrollIntoView?.({ block: 'nearest' })
})

function onDragLeave(event: DragEvent): void {
  if (!frame.value?.contains(event.relatedTarget as Node | null)) setTarget(null)
}

defineExpose({ frame })
</script>

<template>
  <div
    ref="stage"
    class="qb-canvas-stage"
    :style="stageStyle"
  >
    <div
      ref="frame"
      class="qb-canvas"
      :class="{ 'qb-canvas--dragging': dragState.dragging !== null }"
      :style="style"
      @click="store.select(null)"
      @dragover="onDragOver"
      @dragleave="onDragLeave"
      @drop="onDrop"
    >
      <CanvasChildren
        v-if="store.root"
        :node="store.root"
      />
      <div
        v-if="empty"
        class="qb-editor-empty-page"
      >
        <p>{{ t('editor.canvas.emptyPage.title') }}</p>
        <p>{{ t(store.kind === 'page' ? 'editor.canvas.emptyPage.body' : 'editor.canvas.emptyPage.componentBody') }}</p>
      </div>
      <DropIndicator
        :frame="frame"
        :scale="scale"
      />
    </div>
  </div>
</template>
