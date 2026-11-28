<script setup lang="ts">
// The properties of the selected block, generated from its definition and the schema
// (FR-018): one control per property, grouped as the block's definition says, with the
// schema's limits and defaults. No block type has a hand-written panel.
//
// Every change goes through an operation. A refused value leaves the block as it was and
// shows why, next to the field (FR-021). Typing is grouped for undo like typing on the page.
import { computed, reactive, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { definitionOf, type ControlKind } from '../blocks/registry'
import { setLabel, setProp, type OpRefusal } from '../ops'
import { defaultOf, propNames, propSchema, requiredProps, resolvedPropSchema } from '../schema/schema'
import type { Json } from '../schema/subsetValidator'
import { useEditorStore } from '../state/editorStore'
import { refusalKey } from '../editorErrors'
import ChoiceControl from './controls/ChoiceControl.vue'
import LinkControl from './controls/LinkControl.vue'
import MediaControl from './controls/MediaControl.vue'
import RichTextControl from './controls/RichTextControl.vue'
import ListFiltersControl from './controls/ListFiltersControl.vue'
import ListPagingControl from './controls/ListPagingControl.vue'
import ListSortControl from './controls/ListSortControl.vue'
import ListSourceControl from './controls/ListSourceControl.vue'
import TextControl from './controls/TextControl.vue'
import ToggleControl from './controls/ToggleControl.vue'
import TokenControl from './controls/TokenControl.vue'
import BindingControl from './controls/BindingControl.vue'
import MenuControl from './controls/MenuControl.vue'
import NavCollapseControl from './controls/NavCollapseControl.vue'
import MenuLookControl from './controls/MenuLookControl.vue'
import NavContrastNote from './controls/NavContrastNote.vue'
import FormFieldsControl from './controls/FormFieldsControl.vue'
import FormRedirectControl from './controls/FormRedirectControl.vue'
import FormNotifyControl from './controls/FormNotifyControl.vue'
import FormRetentionControl from './controls/FormRetentionControl.vue'
import BorderControl from './controls/BorderControl.vue'
import { formSetupProblems } from '../forms/formSetup'
import { kindOf } from '../bindings/bindingRules'

const { t, te } = useI18n()
const store = useEditorStore()

const node = computed(() => store.selectedNode)
const definition = computed(() => (node.value ? definitionOf(node.value.type) : undefined))
const errors = reactive<Record<string, string>>({})

// A new selection starts with no refusals showing.
watch(() => node.value?.id, () => {
  for (const key of Object.keys(errors)) delete errors[key]
})

const TEXTUAL: ReadonlySet<ControlKind> = new Set(['text', 'multiline', 'embedUrl'])

interface Field {
  prop: string
  control: ControlKind
  id: string
  label: string
  schema: ReturnType<typeof resolvedPropSchema>
  value: Json | undefined
  defaultValue: Json | undefined
  required: boolean
}

const groups = computed(() => {
  const current = node.value
  const def = definition.value
  if (!current || !def) return []
  const required = requiredProps(current.type)

  const field = (prop: string, control: ControlKind): Field => ({
    prop,
    control,
    id: `prop-${current.id}-${prop}`,
    // A block type may name a property its own way: a list's "columns" is a count, not widths.
    label: te(`editor.panel.typeProps.${current.type}.${prop}`) ? t(`editor.panel.typeProps.${current.type}.${prop}`) : t(`editor.panel.props.${prop}`),
    schema: resolvedPropSchema(current.type, prop),
    value: current.props[prop],
    defaultValue: defaultOf(current.type, prop),
    required: required.includes(prop),
  })

  return [
    ...def.groups.map((group) => ({ key: group.labelKey, label: t(`editor.panel.groups.${group.labelKey}`), fields: group.props.map((p) => field(p.prop, p.control)) })),
    // 066 FR-008: every block with a border shows the same group, after its own (BB-08).
    ...(propNames(current.type).includes('border')
      ? [{ key: 'border', label: t('editor.panel.groups.border'), fields: [field('border', 'border' as ControlKind)] }]
      : []),
    // Every block can be named for the layer view; the component reference (010) cannot.
    ...(propNames(current.type).includes('layerLabel')
      ? [{ key: 'layer', label: t('editor.panel.groups.layer'), fields: [field('layerLabel', 'text' as ControlKind)] }]
      : []),
  ]
})

/** Why a value was refused, in words, from the schema keyword that failed. */
function reason(prop: string, refusal: OpRefusal, schema: Field['schema']): string {
  if (refusal.code !== 'invalid_value') return t(refusalKey(refusal.code))

  const specific = `editor.panel.errors.${prop}`
  if (refusal.keyword === 'pattern' && te(specific)) return t(specific)
  if (refusal.keyword === 'maxLength') return t('editor.panel.errors.maxLength', { max: schema.maxLength })
  if (refusal.keyword === 'minLength') return t('editor.panel.errors.minLength', { min: schema.minLength })
  if (refusal.keyword === 'maximum' || refusal.keyword === 'minimum') {
    return t('editor.panel.errors.range', { min: schema.minimum, max: schema.maximum })
  }
  return t('editor.panel.errors.invalid')
}

function change(field: Field, value: Json | undefined): void {
  const current = node.value
  if (!current || !store.root) return

  const result = field.prop === 'layerLabel'
    ? setLabel(store.root, current.id, typeof value === 'string' ? value : '')
    : setProp(store.root, current.id, field.prop, value)
  const refused = store.apply(result, TEXTUAL.has(field.control) ? { coalesce: `${current.id}:${field.prop}` } : {})

  if (refused) errors[field.prop] = reason(field.prop, refused, field.schema)
  else delete errors[field.prop]
}

/**
 * A file chosen from the library (013 FR-009): the file, and the library's alternative
 * text when the block has none of its own and is not decorative. One change, so one undo.
 */
function chooseMedia(field: Field, value: Json, altText: string | null): void {
  const current = node.value
  if (!current || !store.root) return

  let result = setProp(store.root, current.id, field.prop, value)
  const ownAlt = current.props.alt
  const needsAlt = (typeof ownAlt !== 'string' || ownAlt.trim() === '') && current.props.decorative !== true
  if (result.ok && altText && needsAlt && propNames(current.type).includes('alt')) {
    const withAlt = setProp(result.root, current.id, 'alt', altText)
    if (withAlt.ok) result = withAlt
  }

  const refused = store.apply(result)
  if (refused) errors[field.prop] = reason(field.prop, refused, field.schema)
  else delete errors[field.prop]
}

/** A Section's or Container's border has no inner spacing: their schema refers to `borderBox` (066). */
const boxOnly = (type: string): boolean => {
  const written = propSchema(type, 'border')
  return typeof written === 'object' && written !== null && !Array.isArray(written) && written.$ref === '#/$defs/borderBox'
}

const tokenKind = (control: ControlKind) => (control === 'colorToken' ? 'color' : control === 'spaceToken' ? 'space' : 'text')

/**
 * 018 FR-008, research FS-04: what stops a form being published, each with the field it
 * concerns. Publishing refuses the same problems (page-schema.md §3 rule 28).
 */
const formProblems = computed(() => {
  const current = node.value
  if (current?.type !== 'form') return []
  const fields = Array.isArray(current.props.fields) ? current.props.fields : []
  const labelOf = (fieldId: string | null) => {
    const field = fields.find((f) => typeof f === 'object' && f !== null && !Array.isArray(f) && f.id === fieldId)
    return field && typeof field === 'object' && !Array.isArray(field) && typeof field.label === 'string' && field.label ? field.label : t('editor.form.fields.newLabel')
  }
  return formSetupProblems(current.props).map((problem) => ({
    ...problem,
    text: t(`editor.form.problems.${problem.rule}`, { label: labelOf(problem.fieldId) }),
    label: labelOf(problem.fieldId),
    known: fields.some((f) => typeof f === 'object' && f !== null && !Array.isArray(f) && f.id === problem.fieldId),
  }))
})

/** Moves the focus to a field in the form's field list, ready to edit. */
function goToField(fieldId: string): void {
  document.querySelector<HTMLElement>(`.editor-panel [data-field-id="${CSS.escape(fieldId)}"] [data-part="edit"]`)?.focus()
}
</script>

<template>
  <section
    class="editor-panel"
    :aria-label="t('editor.panel.title')"
  >
    <h2 class="editor-panel-title">
      {{ t('editor.panel.title') }}
    </h2>

    <p
      v-if="!node"
      class="help"
    >
      {{ t('editor.panel.nothingSelected') }}
    </p>

    <template v-else-if="definition">
      <p class="editor-panel__type">
        {{ t(definition.labelKey) }}
      </p>
      <p v-if="node.type === 'component-ref'">
        <RouterLink
          class="button-link"
          :to="{ name: 'component-editor', params: { siteId: store.siteId, componentId: String(node.props.componentId) } }"
        >
          {{ t('editor.components.open') }}
        </RouterLink>
      </p>
      <p
        v-if="store.readOnly"
        class="help"
      >
        {{ t('editor.panel.readOnly') }}
      </p>
      <div
        v-if="formProblems.length > 0"
        class="form-problems"
        role="status"
      >
        <p>{{ t('editor.form.problems.title') }}</p>
        <ul>
          <li
            v-for="(problem, index) in formProblems"
            :key="index"
          >
            {{ problem.text }}
            <button
              v-if="problem.fieldId && problem.known"
              type="button"
              class="link"
              @click="goToField(problem.fieldId)"
            >
              {{ t('editor.form.problems.goTo', { label: problem.label }) }}
            </button>
          </li>
        </ul>
      </div>

      <fieldset
        v-for="group in groups"
        :key="group.key"
        class="editor-panel__group"
        :disabled="!!store.readOnly"
      >
        <legend>{{ group.label }}</legend>

        <template
          v-for="field in group.fields"
          :key="field.prop"
        >
          <TextControl
            v-if="field.control === 'text' || field.control === 'multiline' || field.control === 'embedUrl'"
            v-bind="field"
            :site-id="store.siteId"
            :multiline="field.control === 'multiline'"
            :help="field.control === 'embedUrl' ? t('editor.panel.embedHelp') : undefined"
            :error="errors[field.prop]"
            @change="change(field, $event)"
          />
          <ChoiceControl
            v-else-if="field.control === 'choice' || field.control === 'number'"
            v-bind="field"
            :site-id="store.siteId"
            :error="errors[field.prop]"
            @change="change(field, $event)"
          />
          <ToggleControl
            v-else-if="field.control === 'toggle'"
            v-bind="field"
            :site-id="store.siteId"
            :error="errors[field.prop]"
            @change="change(field, $event)"
          />
          <TokenControl
            v-else-if="field.control === 'colorToken' || field.control === 'spaceToken' || field.control === 'textSizeToken'"
            v-bind="field"
            :site-id="store.siteId"
            :kind="tokenKind(field.control)"
            :error="errors[field.prop]"
            @change="change(field, $event)"
          />
          <LinkControl
            v-else-if="field.control === 'link'"
            :id="field.id"
            :prop="field.prop"
            :label="field.label"
            :value="field.value"
            :site-id="store.siteId"
            :error="errors[field.prop]"
            @change="change(field, $event)"
          />
          <MediaControl
            v-else-if="field.control === 'media'"
            :id="field.id"
            :prop="field.prop"
            :label="field.label"
            :value="field.value"
            :site-id="store.siteId"
            :error="errors[field.prop]"
            @change="change(field, $event)"
            @choose="(value: Json, altText: string | null) => chooseMedia(field, value, altText)"
          />
          <ListSourceControl
            v-else-if="field.control === 'listSource'"
            v-bind="field"
            :site-id="store.siteId"
            :error="errors[field.prop]"
            @change="change(field, $event)"
          />
          <ListFiltersControl
            v-else-if="field.control === 'listFilters'"
            v-bind="field"
            :site-id="store.siteId"
            :error="errors[field.prop]"
            @change="change(field, $event)"
          />
          <ListSortControl
            v-else-if="field.control === 'listSort'"
            v-bind="field"
            :site-id="store.siteId"
            :error="errors[field.prop]"
            @change="change(field, $event)"
          />
          <ListPagingControl
            v-else-if="field.control === 'listPaging'"
            v-bind="field"
            :site-id="store.siteId"
            :error="errors[field.prop]"
            @change="change(field, $event)"
          />
          <MenuControl
            v-else-if="field.control === 'menu'"
            v-bind="field"
            :site-id="store.siteId"
            :error="errors[field.prop]"
            @change="change(field, $event)"
          />
          <NavCollapseControl
            v-else-if="field.control === 'navCollapse'"
            v-bind="field"
            :site-id="store.siteId"
            :error="errors[field.prop]"
            @change="change(field, $event)"
          />
          <MenuLookControl
            v-else-if="field.control === 'menuLook'"
            v-bind="field"
            :site-id="store.siteId"
            :error="errors[field.prop]"
            @change="change(field, $event)"
          />
          <TokenControl
            v-else-if="field.control === 'menuColor'"
            v-bind="field"
            :site-id="store.siteId"
            kind="color"
            :inherit-label="t('editor.panel.token.siteMenuStyle')"
            :error="errors[field.prop]"
            @change="change(field, $event)"
          />
          <FormFieldsControl
            v-else-if="field.control === 'formFields'"
            v-bind="field"
            :site-id="store.siteId"
            :error="errors[field.prop]"
            @change="change(field, $event)"
          />
          <FormRedirectControl
            v-else-if="field.control === 'formRedirect'"
            v-bind="field"
            :site-id="store.siteId"
            :error="errors[field.prop]"
            @change="change(field, $event)"
          />
          <FormNotifyControl
            v-else-if="field.control === 'formNotify'"
            v-bind="field"
            :site-id="store.siteId"
            :error="errors[field.prop]"
            @change="change(field, $event)"
          />
          <FormRetentionControl
            v-else-if="field.control === 'formRetention'"
            v-bind="field"
            :site-id="store.siteId"
            :form-id="node.id"
            :error="errors[field.prop]"
            @change="change(field, $event)"
          />
          <BorderControl
            v-else-if="field.control === 'border'"
            v-bind="field"
            :site-id="store.siteId"
            :box-only="boxOnly(node.type)"
            :error="errors[field.prop]"
            @change="change(field, $event)"
          />
          <RichTextControl
            v-else-if="field.control === 'richText'"
            :id="field.id"
            :prop="field.prop"
            :label="field.label"
            :node-id="node.id"
            :error="errors[field.prop]"
          />
          <!-- 016 FR-017: a bindable property inside a template or a list card can take an item's value. -->
          <BindingControl
            v-if="kindOf(node.type, field.prop)"
            :node="node"
            :prop="field.prop"
            :label="field.label"
          />
        </template>
        <!-- 024 FR-017d: the block's colour pairs below the contrast minimums; a warning only. -->
        <NavContrastNote
          v-if="node.type === 'navigation' && group.key === 'style'"
          :node="node"
        />
      </fieldset>
    </template>
  </section>
</template>
