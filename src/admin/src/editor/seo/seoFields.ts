// The editor's view of a page's search metadata (017 research SE-02, SE-05).
//
// The limits come from the schema, so the panel never disagrees with the validator. The
// fallbacks mirror the renderer's HeadBuilder, so the previews show what visitors and link
// previews will get.

import { resolvedPropSchema } from '../schema/schema'
import { isObject, type Json, type JsonObject } from '../schema/subsetValidator'

/** The texts a page can set, in the order the panel shows them. */
export const SEO_TEXT_FIELDS = ['title', 'description', 'ogTitle', 'ogDescription'] as const
export type SeoTextField = (typeof SEO_TEXT_FIELDS)[number]

/** Lengths search engines usually show; longer is allowed, with a warning (017 FR-004). */
export const RECOMMENDED_LENGTH: Record<SeoTextField, number | null> = {
  title: 60,
  description: 160,
  ogTitle: null,
  ogDescription: null,
}

/** The hard maximum of a field, from the page schema. */
export function maxLength(field: SeoTextField): number {
  const seo = resolvedPropSchema('root', 'seo')
  const properties = isObject(seo.properties) ? (seo.properties as JsonObject) : {}
  const schema = properties[field]
  return isObject(schema) && typeof schema.maxLength === 'number' ? schema.maxLength : 500
}

/** The page's metadata, or an empty object when it has none. */
export function seoOf(props: Readonly<Record<string, Json>>): JsonObject {
  return isObject(props.seo) ? (props.seo as JsonObject) : {}
}

export function text(seo: JsonObject, field: SeoTextField): string {
  const value = seo[field]
  return typeof value === 'string' ? value : ''
}

/** Counted as the server counts `maxLength`: in code points. */
export const lengthOf = (value: string): number => Array.from(value).length

export interface SitePresentation {
  name: string
  baseUrl: string
  titleSuffix: string | null
}

/** What the page's head will say, with the fallbacks of 017 FR-005. */
export function effective(seo: JsonObject, pageTitle: string, site: SitePresentation) {
  const title = text(seo, 'title') || pageTitle + (site.titleSuffix ?? '')
  const description = text(seo, 'description')
  return {
    title,
    description,
    ogTitle: text(seo, 'ogTitle') || title,
    ogDescription: text(seo, 'ogDescription') || description,
  }
}

/** The page's address on its site, as the canonical address shows it. */
export function addressOf(baseUrl: string, path: string): string {
  return baseUrl.replace(/\/+$/, '') + (path.startsWith('/') ? path : `/${path}`)
}
