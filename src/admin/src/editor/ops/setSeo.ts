import { pageSchema, propSchema, type ContentNode } from '../schema/schema'
import { isObject, type Json, type JsonObject } from '../schema/subsetValidator'
import { checkSize } from './limits'
import { done, refuse, type OpResult } from './types'

/**
 * Sets one field of the page's search metadata, `root.props.seo` (017 research SE-02), or
 * removes it when `value` is undefined. The metadata is checked against the schema first, and a
 * refused value leaves the document as it was. When the last field goes, `seo` goes with it, so
 * a page without metadata stores none.
 *
 * `field` may be a path into `seo`, such as `bind.title` or `fallback.description`.
 */
export function setSeo(root: ContentNode, field: string, value: Json | undefined, selection: string | null = null): OpResult {
  const current = isObject(root.props.seo) ? (root.props.seo as JsonObject) : {}
  const next = withField(current, field.split('.'), value)

  if (Object.keys(next).length > 0) {
    const failure = pageSchema.checkAgainst(propSchema('root', 'seo'), next)
    if (failure) return refuse('invalid_value', { keyword: failure.keyword, pointer: failure.location })
  }

  const props = { ...root.props }
  if (Object.keys(next).length === 0) delete props.seo
  else props.seo = next

  if (JSON.stringify(props) === JSON.stringify(root.props)) return done(root, selection)

  const updated: ContentNode = { ...root, props }
  return checkSize(updated) ?? done(updated, selection)
}

/** A copy of `object` with the value at `path` set, or removed with any object it leaves empty. */
function withField(object: JsonObject, path: string[], value: Json | undefined): JsonObject {
  const [head, ...rest] = path
  if (head === undefined) return object

  const copy: JsonObject = { ...object }
  if (rest.length === 0) {
    if (value === undefined) delete copy[head]
    else copy[head] = value
    return copy
  }

  const child = withField(isObject(copy[head]) ? (copy[head] as JsonObject) : {}, rest, value)
  if (Object.keys(child).length === 0) delete copy[head]
  else copy[head] = child
  return copy
}
