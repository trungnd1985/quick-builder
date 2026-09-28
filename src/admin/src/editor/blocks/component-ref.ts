// The shared component reference (010) as the editor draws it (011 US7). It has no editable
// properties: what it shows is edited in the component itself, which the panel opens.
import type { BlockDefinition } from './registry'
import ComponentRefBlock from '../canvas/ComponentRefBlock.vue'

const definition: BlockDefinition = {
  type: 'component-ref',
  labelKey: 'editor.blocks.componentRef',
  icon: '◈',
  groups: [],
  initialProps: () => ({}),
  component: ComponentRefBlock,
}

export default definition
