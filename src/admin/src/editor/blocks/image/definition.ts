import type { BlockDefinition } from '../registry'
import ImageBlock from './ImageBlock.vue'

const definition: BlockDefinition = {
  type: 'image',
  labelKey: 'editor.blocks.image',
  icon: '▣',
  groups: [
    {
      labelKey: 'content',
      props: [
        { prop: 'media', control: 'media' },
        { prop: 'alt', control: 'text' },
        { prop: 'decorative', control: 'toggle' },
      ],
    },
    {
      labelKey: 'layout',
      props: [
        { prop: 'width', control: 'choice' },
        { prop: 'link', control: 'link' },
      ],
    },
  ],
  initialProps: () => ({}),
  component: ImageBlock,
}

export default definition
