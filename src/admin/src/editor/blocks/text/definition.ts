import type { BlockDefinition } from '../registry'
import TextBlock from './TextBlock.vue'

const definition: BlockDefinition = {
  type: 'text',
  labelKey: 'editor.blocks.text',
  icon: '¶',
  groups: [
    { labelKey: 'content', props: [{ prop: 'text', control: 'multiline' }] },
    {
      labelKey: 'style',
      props: [
        { prop: 'size', control: 'textSizeToken' },
        { prop: 'align', control: 'choice' },
        { prop: 'color', control: 'colorToken' },
      ],
    },
  ],
  initialProps: () => ({ text: '' }),
  component: TextBlock,
  inlineText: 'text',
}

export default definition
