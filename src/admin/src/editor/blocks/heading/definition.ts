import type { BlockDefinition } from '../registry'
import HeadingBlock from './HeadingBlock.vue'

const definition: BlockDefinition = {
  type: 'heading',
  labelKey: 'editor.blocks.heading',
  icon: 'H',
  groups: [
    {
      labelKey: 'content',
      props: [
        { prop: 'text', control: 'text' },
        { prop: 'level', control: 'number' },
      ],
    },
    {
      labelKey: 'style',
      props: [
        { prop: 'align', control: 'choice' },
        { prop: 'color', control: 'colorToken' },
      ],
    },
  ],
  initialProps: () => ({ text: '' }),
  component: HeadingBlock,
  inlineText: 'text',
}

export default definition
