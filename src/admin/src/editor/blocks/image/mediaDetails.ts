// 013 MV-16: what the canvas needs to draw an image like a published page does — its
// dimensions and resized copies — fetched once per file and shared by every Image block that
// shows it. A failed or unknown file resolves to null, and the block falls back to 003's
// preview address.

import { mediaApi, type MediaFile } from '../../../api/media'

const cache = new Map<string, Promise<MediaFile | null>>()

export function mediaDetails(siteId: string, mediaId: string): Promise<MediaFile | null> {
  const key = `${siteId}/${mediaId}`
  let pending = cache.get(key)
  if (!pending) {
    pending = mediaApi.get(siteId, mediaId).catch(() => null)
    cache.set(key, pending)
  }

  return pending
}

/** Forgets a file, after it was replaced or its alternative text changed. */
export function forgetMediaDetails(siteId: string, mediaId: string): void {
  cache.delete(`${siteId}/${mediaId}`)
}

/** The `srcset` for a file's admin variants; undefined when it has none. */
export function sourceSet(file: Pick<MediaFile, 'variants'>): string | undefined {
  const variants = Array.isArray(file.variants) ? file.variants : []
  return variants.length === 0 ? undefined : variants.map((v) => `${v.path} ${v.width}w`).join(', ')
}

/** The canvas's default source: the largest variant no wider than 1,280 pixels, as published. */
export function defaultSource(file: Pick<MediaFile, 'variants'>): string | undefined {
  const variants = Array.isArray(file.variants) ? file.variants : []
  return [...variants].reverse().find((v) => v.width <= 1280)?.path
}
