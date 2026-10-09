<script setup lang="ts">
// The rich text toolbar, shared by the canvas's Rich Text block and item rich text fields
// (035 FR-006, FR-007, FR-027, research RT-07). Icon buttons in groups, with their names and
// shortcuts as tooltips; one tab stop, moved through with the arrow keys, Home and End (the
// WAI-ARIA toolbar pattern). Only formats the contract allows are offered, and a field's
// allowed formats can hide whole groups.
import { computed, getCurrentInstance, onBeforeUnmount, ref, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import type { Editor } from '@tiptap/core'
import type { MediaFile } from '../../api/media'
import IconButton, { type IconName } from '../../components/IconButton.vue'
import MediaPickerDialog from '../../features/media/MediaPickerDialog.vue'
import LinkControl from '../panel/controls/LinkControl.vue'
import type { Json } from '../schema/subsetValidator'
import { ALL_FORMATS, type AllowedFormats } from './formats'
import ImageGroup from './ImageGroup.vue'
import TableGroup from './TableGroup.vue'

/** The host's undo and redo: the page editor's one history, or the item field's own. */
export interface RichTextHistory {
  undo: () => void
  redo: () => void
  canUndo: boolean
  canRedo: boolean
}

interface Control {
  id: string
  /** A menu instead of a button: the block style (035 US1). */
  kind?: 'select'
  /** Every button has one; the menu has none. */
  icon?: IconName
  label: string
  shortcut?: string
  /** Whether the format is on at the cursor; absent for actions. */
  pressed?: boolean
  disabled?: boolean
  expanded?: boolean
  run: () => void
}

const props = withDefaults(defineProps<{
  editor: Editor
  siteId: string
  allowed?: AllowedFormats
  history?: RichTextHistory
  /** What the last paste or drop left out, said politely (035 FR-012). */
  notice?: string
}>(), { allowed: () => ALL_FORMATS, history: undefined, notice: '' })

const { t } = useI18n()

// The editor is not reactive: count its transactions, so the pressed states follow the cursor.
const tick = ref(0)
const onTransaction = (): void => {
  tick.value++
}
watch(() => props.editor, (next, previous) => {
  previous?.off('transaction', onTransaction)
  next.on('transaction', onTransaction)
}, { immediate: true })
onBeforeUnmount(() => props.editor.off('transaction', onTransaction))

const active = (name: string, attributes?: Record<string, unknown>): boolean => {
  void tick.value
  return props.editor.isActive(name, attributes)
}

/** Whether the block at the cursor has this alignment of its own (035 US6). */
const aligned = (align: string): boolean => {
  void tick.value
  return props.editor.isActive({ textAlign: align })
}

const run = (command: (chain: ReturnType<Editor['chain']>) => ReturnType<Editor['chain']>) => () => {
  command(props.editor.chain().focus()).run()
}

// Images come from the site's media library (035 FR-010): a new one, or another file for the selected one.
const picking = ref<'insert' | 'replace' | null>(null)
const pickerId = `rt-picker-${getCurrentInstance()?.uid ?? 0}`

function chooseImage(file: MediaFile): void {
  const libraryAlt = file.altText?.trim() ? file.altText : null
  if (picking.value === 'replace') {
    const own = props.editor.getAttributes('image').alt
    props.editor.chain().focus().updateAttributes('image', {
      media: { mediaId: file.id },
      alt: typeof own === 'string' && own.trim() !== '' ? own : libraryAlt,
    }).run()
  } else {
    props.editor.chain().focus().insertContent({ type: 'image', attrs: { media: { mediaId: file.id }, alt: libraryAlt } }).run()
  }
  picking.value = null
}

function closePicker(): void {
  picking.value = null
  props.editor.commands.focus()
}

const linking = ref(false)
const linkValue = shallowRef<Json | undefined>(undefined)

function toggleLink(): void {
  if (linking.value) {
    linking.value = false
    return
  }
  linkValue.value = (props.editor.getAttributes('link').to as Json | undefined) ?? undefined
  linking.value = true
}

function applyLink(value: Json | undefined): void {
  linkValue.value = value
  const chain = props.editor.chain().focus().extendMarkRange('link')
  if (value === undefined) chain.unsetMark('link').run()
  else chain.setMark('link', { to: value }).run()
}

/** The groups, in toolbar order; empty groups are left out. */
const groups = computed<Control[][]>(() => {
  const history: Control[] = props.history
    ? [
        { id: 'undo', icon: 'undo', label: t('editor.richText.undo'), shortcut: 'Mod+Z', disabled: !props.history.canUndo, run: props.history.undo },
        { id: 'redo', icon: 'redo', label: t('editor.richText.redo'), shortcut: 'Mod+Shift+Z', disabled: !props.history.canRedo, run: props.history.redo },
      ]
    : []

  // The block style menu: a native select, so its own arrow keys keep working (RT-07).
  const block: Control[] = props.allowed.headings
    ? [{ id: 'blockStyle', kind: 'select', label: t('editor.richText.blockStyle'), run: () => undefined }]
    : []

  const inline: Control[] = [
    { id: 'bold', icon: 'bold', label: t('editor.richText.bold'), shortcut: 'Mod+B', pressed: active('bold'), run: run((c) => c.toggleBold()) },
    { id: 'italic', icon: 'italic', label: t('editor.richText.italic'), shortcut: 'Mod+I', pressed: active('italic'), run: run((c) => c.toggleItalic()) },
    { id: 'underline', icon: 'underline', label: t('editor.richText.underline'), shortcut: 'Mod+U', pressed: active('underline'), run: run((c) => c.toggleUnderline()) },
    { id: 'strike', icon: 'strike', label: t('editor.richText.strike'), shortcut: 'Mod+Shift+X', pressed: active('strike'), run: run((c) => c.toggleStrike()) },
    { id: 'code', icon: 'code', label: t('editor.richText.code'), shortcut: 'Mod+E', pressed: active('code'), run: run((c) => c.toggleCode()) },
  ]

  const lists: Control[] = [
    { id: 'bulletList', icon: 'bullet-list', label: t('editor.richText.bulletList'), shortcut: 'Mod+Shift+8', pressed: active('bulletList'), run: run((c) => c.toggleBulletList()) },
    { id: 'orderedList', icon: 'numbered-list', label: t('editor.richText.orderedList'), shortcut: 'Mod+Shift+7', pressed: active('orderedList'), run: run((c) => c.toggleOrderedList()) },
    ...(props.allowed.headings
      ? [{ id: 'quote', icon: 'quote', label: t('editor.richText.quote'), shortcut: 'Mod+Shift+B', pressed: active('blockquote'), run: run((c) => c.toggleBlockquote()) } satisfies Control]
      : []),
  ]

  const insert: Control[] = [
    { id: 'link', icon: 'link', label: t('editor.richText.link'), pressed: active('link'), expanded: linking.value, run: toggleLink },
    ...(props.allowed.images
      ? [{ id: 'image', icon: 'image', label: t('editor.richText.image.insert'), run: () => { picking.value = 'insert' } } satisfies Control]
      : []),
    ...(props.allowed.tables
      ? [{ id: 'table', icon: 'table', label: t('editor.richText.table.insert'), pressed: active('table'), run: run((c) => c.insertContractTable()) } satisfies Control]
      : []),
  ]

  // Pressing an alignment that is already on takes it off: the block's own alignment again.
  const alignment: Control[] = (['start', 'center', 'end'] as const).map((align) => {
    const on = aligned(align)
    return {
      id: `align-${align}`,
      icon: `align-${align}` as IconName,
      label: t(`editor.richText.align.${align}`),
      pressed: on,
      run: run((c) => (on ? c.unsetTextAlign() : c.setTextAlign(align))),
    }
  })

  return [history, block, inline, lists, insert, alignment].filter((group) => group.length > 0)
})

const flat = computed(() => groups.value.flat())

const BLOCK_STYLES = ['paragraph', 'heading-2', 'heading-3', 'heading-4'] as const
type BlockStyle = (typeof BLOCK_STYLES)[number]

const blockStyle = computed<BlockStyle>(() => {
  for (const level of [2, 3, 4] as const) if (active('heading', { level })) return `heading-${level}`
  return 'paragraph'
})

const styleLabel = (style: BlockStyle): string =>
  style === 'paragraph' ? t('editor.richText.paragraph') : t(`editor.richText.heading${style.slice(-1)}`)

function setBlockStyle(event: Event): void {
  const style = (event.target as HTMLSelectElement).value as BlockStyle
  const chain = props.editor.chain().focus()
  if (style === 'paragraph') chain.setParagraph().run()
  else chain.setHeading({ level: Number(style.slice(-1)) as 2 | 3 | 4 }).run()
}

// Roving tab stop: one control is in the tab order; the arrow keys move it.
const current = ref(0)
const toolbar = ref<HTMLElement | null>(null)

watch(flat, (controls) => {
  if (current.value >= controls.length) current.value = Math.max(0, controls.length - 1)
})

const elements = (): HTMLElement[] => [...(toolbar.value?.querySelectorAll<HTMLElement>('[data-rt-control]') ?? [])]

function moveTo(index: number): void {
  const all = elements()
  if (all.length === 0) return
  current.value = (index + all.length) % all.length
  all[current.value]?.focus()
}

function onKeydown(event: KeyboardEvent): void {
  const index = elements().indexOf(event.target as HTMLElement)
  if (index < 0) return
  const next = { ArrowRight: index + 1, ArrowLeft: index - 1, Home: 0, End: elements().length - 1 }[event.key]
  if (next === undefined) return
  event.preventDefault()
  moveTo(next)
}

function onFocusIn(event: FocusEvent): void {
  const index = elements().indexOf(event.target as HTMLElement)
  if (index >= 0) current.value = index
}

const indexOf = (control: Control): number => flat.value.indexOf(control)
</script>

<template>
  <div
    ref="toolbar"
    class="qb-rt-toolbar"
    role="toolbar"
    :aria-label="t('editor.richText.toolbar')"
    @keydown="onKeydown"
    @focusin="onFocusIn"
  >
    <template
      v-for="(group, g) in groups"
      :key="g"
    >
      <span
        v-if="g > 0"
        class="qb-rt-toolbar__separator"
        role="separator"
        aria-orientation="vertical"
      />
      <div class="qb-rt-toolbar__group">
        <template
          v-for="control in group"
          :key="control.id"
        >
          <select
            v-if="control.kind === 'select'"
            data-rt-control
            class="qb-rt-toolbar__style"
            :aria-label="control.label"
            :value="blockStyle"
            :tabindex="indexOf(control) === current ? 0 : -1"
            @change="setBlockStyle"
          >
            <option
              v-for="style in BLOCK_STYLES"
              :key="style"
              :value="style"
            >
              {{ styleLabel(style) }}
            </option>
          </select>
          <IconButton
            v-else
            data-rt-control
            :icon="control.icon!"
            :label="control.label"
            :shortcut="control.shortcut"
            :disabled="control.disabled"
            :aria-pressed="control.pressed === undefined ? undefined : control.pressed"
            :aria-expanded="control.expanded === undefined ? undefined : control.expanded"
            :tabindex="indexOf(control) === current ? 0 : -1"
            @mousedown.prevent
            @click="control.run()"
          />
        </template>
      </div>
    </template>
    <p
      class="qb-rt-toolbar__notice"
      role="status"
      aria-live="polite"
    >
      {{ notice }}
    </p>
    <TableGroup
      v-if="allowed.tables && active('table')"
      :editor="editor"
      :tick="tick"
    />
    <ImageGroup
      v-if="allowed.images && active('image')"
      :editor="editor"
      :tick="tick"
      @replace="picking = 'replace'"
    />
    <MediaPickerDialog
      v-if="picking"
      :id="pickerId"
      :site-id="siteId"
      kind="image"
      @choose="chooseImage"
      @close="closePicker"
    />
    <div
      v-if="linking"
      class="qb-rt-toolbar__link"
    >
      <LinkControl
        :site-id="siteId"
        :value="linkValue"
        :label="t('editor.richText.link')"
        @change="applyLink"
      />
    </div>
  </div>
</template>
