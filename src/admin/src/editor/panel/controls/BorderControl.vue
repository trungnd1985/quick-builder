<script setup lang="ts">
// A block's whole border (066 FR-008, research BB-08): width, line style, colour, corner radius
// and, except on a Section or Container, inner spacing. One change emits the whole object, so it
// is one operation and one undo step (FR-011).
//
// Style and colour mean something only with a line, so they are disabled under width "none",
// but kept: choosing a width again brings them back. A border that would change nothing is
// stored as nothing, which removes the prop. Radius and inner spacing have no default (BB-02):
// unset, the block keeps its own corners and spacing.
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ControlProps } from './types'
import { definitionDefault } from '../../schema/schema'
import { isObject, type Json, type JsonObject } from '../../schema/subsetValidator'
import { useEditorStore } from '../../state/editorStore'
import { isDeletedToken, tokensOf, tokenValue, type TokenKind } from '../../tokens/tokenCss'

const props = defineProps<ControlProps & {
  /** A Section's or Container's border (`borderBox`), which has no inner spacing. */
  boxOnly?: boolean
}>()
const emit = defineEmits<{ change: [value: Json | undefined] }>()
const { t } = useI18n()
const store = useEditorStore()

const WIDTHS = ['none', 'thin', 'medium', 'thick'] as const
const STYLES = ['solid', 'dashed', 'dotted'] as const
const DEFAULT = ''

const current = computed<JsonObject>(() => (isObject(props.value) ? props.value : {}))
const defaultWidth = definitionDefault('border', 'width') as string
const defaultStyle = definitionDefault('border', 'style') as string
const defaultColor = definitionDefault('border', 'color')

const width = computed(() => (typeof current.value.width === 'string' ? current.value.width : defaultWidth))
const style = computed(() => (typeof current.value.style === 'string' ? current.value.style : defaultStyle))
const noLine = computed(() => width.value === 'none')

const tokenId = (value: Json | undefined): string =>
  isObject(value) && typeof value.token === 'string' ? value.token : DEFAULT

/** The selected token, or the default option when it is unset or was deleted. */
const selected = (key: 'color' | 'radius' | 'padding', kind: TokenKind): string =>
  isDeletedToken(current.value[key], kind, store.tokens) ? DEFAULT : tokenId(current.value[key])

const deleted = computed(() =>
  isDeletedToken(current.value.color, 'color', store.tokens)
  || isDeletedToken(current.value.radius, 'radius', store.tokens)
  || (!props.boxOnly && isDeletedToken(current.value.padding, 'space', store.tokens)),
)

const colorDefaultName = computed(() => {
  const id = tokenId(defaultColor)
  return store.tokens.colors.find((c) => c.id === id)?.name ?? t('editor.panel.token.none')
})
const swatch = computed(() => tokenValue(current.value.color, 'color', store.tokens, defaultColor, 'transparent'))

const idOf = (part: string) => `${props.id}-${part}`
const describedBy = (part: string) =>
  [noLine.value && (part === 'style' || part === 'color') ? idOf('noline') : '', deleted.value ? idOf('help') : '', props.error ? idOf('error') : '']
    .filter(Boolean)
    .join(' ') || undefined

/** Whether a border changes nothing: no line, no corners, no spacing, and no kept choices. */
function changesNothing(border: JsonObject): boolean {
  const keys = Object.keys(border)
  return keys.every((key) =>
    (key === 'width' && border.width === 'none')
    || (key === 'style' && border.style === defaultStyle)
    || (key === 'color' && tokenId(border.color) === tokenId(defaultColor)))
}

function update(key: string, value: Json | undefined): void {
  const next: JsonObject = { ...current.value }
  if (value === undefined) delete next[key]
  else next[key] = value
  emit('change', changesNothing(next) ? undefined : next)
}

const onChoice = (key: 'width' | 'style') => (event: Event) => update(key, (event.target as HTMLSelectElement).value)
const onToken = (key: 'color' | 'radius' | 'padding') => (event: Event) => {
  const id = (event.target as HTMLSelectElement).value
  update(key, id === DEFAULT ? undefined : { token: id })
}
</script>

