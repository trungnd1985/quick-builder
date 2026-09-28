import type { BlockDefinition } from '../registry'
import RichTextBlock from './RichTextBlock.vue'

const definition: BlockDefinition = {
  type: 'rich-text',
  labelKey: 'editor.blocks.richText',
  icon: '≡',
  groups: [
    { labelKey: 'content', props: [{ prop: 'content', control: 'richText' }] },
    {
      labelKey: 'style',
      props: [
        { prop: 'align', control: 'choice' },
        { prop: 'color', control: 'colorToken' },
      ],
    },
  ],
  initialProps: () => ({ content: { type: 'doc', content: [] } }),
  component: RichTextBlock,
}

export default definition
