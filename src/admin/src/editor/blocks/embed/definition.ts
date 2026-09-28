import type { BlockDefinition } from '../registry'
import EmbedBlock from './EmbedBlock.vue'

const definition: BlockDefinition = {
  type: 'embed',
  labelKey: 'editor.blocks.embed',
  icon: '▶',
  groups: [
    {
      labelKey: 'content',
      props: [
        { prop: 'url', control: 'embedUrl' },
        { prop: 'title', control: 'text' },
      ],
    },
    { labelKey: 'layout', props: [{ prop: 'aspectRatio', control: 'choice' }] },
  ],
  initialProps: () => ({}),
  component: EmbedBlock,
}

export default definition

/** Which supported provider an embed address belongs to (research ED-06); null for none. */
export function embedProvider(url: unknown): 'youtube' | 'vimeo' | null {
  if (typeof url !== 'string') return null
  if (/^https:\/\/((www\.)?youtube\.com|youtu\.be)\//.test(url)) return 'youtube'
  if (/^https:\/\/(www\.)?vimeo\.com\//.test(url)) return 'vimeo'
  return null
}