<template>
  <div
    class="editor-field editor-border"
    :class="{ 'editor-field--invalid': !!error }"
    data-prop="border"
  >
    <div class="editor-border__part">
      <label
        class="editor-field__label"
        :for="idOf('width')"
      >{{ t('editor.panel.border.width') }}</label>
      <select
        :id="idOf('width')"
        :value="width"
        :aria-describedby="describedBy('width')"
        @change="onChoice('width')($event)"
      >
        <option
          v-for="option in WIDTHS"
          :key="option"
          :value="option"
        >
          {{ t(`editor.panel.border.widths.${option}`) }}
        </option>
      </select>
    </div>

    <div class="editor-border__part">
      <label
        class="editor-field__label"
        :for="idOf('style')"
      >{{ t('editor.panel.border.style') }}</label>
      <select
        :id="idOf('style')"
        :value="style"
        :disabled="noLine"
        :aria-describedby="describedBy('style')"
        @change="onChoice('style')($event)"
      >
        <option
          v-for="option in STYLES"
          :key="option"
          :value="option"
        >
          {{ t(`editor.panel.border.styles.${option}`) }}
        </option>
      </select>
    </div>

    <div class="editor-border__part">
      <label
        class="editor-field__label"
        :for="idOf('color')"
      >{{ t('editor.panel.border.color') }}</label>
      <div class="editor-token">
        <span
          class="editor-token__swatch"
          :style="{ background: swatch }"
          aria-hidden="true"
        />
        <select
          :id="idOf('color')"
          :value="selected('color', 'color')"
          :disabled="noLine"
          :aria-describedby="describedBy('color')"
          @change="onToken('color')($event)"
        >
          <option :value="DEFAULT">
            {{ t('editor.panel.border.colorDefault', { name: colorDefaultName }) }}
          </option>
          <option
            v-for="token in tokensOf(store.tokens, 'color')"
            :key="token.id"
            :value="token.id"
          >
            {{ token.name }}
          </option>
        </select>
      </div>
    </div>

    <p
      v-if="noLine"
      :id="idOf('noline')"
      class="help"
    >
      {{ t('editor.panel.border.noLine') }}
    </p>

    <div class="editor-border__part">
      <label
        class="editor-field__label"
        :for="idOf('radius')"
      >{{ t('editor.panel.border.radius') }}</label>
      <select
        :id="idOf('radius')"
        :value="selected('radius', 'radius')"
        :aria-describedby="describedBy('radius')"
        @change="onToken('radius')($event)"
      >
        <option :value="DEFAULT">
          {{ t('editor.panel.border.radiusDefault') }}
        </option>
        <option
          v-for="token in tokensOf(store.tokens, 'radius')"
          :key="token.id"
          :value="token.id"
        >
          {{ token.name }}
        </option>
      </select>
    </div>

    <div
      v-if="!boxOnly"
      class="editor-border__part"
    >
      <label
        class="editor-field__label"
        :for="idOf('padding')"
      >{{ t('editor.panel.border.padding') }}</label>
      <select
        :id="idOf('padding')"
        :value="selected('padding', 'space')"
        :aria-describedby="describedBy('padding')"
        @change="onToken('padding')($event)"
      >
        <option :value="DEFAULT">
          {{ t('editor.panel.border.paddingNone') }}
        </option>
        <option
          v-for="token in tokensOf(store.tokens, 'space')"
          :key="token.id"
          :value="token.id"
        >
          {{ token.name }}
        </option>
      </select>
    </div>

    <p
      v-if="deleted"
      :id="idOf('help')"
      class="help"
    >
      {{ t('editor.panel.border.deleted') }}
    </p>
    <p
      v-if="error"
      :id="idOf('error')"
      class="field-error"
      role="alert"
    >
      {{ error }}
    </p>
  </div>
</template>
