import type { BlockDefinition } from '../registry'
import { newFieldId } from '../../forms/fieldIds'
import FormBlock from './FormBlock.vue'

// 018 FR-001 to FR-008: a form visitors submit. Its fields are one property, edited as a list;
// what it does after a submission, who is told, and how long submissions are kept are the rest.
// A new form is complete enough to publish: a name and one required email field.
const definition: BlockDefinition = {
  type: 'form',
  labelKey: 'editor.blocks.form',
  icon: '✉',
  groups: [
    {
      labelKey: 'content',
      props: [
        { prop: 'name', control: 'text' },
        { prop: 'fields', control: 'formFields' },
        { prop: 'submitLabel', control: 'text' },
      ],
    },
    {
      labelKey: 'messages',
      props: [
        { prop: 'successMessage', control: 'multiline' },
        { prop: 'errorMessage', control: 'multiline' },
      ],
    },
    { labelKey: 'afterSubmit', props: [{ prop: 'redirect', control: 'formRedirect' }] },
    { labelKey: 'notifications', props: [{ prop: 'notify', control: 'formNotify' }] },
    { labelKey: 'submissions', props: [{ prop: 'retentionDays', control: 'formRetention' }] },
  ],
  initialProps: (t) => ({
    name: t('editor.form.defaultName'),
    fields: [{ id: newFieldId(), type: 'email', label: t('editor.form.defaultEmailLabel'), required: true }],
  }),
  component: FormBlock,
}

export default definition
