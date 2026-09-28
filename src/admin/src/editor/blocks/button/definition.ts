import type { BlockDefinition } from '../registry'
import ButtonBlock from './ButtonBlock.vue'

const definition: BlockDefinition = {
  type: 'button',
  labelKey: 'editor.blocks.button',
  icon: '⬭',
  groups: [
    {
      labelKey: 'content',
      props: [
        { prop: 'label', control: 'text' },
        { prop: 'link', control: 'link' },
        { prop: 'newTab', control: 'toggle' },
      ],
    },
    {
      labelKey: 'style',
      props: [
        { prop: 'style', control: 'choice' },
        { prop: 'align', control: 'choice' },
      ],
    },
  ],
  initialProps: (t) => ({ label: t('editor.blocks.buttonInitialLabel') }),
  component: ButtonBlock,
  inlineText: 'label',
}

export default definition
