<script setup lang="ts">
// A Form block on the canvas (018 FR-001, FR-009): the published page's markup, drawn with the
// same classes so the same stylesheet lays it out (page-schema.md §9), with every control
// disabled so nothing is typed into or sent from the editor. Default texts are the English
// ones visitors read (public-forms.md §2.3); the published page uses the site's language.
// A form that publishing would refuse (rule 28) says so on the canvas; the panel lists why.
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ContentNode } from '../../schema/schema'
import { isObject, type JsonObject } from '../../schema/subsetValidator'
import { formSetupProblems } from '../../forms/formSetup'
import { useSitePages } from '../../state/useSitePages'
import { useBlock } from '../useBlock'
import { useBorder } from '../useBorder'

const props = defineProps<{ node: ContentNode }>()
const { t } = useI18n()
const { store } = useBlock(() => props.node)
// 066: a border frames the whole form, its messages included.
const { classes: borderClasses, style: borderStyle } = useBorder(() => props.node)
const pages = useSitePages(store.siteId)

const text = (value: unknown, fallback: string): string => (typeof value === 'string' && value.length > 0 ? value : fallback)

const fields = computed(() => (Array.isArray(props.node.props.fields) ? props.node.props.fields.filter(isObject) : []))
const success = computed(() => text(props.node.props.successMessage, t('editor.form.public.success')))
const submit = computed(() => text(props.node.props.submitLabel, t('editor.form.public.submit')))
const incomplete = computed(() => formSetupProblems(props.node.props).length > 0)

const base = computed(() => `qb-canvas-${props.node.id}`)
const inputId = (field: JsonObject) => `${base.value}-f-${String(field.id)}`
const required = (field: JsonObject) => (field.required === undefined ? field.type === 'consent' : field.required === true)
const options = (field: JsonObject) => (Array.isArray(field.options) ? field.options.filter(isObject) : [])
const help = (field: JsonObject) => (typeof field.help === 'string' ? field.help : '')

const INPUT_TYPES: Record<string, string> = { email: 'email', phone: 'tel', number: 'number', date: 'date' }

/** A consent's link, as published: only to a page the site's visitors can reach. */
function consentLink(field: JsonObject): { href: string; title: string } | null {
  if (typeof field.linkPageId !== 'string') return null
  const page = pages.value.find((p) => p.id === field.linkPageId)
  if (!page || page.publication.state === 'draft') return null
  return { href: page.path, title: page.title || page.path }
}
</script>

<template>
  <div
    class="qb-form"
    :class="borderClasses"
    :style="borderStyle"
  >
    <div
      class="qb-form__sent"
      role="status"
    >
      <p>{{ success }}</p>
    </div>
    <form
      class="qb-form__form"
      novalidate
      @submit.prevent
    >
      <input
        type="hidden"
        name="_page"
      >
      <input
        type="hidden"
        name="_path"
      >
      <div
        class="qb-form__hp"
        aria-hidden="true"
      >
        <label :for="`${base}-hp`">{{ t('editor.form.public.trap') }}</label>
        <input
          :id="`${base}-hp`"
          name="_hp"
          type="text"
          tabindex="-1"
          autocomplete="off"
          disabled
        >
      </div>
      <template
        v-for="field in fields"
        :key="String(field.id)"
      >
        <fieldset
          v-if="field.type === 'radio' || field.type === 'checkboxes'"
          class="qb-form__field qb-form__group"
          disabled
        >
          <legend class="qb-form__label">
            {{ field.label }}<span
              v-if="required(field)"
              class="qb-form__req"
            >{{ ' ' + t('editor.form.public.required') }}</span>
          </legend>
          <p
            v-if="help(field)"
            class="qb-form__help"
          >
            {{ help(field) }}
          </p>
          <div
            v-for="(option, i) in options(field)"
            :key="i"
            class="qb-form__choice"
          >
            <input
              :id="`${inputId(field)}-${i}`"
              :type="field.type === 'radio' ? 'radio' : 'checkbox'"
              :value="String(option.value)"
              disabled
            ><label :for="`${inputId(field)}-${i}`">{{ option.label }}</label>
          </div>
        </fieldset>
        <div
          v-else-if="field.type === 'checkbox' || field.type === 'consent'"
          class="qb-form__field qb-form__check"
        >
          <input
            :id="inputId(field)"
            type="checkbox"
            disabled
          ><label
            class="qb-form__label"
            :for="inputId(field)"
          >{{ field.type === 'consent' ? field.statement : field.label }}<span
            v-if="required(field)"
            class="qb-form__req"
          >{{ ' ' + t('editor.form.public.required') }}</span></label>
          <p
            v-if="help(field)"
            class="qb-form__help"
          >
            {{ help(field) }}
          </p>
          <p
            v-if="consentLink(field)"
            class="qb-form__help"
          >
            <a
              class="qb-form__link"
              :href="consentLink(field)!.href"
              tabindex="-1"
              @click.prevent
            >{{ consentLink(field)!.title }}</a>
          </p>
        </div>
        <div
          v-else
          class="qb-form__field"
        >
          <label
            class="qb-form__label"
            :for="inputId(field)"
          >{{ field.label }}<span
            v-if="required(field)"
            class="qb-form__req"
          >{{ ' ' + t('editor.form.public.required') }}</span></label>
          <p
            v-if="help(field)"
            class="qb-form__help"
          >
            {{ help(field) }}
          </p>
          <textarea
            v-if="field.type === 'textarea'"
            :id="inputId(field)"
            class="qb-form__input"
            rows="5"
            :placeholder="typeof field.placeholder === 'string' ? field.placeholder : undefined"
            disabled
          />
          <select
            v-else-if="field.type === 'select'"
            :id="inputId(field)"
            class="qb-form__input"
            disabled
          >
            <option value="" />
            <option
              v-for="(option, i) in options(field)"
              :key="i"
              :value="String(option.value)"
            >
              {{ option.label }}
            </option>
          </select>
          <input
            v-else
            :id="inputId(field)"
            class="qb-form__input"
            :type="INPUT_TYPES[String(field.type)] ?? 'text'"
            :placeholder="typeof field.placeholder === 'string' ? field.placeholder : undefined"
            disabled
          >
        </div>
      </template>
      <div class="qb-form__actions">
        <button
          class="qb-form__submit"
          type="submit"
          disabled
        >
          {{ submit }}
        </button>
      </div>
    </form>
    <p
      v-if="incomplete"
      class="qb-editor-form-note"
    >
      {{ t('editor.form.incomplete') }}
    </p>
  </div>
</template>
