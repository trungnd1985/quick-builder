// The Collection list (016 research TB-07): a card repeated once per listed item, and an
// empty state. Its source, filters, sort keys and paging have their own controls.
import { newNodeId } from '../../ops/ids'
import type { ContentNode } from '../../schema/schema'
import { isObject, type Json } from '../../schema/subsetValidator'
import type { BlockDefinition, InsertContext } from '../registry'
import CollectionListBlock from './CollectionListBlock.vue'

function itemHeading(context: InsertContext, props: Record<string, Json>): ContentNode {
  const source = isObject(props.source) && typeof props.source.collectionId === 'string' ? props.source.collectionId : null
  const nameField = source ? context.nameFieldOf?.(source) ?? null : null
  return {
    id: newNodeId(),
    type: 'heading',
    props: {
      text: '',
      level: 3,
      bind: {
        text: { value: nameField ? { field: nameField } : { item: 'slug' } },
        link: { value: { item: 'url' } },
      },
    },
    children: [],
  }
}

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
  // FR-022: the card starts with a Heading showing each item's name (its slug when the
  // collection has no name field), linked to the item's page, so a new list already leads to
  // the details pages.
  initialChildren: (t, context, props) => [
    { id: newNodeId(), type: 'list-card', props: {}, children: [itemHeading(context, props)] },
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
