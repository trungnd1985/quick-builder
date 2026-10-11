// The Collection list (016 research TB-07): a card repeated once per listed item, and an
// empty state. Its source, filters, sort keys and paging have their own controls.
import { newNodeId } from '../../ops/ids'
import type { BlockDefinition } from '../registry'
import CollectionListBlock from './CollectionListBlock.vue'

const definition: BlockDefinition = {
  type: 'collection-list',
  labelKey: 'editor.blocks.collectionList',
  icon: '☰',
  groups: [
    {
      labelKey: 'content',
      props: [
        { prop: 'source', control: 'listSource' },
        { prop: 'filters', control: 'listFilters' },
        { prop: 'sort', control: 'listSort' },
        { prop: 'limit', control: 'number' },
        { prop: 'paging', control: 'listPaging' },
      ],
    },
    {
      labelKey: 'layout',
      props: [
        { prop: 'columns', control: 'number' },
        { prop: 'gap', control: 'spaceToken' },
      ],
    },
  ],
  // A list needs a source (rule: exactly one of collection and field), so it starts with the
  // site's first collection, changed at the top of the panel; without any, it is refused.
  initialProps: (_t, context) => (context.collectionIds[0] ? { source: { collectionId: context.collectionIds[0] } } : null),
  // Rule 20: a list holds exactly its card, then its empty state; the empty state says so.
  initialChildren: (t) => [
    { id: newNodeId(), type: 'list-card', props: {}, children: [] },
    {
      id: newNodeId(),
      type: 'list-empty',
      props: {},
      children: [{ id: newNodeId(), type: 'text', props: { text: t('editor.list.emptyText') }, children: [] }],
    },
  ],
  component: CollectionListBlock,
}

export default definition
